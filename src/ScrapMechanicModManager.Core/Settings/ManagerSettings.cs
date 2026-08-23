using ScrapMechanicModManager.Core.Localization;

namespace ScrapMechanicModManager.Core.Settings;

public static class BuiltInModuleIds
{
    public const string RobotLoot = "scrap-mechanic-robot-loot";
    public const string BeehiveAutomation = "scrap-mechanic-beehive-automation";
    public const string FreezerAutomation = "scrap-mechanic-freezer-automation";

    public static IReadOnlyList<string> DefaultSelected { get; } =
        Array.AsReadOnly([RobotLoot]);

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([RobotLoot, BeehiveAutomation, FreezerAutomation]);

    internal static string Canonicalize(string moduleId)
    {
        if (moduleId.Equals("robot-loot", StringComparison.OrdinalIgnoreCase))
        {
            return RobotLoot;
        }
        if (moduleId.Equals("beehive-automation", StringComparison.OrdinalIgnoreCase))
        {
            return BeehiveAutomation;
        }
        if (moduleId.Equals("freezer-automation", StringComparison.OrdinalIgnoreCase))
        {
            return FreezerAutomation;
        }
        return moduleId;
    }
}

public sealed class ManagerSettings : IEquatable<ManagerSettings>
{
    public ManagerSettings(
        string? gameRoot,
        AppLanguage language,
        IReadOnlyList<string>? selectedModuleIds = null)
    {
        GameRoot = gameRoot;
        Language = language;
        SelectedModuleIds = selectedModuleIds is null
            ? BuiltInModuleIds.DefaultSelected
            : NormalizeModuleIds(selectedModuleIds);
    }

    public string? GameRoot { get; }
    public AppLanguage Language { get; }
    public IReadOnlyList<string> SelectedModuleIds { get; }

    public static ManagerSettings Default { get; } =
        new(null, AppLanguage.Hungarian);

    public bool Equals(ManagerSettings? other) =>
        other is not null
        && string.Equals(GameRoot, other.GameRoot, StringComparison.Ordinal)
        && Language == other.Language
        && SelectedModuleIds.SequenceEqual(
            other.SelectedModuleIds,
            StringComparer.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ManagerSettings);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GameRoot, StringComparer.Ordinal);
        hash.Add(Language);
        foreach (string moduleId in SelectedModuleIds)
        {
            hash.Add(moduleId, StringComparer.OrdinalIgnoreCase);
        }
        return hash.ToHashCode();
    }

    private static IReadOnlyList<string> NormalizeModuleIds(
        IEnumerable<string> moduleIds) =>
        moduleIds
            .Where(moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Select(moduleId => BuiltInModuleIds.Canonicalize(moduleId.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
