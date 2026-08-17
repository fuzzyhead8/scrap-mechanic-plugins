using System.IO.Compression;
using System.Text.Json;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Core.Installation;

public sealed record InstallResult(
    string BackupDirectory,
    int InstalledFileCount,
    bool CacheBundleInvalidated);

public sealed class ModInstaller(HashService? hashService = null)
{
    private const string CoreDataBundleRelativePath = "Cache/Bundle/core_data.cbo";
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
                "The release manifest is invalid: " + string.Join("; ", manifestErrors));
        }
        if (manifest.Files.Any(file => TargetsGeneratedCacheDirectory(file.Target)))
        {
            throw new InvalidDataException(
                "The generated Cache directory cannot be a payload target.");
        }
        if (!Directory.Exists(gameRoot))
        {
            throw new DirectoryNotFoundException($"The game root does not exist: {gameRoot}");
        }
        if (!File.Exists(payloadZipPath))
        {
            throw new FileNotFoundException("The payload ZIP was not found.", payloadZipPath);
        }
        if (!await _hashService.VerifyFileAsync(
                payloadZipPath,
                manifest.PayloadSha256,
                cancellationToken))
        {
            throw new InvalidDataException("Payload ZIP SHA-256 verification failed.");
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
            bool cacheBundleInvalidated;
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

                string? cacheBundlePath = BackupCoreDataBundle(
                    gameRoot,
                    snapshotRoot,
                    CoreDataBundleRelativePath);

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

                cacheBundleInvalidated = InvalidateCoreDataBundle(cacheBundlePath);
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

            return new InstallResult(
                snapshotRoot,
                manifest.Files.Count,
                cacheBundleInvalidated);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    public async Task<bool> RestoreAsync(
        string gameRoot,
        string snapshotDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(gameRoot))
        {
            throw new DirectoryNotFoundException($"The game root does not exist: {gameRoot}");
        }
        if (!Directory.Exists(snapshotDirectory))
        {
            throw new DirectoryNotFoundException($"The backup snapshot does not exist: {snapshotDirectory}");
        }

        string metadataPath = Path.Combine(snapshotDirectory, ".snapshot.json");
        if (!File.Exists(metadataPath))
        {
            throw new InvalidDataException("Backup snapshot metadata is missing.");
        }
        SnapshotMetadata metadata = JsonSerializer.Deserialize<SnapshotMetadata>(
            await File.ReadAllTextAsync(metadataPath, cancellationToken))
            ?? throw new InvalidDataException("Backup snapshot metadata is invalid.");

        var restoreFiles = new List<(SnapshotFile Metadata, string Target, string? Backup)>();
        foreach (SnapshotFile file in metadata.Files)
        {
            string targetPath = CombineSafe(gameRoot, file.Target);
            string? backupPath = file.HadOriginal
                ? CombineSafe(snapshotDirectory, file.Target)
                : null;
            if (file.HadOriginal && !File.Exists(backupPath))
            {
                throw new InvalidDataException($"Missing backup file: {file.Target}");
            }
            restoreFiles.Add((file, targetPath, backupPath));
        }

        string rollbackRoot = Path.Combine(
            Path.GetTempPath(),
            ".smmm-restore-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rollbackRoot);
        var rollbackFiles = new List<(string Target, string? Rollback, bool HadCurrent)>();
        bool cacheBundleInvalidated;
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

            const string cacheBackupRelativePath =
                ".cache-invalidations/core_data-before-restore.cbo";
            string? cacheBundlePath = BackupCoreDataBundle(
                gameRoot,
                snapshotDirectory,
                cacheBackupRelativePath,
                overwrite: true);

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

            cacheBundleInvalidated = InvalidateCoreDataBundle(cacheBundlePath);
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

        return cacheBundleInvalidated;
    }

    private async Task StageAndValidateAsync(
        string payloadZipPath,
        ModManifest manifest,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(payloadZipPath);
        var entries = new Dictionary<string, ZipArchiveEntry>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            if (!ModManifest.IsSafeRelativePath(normalized))
            {
                throw new InvalidDataException($"Unsafe ZIP path: {entry.FullName}");
            }
            if (!entries.TryAdd(normalized, entry))
            {
                throw new InvalidDataException($"Duplicate ZIP entry: {entry.FullName}");
            }
        }

        foreach (ModFileEntry file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = file.Source.Replace('\\', '/');
            if (!entries.TryGetValue(source, out ZipArchiveEntry? entry)
                || string.IsNullOrEmpty(entry.Name))
            {
                throw new InvalidDataException($"Missing payload file: {file.Source}");
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
                    $"File SHA-256 mismatch: {file.Source}");
            }
        }
    }

    private static bool TargetsGeneratedCacheDirectory(string relativePath)
    {
        string firstSegment = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)[0]
            .TrimEnd(' ', '.');
        return string.Equals(firstSegment, "Cache", StringComparison.OrdinalIgnoreCase)
            || firstSegment.StartsWith("CACHE~", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BackupCoreDataBundle(
        string gameRoot,
        string backupRoot,
        string backupRelativePath,
        bool overwrite = false)
    {
        string cacheBundlePath = CombineSafe(gameRoot, CoreDataBundleRelativePath);
        if (!File.Exists(cacheBundlePath)) return null;

        string backupPath = CombineSafe(backupRoot, backupRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(cacheBundlePath, backupPath, overwrite);
        return cacheBundlePath;
    }

    private static bool InvalidateCoreDataBundle(string? cacheBundlePath)
    {
        if (cacheBundlePath is null || !File.Exists(cacheBundlePath)) return false;

        File.Delete(cacheBundlePath);
        return true;
    }

    private static string CombineSafe(string root, string relativePath)
    {
        if (!ModManifest.IsSafeRelativePath(relativePath))
        {
            throw new InvalidDataException($"Unsafe relative path: {relativePath}");
        }

        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(fullRoot, pathComparison))
        {
            throw new InvalidDataException($"Path escapes the target directory: {relativePath}");
        }
        return fullPath;
    }

    private static string Sanitize(string value) => string.Concat(
        value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private sealed record SnapshotMetadata(IReadOnlyList<SnapshotFile> Files);
    private sealed record SnapshotFile(string Target, bool HadOriginal);
}
