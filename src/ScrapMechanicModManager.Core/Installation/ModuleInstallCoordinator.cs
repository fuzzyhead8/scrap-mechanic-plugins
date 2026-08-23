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
}
