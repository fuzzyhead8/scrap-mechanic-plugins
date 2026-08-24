namespace ScrapMechanicModManager.Core.Updates;

public sealed record ModulePackageLimits(
    long MaxPackageBytes,
    int MaxEntries,
    long MaxSingleEntryBytes,
    long MaxTotalUncompressedBytes,
    long MaxManifestBytes)
{
    public static ModulePackageLimits Default { get; } = new(
        MaxPackageBytes: 256L * 1024 * 1024,
        MaxEntries: 2048,
        MaxSingleEntryBytes: 64L * 1024 * 1024,
        MaxTotalUncompressedBytes: 512L * 1024 * 1024,
        MaxManifestBytes: 1024L * 1024);
}
