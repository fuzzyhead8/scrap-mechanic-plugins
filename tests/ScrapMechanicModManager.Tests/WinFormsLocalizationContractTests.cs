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
        Assert.Contains("_settingsStore.Load()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WinForms_exposes_three_persistent_module_rows_and_selected_restore()
    {
        string source = File.ReadAllText(MainFormPath);

        Assert.Contains("CheckBox _robotLootModule", source, StringComparison.Ordinal);
        Assert.Contains("CheckBox _beehiveAutomationModule", source, StringComparison.Ordinal);
        Assert.Contains("CheckBox _freezerAutomationModule", source, StringComparison.Ordinal);
        Assert.Contains("Label _robotLootStatus", source, StringComparison.Ordinal);
        Assert.Contains("Label _beehiveAutomationStatus", source, StringComparison.Ordinal);
        Assert.Contains("Label _freezerAutomationStatus", source, StringComparison.Ordinal);
        Assert.Contains("ModuleInstallCoordinator _moduleInstaller", source, StringComparison.Ordinal);
        Assert.Contains("GetLatestModuleReleaseAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetSelectedModuleIds", source, StringComparison.Ordinal);
        Assert.Contains("ModuleSelection.FilterAvailable", source, StringComparison.Ordinal);
        Assert.Contains("RestoreSelectedModulesAsync", source, StringComparison.Ordinal);
        Assert.Contains("SelectedModuleIds", source, StringComparison.Ordinal);
        Assert.Contains("TextKey.ButtonRestoreSelectedModules", source, StringComparison.Ordinal);
        Assert.Contains("AssemblyInformationalVersionAttribute", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseChannel.GetReleaseTag", source, StringComparison.Ordinal);
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
