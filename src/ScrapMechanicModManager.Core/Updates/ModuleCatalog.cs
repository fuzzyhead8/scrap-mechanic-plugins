namespace ScrapMechanicModManager.Core.Updates;

public sealed class ModuleCatalog
{
    public int SchemaVersion { get; init; }
    public IReadOnlyList<ModuleCatalogEntry> Modules { get; init; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != 1) errors.Add($"Unsupported SchemaVersion: {SchemaVersion}.");
        if (Modules is null || Modules.Count == 0)
        {
            errors.Add("At least one module entry is required.");
            return errors;
        }

        var modIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModuleCatalogEntry? module in Modules)
        {
            if (module is null)
            {
                errors.Add("Module entry cannot be null.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(module.ModId))
            {
                errors.Add("ModId is required.");
            }
            else if (!modIds.Add(module.ModId))
            {
                errors.Add($"Duplicate ModId: {module.ModId}.");
            }

            if (!ModManifest.IsSafeAssetName(module.ManifestAsset))
            {
                errors.Add($"Invalid ManifestAsset: {module.ManifestAsset}.");
            }
            else if (!manifestAssets.Add(module.ManifestAsset))
            {
                errors.Add($"Duplicate ManifestAsset: {module.ManifestAsset}.");
            }
        }

        return errors;
    }
}

public sealed class ModuleCatalogEntry
{
    public string ModId { get; init; } = string.Empty;
    public string ManifestAsset { get; init; } = string.Empty;
    public bool DefaultSelected { get; init; }
}

public sealed record ResolvedModule(
    string ModId,
    string ManifestAsset,
    bool DefaultSelected,
    ModManifest Manifest,
    Uri PayloadDownloadUrl);

public sealed record ResolvedModuleRelease(
    string TagName,
    IReadOnlyList<ResolvedModule> Modules,
    bool UsedLegacyManifest);
