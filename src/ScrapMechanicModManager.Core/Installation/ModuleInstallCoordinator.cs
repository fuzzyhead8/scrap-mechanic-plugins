using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Core.Installation;

public sealed record ModuleInstallRequest(
    string PayloadZipPath,
    ModManifest Manifest);

public sealed class ModuleInstallCoordinator(
    ModInstaller? installer = null,
    BackupSnapshotCatalog? backupCatalog = null)
{
    private readonly ModInstaller _installer = installer ?? new ModInstaller();
    private readonly BackupSnapshotCatalog _backupCatalog =
        backupCatalog ?? new BackupSnapshotCatalog();

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

    public async Task<InstallResult> InstallCandidatesAsync(
        string gameRoot,
        IReadOnlyList<ModuleCandidate> candidates,
        ModulePayloadAcquirer payloadAcquirer,
        string backupRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(payloadAcquirer);
        var leases = new List<ModulePayloadLease>(candidates.Count);
        try
        {
            foreach (ModuleCandidate candidate in candidates)
            {
                leases.Add(await payloadAcquirer.AcquireAsync(
                    candidate,
                    cancellationToken));
            }

            ModuleInstallRequest[] requests = leases
                .Select(lease => new ModuleInstallRequest(
                    lease.PayloadPath,
                    lease.Manifest))
                .ToArray();
            return await InstallAsync(
                gameRoot,
                requests,
                backupRoot,
                cancellationToken);
        }
        finally
        {
            foreach (ModulePayloadLease lease in leases.AsEnumerable().Reverse())
            {
                await lease.DisposeAsync();
            }
        }
    }

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

    public string? FindLatestSnapshotForModule(string backupRoot, string modId) =>
        _backupCatalog.FindLatestValidSnapshotForModule(backupRoot, modId);
}
