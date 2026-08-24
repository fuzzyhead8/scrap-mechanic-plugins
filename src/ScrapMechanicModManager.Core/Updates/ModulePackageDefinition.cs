using ScrapMechanicModManager.Core.Localization;

namespace ScrapMechanicModManager.Core.Updates;

public sealed record LocalizedModuleText(
    string Hungarian,
    string English)
{
    public string Get(AppLanguage language)
    {
        string preferred = language == AppLanguage.English ? English : Hungarian;
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;

        string fallback = language == AppLanguage.English ? Hungarian : English;
        return fallback ?? string.Empty;
    }
}

public sealed class ModulePackageDefinition
{
    private const string ValidationPayloadName = "package.smmmod";
    private const string ValidationHash =
        "0000000000000000000000000000000000000000000000000000000000000000";

    public int SchemaVersion { get; init; }
    public string ModId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public LocalizedModuleText DisplayName { get; init; } = new(string.Empty, string.Empty);
    public LocalizedModuleText Description { get; init; } = new(string.Empty, string.Empty);
    public string MinimumManagerVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> SupportedBuildIds { get; init; } = [];
    public IReadOnlyList<ModFileEntry> Files { get; init; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>(CreateInstallManifest(
            ValidationPayloadName,
            ValidationHash).Validate());
        if (string.IsNullOrWhiteSpace(DisplayName.Get(AppLanguage.Hungarian))
            && string.IsNullOrWhiteSpace(DisplayName.Get(AppLanguage.English)))
        {
            errors.Add("At least one localized DisplayName is required.");
        }
        if (string.IsNullOrWhiteSpace(MinimumManagerVersion))
        {
            errors.Add("MinimumManagerVersion is required.");
        }
        return errors;
    }

    public ModManifest CreateInstallManifest(string payloadAsset, string payloadSha256) => new()
    {
        SchemaVersion = SchemaVersion,
        ModId = ModId,
        Version = Version,
        PayloadAsset = payloadAsset,
        PayloadSha256 = payloadSha256,
        SupportedBuildIds = SupportedBuildIds,
        Files = Files,
    };
}
