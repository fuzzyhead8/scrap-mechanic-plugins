using ScrapMechanicModManager.Core.Localization;

namespace ScrapMechanicModManager.Core.Settings;

public sealed record ManagerSettings(string? GameRoot, AppLanguage Language)
{
    public static ManagerSettings Default { get; } = new(null, AppLanguage.Hungarian);
}
