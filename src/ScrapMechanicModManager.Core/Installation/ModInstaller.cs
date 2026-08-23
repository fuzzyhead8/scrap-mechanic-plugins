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
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly HashService _hashService = hashService ?? new HashService();

    public Task<InstallResult> InstallAsync(
        string gameRoot,
        string payloadZipPath,
        ModManifest manifest,
        string backupRoot,
        CancellationToken cancellationToken = default) =>
        InstallModulesAsync(
            gameRoot,
            [new ModuleInstallRequest(payloadZipPath, manifest)],
            backupRoot,
            cancellationToken);

    internal async Task<InstallResult> InstallModulesAsync(
        string gameRoot,
        IReadOnlyList<ModuleInstallRequest> modules,
        string backupRoot,
        CancellationToken cancellationToken = default)
    {
        ValidateModuleSet(modules);
        if (!Directory.Exists(gameRoot))
        {
            throw new DirectoryNotFoundException($"The game root does not exist: {gameRoot}");
        }

        foreach (ModuleInstallRequest module in modules)
        {
            if (!File.Exists(module.PayloadZipPath))
            {
                throw new FileNotFoundException(
                    "The payload ZIP was not found.",
                    module.PayloadZipPath);
            }
            if (!await _hashService.VerifyFileAsync(
                    module.PayloadZipPath,
                    module.Manifest.PayloadSha256,
                    cancellationToken))
            {
                throw new InvalidDataException(
                    $"Payload ZIP SHA-256 verification failed for {module.Manifest.ModId}.");
            }
        }

        Directory.CreateDirectory(backupRoot);
        string stagingRoot = Path.Combine(
            backupRoot,
            ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var stagedModules = new List<StagedModule>(modules.Count);
            for (int index = 0; index < modules.Count; index++)
            {
                ModuleInstallRequest module = modules[index];
                string moduleStagingRoot = Path.Combine(stagingRoot, $"module-{index}");
                Directory.CreateDirectory(moduleStagingRoot);
                await StageAndValidateAsync(
                    module.PayloadZipPath,
                    module.Manifest,
                    moduleStagingRoot,
                    cancellationToken);
                stagedModules.Add(new StagedModule(module, moduleStagingRoot));
            }

            string versionLabel = modules.Count == 1
                ? modules[0].Manifest.Version
                : $"modules-{modules[0].Manifest.Version}";
            string snapshotName =
                $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}-{Sanitize(versionLabel)}-{Guid.NewGuid():N}";
            if (snapshotName.Length > 80) snapshotName = snapshotName[..80];
            string snapshotRoot = Path.Combine(backupRoot, snapshotName);
            Directory.CreateDirectory(snapshotRoot);

            var touchedTargets = new List<(string Target, string? Backup)>();
            var snapshotFiles = new List<SnapshotFile>();
            bool cacheBundleInvalidated;
            try
            {
                foreach (ModuleInstallRequest module in modules)
                {
                    foreach (ModFileEntry file in module.Manifest.Files)
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
                        snapshotFiles.Add(new SnapshotFile
                        {
                            ModId = module.Manifest.ModId,
                            Target = file.Target,
                            HadOriginal = hadOriginal,
                        });
                    }
                }

                string? cacheBundlePath = BackupCoreDataBundle(
                    gameRoot,
                    snapshotRoot,
                    CoreDataBundleRelativePath);

                var metadata = new SnapshotMetadata
                {
                    SchemaVersion = 2,
                    Modules = modules.Select(module => new SnapshotModule
                    {
                        ModId = module.Manifest.ModId,
                        Version = module.Manifest.Version,
                    }).ToArray(),
                    Files = snapshotFiles,
                };
                await File.WriteAllTextAsync(
                    Path.Combine(snapshotRoot, ".snapshot.json"),
                    JsonSerializer.Serialize(metadata, SnapshotJsonOptions),
                    cancellationToken);

                foreach (StagedModule module in stagedModules)
                {
                    foreach (ModFileEntry file in module.Request.Manifest.Files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string stagedPath = CombineSafe(module.StagingRoot, file.Source);
                        string targetPath = CombineSafe(gameRoot, file.Target);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                        string temporaryTarget =
                            targetPath + ".smmm-new-" + Guid.NewGuid().ToString("N");
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

                cacheBundleInvalidated = InvalidateCoreDataBundle(cacheBundlePath);
            }
            catch
            {
                foreach ((string target, string? backup) in
                         touchedTargets.AsEnumerable().Reverse())
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
                if (Directory.Exists(snapshotRoot))
                {
                    Directory.Delete(snapshotRoot, recursive: true);
                }
                throw;
            }

            return new InstallResult(
                snapshotRoot,
                modules.Sum(module => module.Manifest.Files.Count),
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
        SnapshotMetadata metadata = await LoadSnapshotMetadataAsync(
            gameRoot,
            snapshotDirectory,
            cancellationToken);
        return await RestoreFilesAsync(
            gameRoot,
            snapshotDirectory,
            metadata.Files,
            cancellationToken);
    }

    internal async Task<bool> RestoreModuleAsync(
        string gameRoot,
        string snapshotDirectory,
        string modId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        SnapshotMetadata metadata = await LoadSnapshotMetadataAsync(
            gameRoot,
            snapshotDirectory,
            cancellationToken);
        if (!metadata.Files.Any(file => !string.IsNullOrWhiteSpace(file.ModId)))
        {
            throw new InvalidDataException(
                "Backup snapshot does not contain module metadata.");
        }

        SnapshotFile[] selectedFiles = metadata.Files
            .Where(file => string.Equals(
                file.ModId,
                modId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (selectedFiles.Length == 0)
        {
            throw new InvalidDataException(
                $"Backup snapshot does not contain module {modId}.");
        }

        return await RestoreFilesAsync(
            gameRoot,
            snapshotDirectory,
            selectedFiles,
            cancellationToken);
    }

    private static void ValidateModuleSet(IReadOnlyList<ModuleInstallRequest> modules)
    {
        if (modules is null || modules.Count == 0)
        {
            throw new InvalidDataException("At least one module must be selected.");
        }

        var modIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string>? commonBuildIds = null;
        foreach (ModuleInstallRequest? module in modules)
        {
            if (module is null)
            {
                throw new InvalidDataException("Module install request cannot be null.");
            }

            IReadOnlyList<string> manifestErrors = module.Manifest.Validate();
            if (manifestErrors.Count > 0)
            {
                throw new InvalidDataException(
                    "The release manifest is invalid: "
                    + string.Join("; ", manifestErrors));
            }
            if (!modIds.Add(module.Manifest.ModId))
            {
                throw new InvalidDataException(
                    $"Duplicate ModId: {module.Manifest.ModId}.");
            }
            if (commonBuildIds is null)
            {
                commonBuildIds = new HashSet<string>(
                    module.Manifest.SupportedBuildIds,
                    StringComparer.Ordinal);
            }
            else
            {
                commonBuildIds.IntersectWith(module.Manifest.SupportedBuildIds);
            }
            if (module.Manifest.Files.Any(file =>
                    TargetsGeneratedCacheDirectory(file.Target)))
            {
                throw new InvalidDataException(
                    "The generated Cache directory cannot be a payload target.");
            }

            foreach (ModFileEntry file in module.Manifest.Files)
            {
                string target = file.Target.Replace('\\', '/');
                if (!targets.Add(target))
                {
                    throw new InvalidDataException(
                        $"Duplicate Target path across modules: {file.Target}.");
                }
            }
        }

        if (commonBuildIds is null || commonBuildIds.Count == 0)
        {
            throw new InvalidDataException(
                "Selected modules have no common supported Steam build.");
        }
    }

    private static async Task<SnapshotMetadata> LoadSnapshotMetadataAsync(
        string gameRoot,
        string snapshotDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(gameRoot))
        {
            throw new DirectoryNotFoundException($"The game root does not exist: {gameRoot}");
        }
        if (!Directory.Exists(snapshotDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The backup snapshot does not exist: {snapshotDirectory}");
        }

        string metadataPath = Path.Combine(snapshotDirectory, ".snapshot.json");
        if (!File.Exists(metadataPath))
        {
            throw new InvalidDataException("Backup snapshot metadata is missing.");
        }

        SnapshotMetadata metadata = JsonSerializer.Deserialize<SnapshotMetadata>(
            await File.ReadAllTextAsync(metadataPath, cancellationToken),
            SnapshotJsonOptions)
            ?? throw new InvalidDataException("Backup snapshot metadata is invalid.");
        if (metadata.Files is null)
        {
            throw new InvalidDataException("Backup snapshot file metadata is missing.");
        }
        return metadata;
    }

    private static Task<bool> RestoreFilesAsync(
        string gameRoot,
        string snapshotDirectory,
        IReadOnlyList<SnapshotFile> files,
        CancellationToken cancellationToken)
    {
        var restoreFiles = new List<(SnapshotFile Metadata, string Target, string? Backup)>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotFile file in files)
        {
            if (!targets.Add(file.Target.Replace('\\', '/')))
            {
                throw new InvalidDataException(
                    $"Duplicate backup target: {file.Target}");
            }

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
                string temporaryTarget =
                    targetPath + ".smmm-restore-" + Guid.NewGuid().ToString("N");
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
            foreach ((string target, string? rollback, bool hadCurrent) in
                     rollbackFiles.AsEnumerable().Reverse())
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

        return Task.FromResult(cacheBundleInvalidated);
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

    private sealed record StagedModule(
        ModuleInstallRequest Request,
        string StagingRoot);

    private sealed class SnapshotMetadata
    {
        public int SchemaVersion { get; init; }
        public IReadOnlyList<SnapshotModule> Modules { get; init; } = [];
        public IReadOnlyList<SnapshotFile> Files { get; init; } = [];
    }

    private sealed class SnapshotModule
    {
        public string ModId { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
    }

    private sealed class SnapshotFile
    {
        public string? ModId { get; init; }
        public string Target { get; init; } = string.Empty;
        public bool HadOriginal { get; init; }
    }
}
