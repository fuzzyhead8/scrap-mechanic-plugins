using System.Text.Json;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Core.Installation;

public sealed record ModuleInstallRequest(
    string PayloadZipPath,
    ModManifest Manifest);

public sealed class ModuleInstallCoordinator(ModInstaller? installer = null)
{
    private readonly ModInstaller _installer = installer ?? new ModInstaller();

    public Task<InstallResult> InstallAsync(
        string gameRoot,
        IReadOnlyList<ModuleInstallRequest> modules,
        string backupRoot,
        CancellationToken cancellationToken = default) =>
        _installer.InstallModulesAsync(
            gameRoot,
            modules,
            backupRoot,
            cancellationToken);

    public Task<bool> RestoreModuleAsync(
        string gameRoot,
        string snapshotDirectory,
        string modId,
        CancellationToken cancellationToken = default) =>
        _installer.RestoreModuleAsync(
            gameRoot,
            snapshotDirectory,
            modId,
            cancellationToken);

    public string? FindLatestSnapshotForModule(string backupRoot, string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        if (!Directory.Exists(backupRoot)) return null;

        foreach (string snapshotDirectory in Directory
                     .GetDirectories(backupRoot)
                     .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string metadataPath = Path.Combine(snapshotDirectory, ".snapshot.json");
            if (!File.Exists(metadataPath)) continue;

            try
            {
                SnapshotLookupMetadata? metadata =
                    JsonSerializer.Deserialize<SnapshotLookupMetadata>(
                        File.ReadAllText(metadataPath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (metadata?.Files?.Any(file =>
                        file is not null
                        && string.Equals(
                            file.ModId,
                            modId,
                            StringComparison.OrdinalIgnoreCase)) == true)
                {
                    return snapshotDirectory;
                }
            }
            catch (JsonException)
            {
                // An incomplete snapshot is not eligible for module restore.
            }
            catch (IOException)
            {
                // An unreadable snapshot is not eligible for module restore.
            }
            catch (UnauthorizedAccessException)
            {
                // An unreadable snapshot is not eligible for module restore.
            }
        }

        return null;
    }

    private sealed class SnapshotLookupMetadata
    {
        public IReadOnlyList<SnapshotLookupFile?>? Files { get; init; }
    }

    private sealed class SnapshotLookupFile
    {
        public string? ModId { get; init; }
    }
}
