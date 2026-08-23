using System.Text.Json;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Security;

namespace ScrapMechanicModManager.Core.Updates;

public enum ModuleInstallState
{
    NotInstalled,
    UpToDate,
    UpdateAvailable,
}

public sealed class ModuleStatusEvaluator(
    HashService? hashService = null,
    ModuleInstallCoordinator? coordinator = null)
{
    private readonly HashService _hashService = hashService ?? new HashService();
    private readonly ModuleInstallCoordinator _coordinator = coordinator ?? new();

    public async Task<ModuleInstallState> EvaluateAsync(
        string gameRoot,
        string backupRoot,
        ModManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentNullException.ThrowIfNull(manifest);

        bool current = manifest.Files.Count > 0;
        foreach (ModFileEntry file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = CombineSafe(gameRoot, file.Target);
            if (!File.Exists(target)
                || !await _hashService.VerifyFileAsync(
                    target,
                    file.Sha256,
                    cancellationToken))
            {
                current = false;
            }
        }

        if (current) return ModuleInstallState.UpToDate;

        string? snapshotDirectory = _coordinator.FindLatestSnapshotForModule(
            backupRoot,
            manifest.ModId);
        if (snapshotDirectory is null) return ModuleInstallState.NotInstalled;

        return await MatchesSnapshotBackupAsync(
            gameRoot,
            snapshotDirectory,
            manifest.ModId,
            cancellationToken)
            ? ModuleInstallState.NotInstalled
            : ModuleInstallState.UpdateAvailable;
    }

    private async Task<bool> MatchesSnapshotBackupAsync(
        string gameRoot,
        string snapshotDirectory,
        string modId,
        CancellationToken cancellationToken)
    {
        try
        {
            SnapshotMetadata? metadata = JsonSerializer.Deserialize<SnapshotMetadata>(
                await File.ReadAllTextAsync(
                    Path.Combine(snapshotDirectory, ".snapshot.json"),
                    cancellationToken),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            SnapshotFile[] files = metadata?.Files?
                .Where(file =>
                    file is not null
                    && string.Equals(
                        file.ModId,
                        modId,
                        StringComparison.OrdinalIgnoreCase))
                .Select(file => file!)
                .ToArray()
                ?? [];
            if (files.Length == 0) return false;

            foreach (SnapshotFile file in files)
            {
                string target = CombineSafe(gameRoot, file.Target);
                if (!file.HadOriginal)
                {
                    if (File.Exists(target)) return false;
                    continue;
                }

                string backup = CombineSafe(snapshotDirectory, file.Target);
                if (!File.Exists(target) || !File.Exists(backup)) return false;
                await using FileStream backupStream = File.OpenRead(backup);
                string backupHash = await _hashService.ComputeSha256Async(
                    backupStream,
                    cancellationToken);
                if (!await _hashService.VerifyFileAsync(
                        target,
                        backupHash,
                        cancellationToken))
                {
                    return false;
                }
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string CombineSafe(string root, string relativePath)
    {
        if (!ModManifest.IsSafeRelativePath(relativePath))
        {
            throw new InvalidDataException($"Unsafe relative path: {relativePath}");
        }

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
                $"Path escapes the game root: {relativePath}");
        }
        return fullPath;
    }

    private sealed class SnapshotMetadata
    {
        public IReadOnlyList<SnapshotFile?>? Files { get; init; }
    }

    private sealed class SnapshotFile
    {
        public string? ModId { get; init; }
        public string Target { get; init; } = string.Empty;
        public bool HadOriginal { get; init; }
    }
}
