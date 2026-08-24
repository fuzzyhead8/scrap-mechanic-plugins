namespace ScrapMechanicModManager.Core.Updates;

public sealed class OnlineModuleCatalog
{
    public int SchemaVersion { get; init; }
    public IReadOnlyList<OnlineModuleCatalogEntry> Modules { get; init; } = [];
}

public sealed class OnlineModuleCatalogEntry
{
    public ModulePackageDefinition Definition { get; init; } = new();
    public string PackageUrl { get; init; } = string.Empty;
    public string PackageSha256 { get; init; } = string.Empty;
    public bool DefaultSelected { get; init; }
}

public sealed record OnlineModuleCatalogLoadResult(
    ModuleSourceSnapshot Snapshot,
    bool UsedCache,
    string? ETag);
