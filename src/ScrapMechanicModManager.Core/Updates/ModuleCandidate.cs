namespace ScrapMechanicModManager.Core.Updates;

public sealed record ModuleCandidate(
    ModulePackageDefinition Definition,
    ModuleSourceKind SourceKind,
    string PackageSha256,
    Uri? PackageDownloadUrl,
    string? LocalPackagePath,
    bool DefaultSelected,
    IReadOnlyList<string> ValidationErrors)
{
    public string ModId => Definition.ModId;

    public bool CanInstall => ValidationErrors.Count == 0;

    public ModManifest CreateInstallManifest()
    {
        string packageName = (SourceKind == ModuleSourceKind.Local
            ? Path.GetFileName(LocalPackagePath)
            : Path.GetFileName(PackageDownloadUrl?.AbsolutePath)) ?? string.Empty;
        return Definition.CreateInstallManifest(packageName, PackageSha256);
    }
}
