using System.IO.Compression;
using System.Text.Json;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Core.Installation;

public sealed record InstallResult(string BackupDirectory, int InstalledFileCount);

public sealed class ModInstaller(HashService? hashService = null)
{
    private readonly HashService _hashService = hashService ?? new HashService();

    public async Task<InstallResult> InstallAsync(
        string gameRoot,
        string payloadZipPath,
        ModManifest manifest,
        string backupRoot,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> manifestErrors = manifest.Validate();
        if (manifestErrors.Count > 0)
        {
            throw new InvalidDataException(
                "A release manifest hibás: " + string.Join("; ", manifestErrors));
        }
        if (!Directory.Exists(gameRoot))
        {
            throw new DirectoryNotFoundException($"A game root nem létezik: {gameRoot}");
        }
        if (!File.Exists(payloadZipPath))
        {
            throw new FileNotFoundException("A payload ZIP nem található.", payloadZipPath);
        }
        if (!await _hashService.VerifyFileAsync(
                payloadZipPath,
                manifest.PayloadSha256,
                cancellationToken))
        {
            throw new InvalidDataException("A payload ZIP SHA-256 ellenőrzése sikertelen.");
        }

        Directory.CreateDirectory(backupRoot);
        string stagingRoot = Path.Combine(
            backupRoot,
            ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        try
        {
            await StageAndValidateAsync(
                payloadZipPath,
                manifest,
                stagingRoot,
                cancellationToken);

            string snapshotName =
                $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}-{Sanitize(manifest.Version)}-{Guid.NewGuid():N}";
            if (snapshotName.Length > 80) snapshotName = snapshotName[..80];
            string snapshotRoot = Path.Combine(backupRoot, snapshotName);
            Directory.CreateDirectory(snapshotRoot);

            var touchedTargets = new List<(string Target, string? Backup)>();
            var snapshotFiles = new List<SnapshotFile>();
            try
            {
                foreach (ModFileEntry file in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string targetPath = CombineSafe(gameRoot, file.Target);
                    string? backupPath = null;
                    bool hadOriginal = File.Exists(targetPath);
                    if (hadOriginal)
                    {
                        backupPath = CombineSafe(snapshotRoot, file.Target);
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                        File.Copy(targetPath, backupPath, overwrite: false);
                    }
                    touchedTargets.Add((targetPath, backupPath));
                    snapshotFiles.Add(new SnapshotFile(file.Target, hadOriginal));
                }

                await File.WriteAllTextAsync(
                    Path.Combine(snapshotRoot, ".snapshot.json"),
                    JsonSerializer.Serialize(new SnapshotMetadata(snapshotFiles)),
                    cancellationToken);

                foreach (ModFileEntry file in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string stagedPath = CombineSafe(stagingRoot, file.Source);
                    string targetPath = CombineSafe(gameRoot, file.Target);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    string temporaryTarget = targetPath + ".smmm-new-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Copy(stagedPath, temporaryTarget, overwrite: false);
                        File.Move(temporaryTarget, targetPath, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(temporaryTarget)) File.Delete(temporaryTarget);
                    }
                }
            }
            catch
            {
                foreach ((string target, string? backup) in touchedTargets.AsEnumerable().Reverse())
                {
                    if (backup is not null && File.Exists(backup))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(backup, target, overwrite: true);
                    }
                    else if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }
                throw;
            }

            return new InstallResult(snapshotRoot, manifest.Files.Count);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    public async Task RestoreAsync(
        string gameRoot,
        string snapshotDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(gameRoot))
        {
            throw new DirectoryNotFoundException($"A game root nem létezik: {gameRoot}");
        }
        if (!Directory.Exists(snapshotDirectory))
        {
            throw new DirectoryNotFoundException($"A backup snapshot nem létezik: {snapshotDirectory}");
        }

        string metadataPath = Path.Combine(snapshotDirectory, ".snapshot.json");
        if (!File.Exists(metadataPath))
        {
            throw new InvalidDataException("A backup snapshot metadata hiányzik.");
        }
        SnapshotMetadata metadata = JsonSerializer.Deserialize<SnapshotMetadata>(
            await File.ReadAllTextAsync(metadataPath, cancellationToken))
            ?? throw new InvalidDataException("A backup snapshot metadata hibás.");

        var restoreFiles = new List<(SnapshotFile Metadata, string Target, string? Backup)>();
        foreach (SnapshotFile file in metadata.Files)
        {
            string targetPath = CombineSafe(gameRoot, file.Target);
            string? backupPath = file.HadOriginal
                ? CombineSafe(snapshotDirectory, file.Target)
                : null;
            if (file.HadOriginal && !File.Exists(backupPath))
            {
                throw new InvalidDataException($"Hiányzó backup fájl: {file.Target}");
            }
            restoreFiles.Add((file, targetPath, backupPath));
        }

        string rollbackRoot = Path.Combine(
            Path.GetTempPath(),
            ".smmm-restore-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rollbackRoot);
        var rollbackFiles = new List<(string Target, string? Rollback, bool HadCurrent)>();
        try
        {
            foreach ((SnapshotFile file, string targetPath, _) in restoreFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool hadCurrent = File.Exists(targetPath);
                string? rollbackPath = null;
                if (hadCurrent)
                {
                    rollbackPath = CombineSafe(rollbackRoot, file.Target);
                    Directory.CreateDirectory(Path.GetDirectoryName(rollbackPath)!);
                    File.Copy(targetPath, rollbackPath, overwrite: false);
                }
                rollbackFiles.Add((targetPath, rollbackPath, hadCurrent));
            }

            foreach ((SnapshotFile file, string targetPath, string? backupPath) in restoreFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!file.HadOriginal)
                {
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                string temporaryTarget = targetPath + ".smmm-restore-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(backupPath!, temporaryTarget, overwrite: false);
                    File.Move(temporaryTarget, targetPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryTarget)) File.Delete(temporaryTarget);
                }
            }
        }
        catch
        {
            foreach ((string target, string? rollback, bool hadCurrent) in rollbackFiles.AsEnumerable().Reverse())
            {
                if (hadCurrent && rollback is not null && File.Exists(rollback))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(rollback, target, overwrite: true);
                }
                else if (!hadCurrent && File.Exists(target))
                {
                    File.Delete(target);
                }
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(rollbackRoot))
            {
                Directory.Delete(rollbackRoot, recursive: true);
            }
        }
    }

    private async Task StageAndValidateAsync(
        string payloadZipPath,
        ModManifest manifest,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(payloadZipPath);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            if (!ModManifest.IsSafeRelativePath(normalized))
            {
                throw new InvalidDataException($"Nem biztonságos ZIP útvonal: {entry.FullName}");
            }
            if (!entries.TryAdd(normalized, entry))
            {
                throw new InvalidDataException($"Duplikált ZIP entry: {entry.FullName}");
            }
        }

        foreach (ModFileEntry file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = file.Source.Replace('\\', '/');
            if (!entries.TryGetValue(source, out ZipArchiveEntry? entry)
                || string.IsNullOrEmpty(entry.Name))
            {
                throw new InvalidDataException($"Hiányzó payload fájl: {file.Source}");
            }

            string stagedPath = CombineSafe(stagingRoot, source);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            await using (Stream input = entry.Open())
            await using (FileStream output = File.Create(stagedPath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            if (!await _hashService.VerifyFileAsync(
                    stagedPath,
                    file.Sha256,
                    cancellationToken))
            {
                throw new InvalidDataException(
                    $"Fájl SHA-256 eltérés: {file.Source}");
            }
        }
    }

    private static string CombineSafe(string root, string relativePath)
    {
        if (!ModManifest.IsSafeRelativePath(relativePath))
        {
            throw new InvalidDataException($"Nem biztonságos relatív útvonal: {relativePath}");
        }

        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Az útvonal kilép a célkönyvtárból: {relativePath}");
        }
        return fullPath;
    }

    private static string Sanitize(string value) => string.Concat(
        value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private sealed record SnapshotMetadata(IReadOnlyList<SnapshotFile> Files);
    private sealed record SnapshotFile(string Target, bool HadOriginal);
}
