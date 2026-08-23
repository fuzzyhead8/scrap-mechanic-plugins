using System.Globalization;
using System.Text.Json;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Core.Installation;

public sealed class BackupSnapshotCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyList<BackupSnapshotInfo> Scan(string backupRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        string fullBackupRoot = Path.GetFullPath(backupRoot);
        if (!Directory.Exists(fullBackupRoot)) return [];

        try
        {
            return Directory.GetDirectories(fullBackupRoot)
                .Select(ReadSnapshot)
                .OrderByDescending(snapshot => snapshot.CreatedAtUtc)
                .ThenByDescending(
                    snapshot => Path.GetFileName(snapshot.SnapshotDirectory),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public ModuleBackupStatus GetModuleStatus(string backupRoot, string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        IReadOnlyList<BackupSnapshotInfo> snapshots = Scan(backupRoot);
        BackupSnapshotInfo? available = snapshots.FirstOrDefault(snapshot =>
            snapshot.State == BackupSnapshotState.Available
            && snapshot.ModuleIds.Contains(modId, StringComparer.OrdinalIgnoreCase));
        if (available is not null)
        {
            available.ModuleVersions.TryGetValue(modId, out string? version);
            return new ModuleBackupStatus(
                modId,
                BackupSnapshotState.Available,
                available.SnapshotDirectory,
                available.CreatedAtUtc,
                version);
        }

        bool hasRelevantCorruption = snapshots.Any(snapshot =>
            snapshot.State == BackupSnapshotState.Corrupt
            && (snapshot.ModuleIds.Count == 0
                || snapshot.ModuleIds.Contains(
                    modId,
                    StringComparer.OrdinalIgnoreCase)));
        if (hasRelevantCorruption)
        {
            return new ModuleBackupStatus(
                modId,
                BackupSnapshotState.Corrupt,
                null,
                null,
                null);
        }

        if (snapshots.Any(snapshot => snapshot.State == BackupSnapshotState.Legacy))
        {
            return new ModuleBackupStatus(
                modId,
                BackupSnapshotState.Legacy,
                null,
                null,
                null);
        }

        return new ModuleBackupStatus(
            modId,
            BackupSnapshotState.None,
            null,
            null,
            null);
    }

    public string? FindLatestValidSnapshotForModule(
        string backupRoot,
        string modId)
    {
        ModuleBackupStatus status = GetModuleStatus(backupRoot, modId);
        return status.State == BackupSnapshotState.Available
            ? status.SnapshotDirectory
            : null;
    }

    private static BackupSnapshotInfo ReadSnapshot(string snapshotDirectory)
    {
        DateTimeOffset createdAtUtc = ReadCreatedAtUtc(snapshotDirectory);
        string metadataPath = Path.Combine(snapshotDirectory, ".snapshot.json");
        if (!File.Exists(metadataPath))
        {
            return Corrupt(
                snapshotDirectory,
                createdAtUtc,
                [],
                "Backup snapshot metadata is missing.");
        }

        try
        {
            SnapshotMetadata? metadata = JsonSerializer.Deserialize<SnapshotMetadata>(
                File.ReadAllText(metadataPath),
                JsonOptions);
            if (metadata is null)
            {
                return Corrupt(
                    snapshotDirectory,
                    createdAtUtc,
                    [],
                    "Backup snapshot metadata is invalid.");
            }

            string[] discoveredModuleIds = DiscoverModuleIds(metadata);
            if (metadata.SchemaVersion != 2)
            {
                return new BackupSnapshotInfo(
                    snapshotDirectory,
                    BackupSnapshotState.Legacy,
                    createdAtUtc,
                    discoveredModuleIds,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    null);
            }

            string? validationError = ValidateSnapshot(
                snapshotDirectory,
                metadata,
                out IReadOnlyDictionary<string, string> moduleVersions);
            return validationError is null
                ? new BackupSnapshotInfo(
                    snapshotDirectory,
                    BackupSnapshotState.Available,
                    createdAtUtc,
                    discoveredModuleIds,
                    moduleVersions,
                    null)
                : Corrupt(
                    snapshotDirectory,
                    createdAtUtc,
                    discoveredModuleIds,
                    validationError);
        }
        catch (Exception exception) when (exception is JsonException
                                               or IOException
                                               or UnauthorizedAccessException)
        {
            return Corrupt(
                snapshotDirectory,
                createdAtUtc,
                [],
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string? ValidateSnapshot(
        string snapshotDirectory,
        SnapshotMetadata metadata,
        out IReadOnlyDictionary<string, string> moduleVersions)
    {
        moduleVersions = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (metadata.Modules is null || metadata.Modules.Count == 0)
        {
            return "Backup snapshot module metadata is missing.";
        }
        if (metadata.Files is null || metadata.Files.Count == 0)
        {
            return "Backup snapshot file metadata is missing.";
        }

        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotModule? module in metadata.Modules)
        {
            if (module is null
                || string.IsNullOrWhiteSpace(module.ModId)
                || string.IsNullOrWhiteSpace(module.Version))
            {
                return "Backup snapshot contains invalid module metadata.";
            }
            if (!versions.TryAdd(module.ModId, module.Version))
            {
                return $"Duplicate module ID in backup snapshot: {module.ModId}.";
            }
        }

        var modulesWithFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotFile? file in metadata.Files)
        {
            if (file is null
                || string.IsNullOrWhiteSpace(file.ModId)
                || !versions.ContainsKey(file.ModId))
            {
                return "Backup snapshot contains invalid file module metadata.";
            }
            if (!ModManifest.IsSafeRelativePath(file.Target))
            {
                return $"Unsafe backup target path: {file.Target}.";
            }
            modulesWithFiles.Add(file.ModId);
            if (file.HadOriginal)
            {
                string backupPath = CombineSafe(snapshotDirectory, file.Target);
                if (!File.Exists(backupPath))
                {
                    return $"Required backup file is missing: {file.Target}.";
                }
            }
        }

        if (versions.Keys.Any(modId => !modulesWithFiles.Contains(modId)))
        {
            return "Backup snapshot module has no file metadata.";
        }

        moduleVersions = versions;
        return null;
    }

    private static string[] DiscoverModuleIds(SnapshotMetadata metadata) =>
        (metadata.Modules ?? [])
            .Where(module => module is not null && !string.IsNullOrWhiteSpace(module.ModId))
            .Select(module => module!.ModId)
            .Concat((metadata.Files ?? [])
                .Where(file => file is not null && !string.IsNullOrWhiteSpace(file.ModId))
                .Select(file => file!.ModId!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static BackupSnapshotInfo Corrupt(
        string snapshotDirectory,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<string> moduleIds,
        string error) => new(
            snapshotDirectory,
            BackupSnapshotState.Corrupt,
            createdAtUtc,
            moduleIds,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            error);

    private static DateTimeOffset ReadCreatedAtUtc(string snapshotDirectory)
    {
        string name = Path.GetFileName(snapshotDirectory);
        if (name.Length >= 19
            && DateTimeOffset.TryParseExact(
                name[..19],
                "yyyyMMdd_HHmmss_fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            return parsed;
        }
        return new DateTimeOffset(
            Directory.GetLastWriteTimeUtc(snapshotDirectory),
            TimeSpan.Zero);
    }

    private static string CombineSafe(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(fullRoot, comparison))
        {
            throw new InvalidDataException(
                $"Path escapes the backup snapshot: {relativePath}");
        }
        return fullPath;
    }

    private sealed class SnapshotMetadata
    {
        public int SchemaVersion { get; init; }
        public IReadOnlyList<SnapshotModule?>? Modules { get; init; }
        public IReadOnlyList<SnapshotFile?>? Files { get; init; }
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
