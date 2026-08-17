namespace ScrapMechanicModManager.Tests;

public sealed class WinFormsLocalizationContractTests
{
    [Fact]
    public void WinForms_uses_the_shared_localization_and_settings_contracts()
    {
        string source = File.ReadAllText(MainFormPath);

        Assert.Contains("AppLocalizer _localizer", source, StringComparison.Ordinal);
        Assert.Contains("ManagerSettingsStore _settingsStore", source, StringComparison.Ordinal);
        Assert.Contains("ComboBox _languageSelector", source, StringComparison.Ordinal);
        Assert.Contains("List<LocalizedMessage> _logMessages", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record ManagerSettings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WinForms_language_change_reapplies_all_visible_localized_state()
    {
        string source = File.ReadAllText(MainFormPath);

        Assert.Contains("ApplyLocalizedText", source, StringComparison.Ordinal);
        Assert.Contains("RenderLocalizedState", source, StringComparison.Ordinal);
        Assert.Contains("RenderLog", source, StringComparison.Ordinal);
        Assert.Contains("OnLanguageChangedAsync", source, StringComparison.Ordinal);
        Assert.Contains("SaveCurrentSettingsAsync", source, StringComparison.Ordinal);
    }

    private static string MainFormPath => Path.Combine(
        FindRepoRoot(),
        "src",
        "ScrapMechanicModManager",
        "MainForm.cs");

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "robots_01.zip")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
