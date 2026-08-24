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

    public ModuleCandidate ForManagerVersion(string currentManagerVersion)
    {
        var errors = new List<string>(ValidationErrors);
        if (!ModManifest.IsSemanticVersion(currentManagerVersion))
        {
            errors.Add($"Invalid current manager version: {currentManagerVersion}.");
        }
        else if (ModManifest.IsSemanticVersion(Definition.MinimumManagerVersion)
            && SemanticVersionComparer.Compare(
                currentManagerVersion,
                Definition.MinimumManagerVersion) < 0)
        {
            errors.Add(
                $"Requires manager version {Definition.MinimumManagerVersion} or newer.");
        }

        return this with
        {
            ValidationErrors = errors.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    public ModManifest CreateInstallManifest()
    {
        string packageName = (SourceKind == ModuleSourceKind.Local
            ? Path.GetFileName(LocalPackagePath)
            : Path.GetFileName(PackageDownloadUrl?.AbsolutePath)) ?? string.Empty;
        return Definition.CreateInstallManifest(packageName, PackageSha256);
    }
}
