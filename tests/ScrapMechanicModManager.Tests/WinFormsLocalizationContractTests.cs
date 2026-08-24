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
        Assert.Contains("List<OperationRecord> _operationHistory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("List<LocalizedMessage> _logMessages", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record ManagerSettings", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
        Assert.Contains("_settingsStore.Load()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WinForms_renders_dynamic_online_and_local_module_rows()
    {
        string source = File.ReadAllText(MainFormPath);

        Assert.DoesNotContain("CheckBox _robotLootModule", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckBox _beehiveAutomationModule", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckBox _freezerAutomationModule", source, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, ModuleRowControls> _moduleRows", source, StringComparison.Ordinal);
        Assert.Contains("OnlineModuleCatalogClient _onlineCatalogClient", source, StringComparison.Ordinal);
        Assert.Contains("LocalModulePackageSource _localModuleSource", source, StringComparison.Ordinal);
        Assert.Contains("ModulePayloadAcquirer _payloadAcquirer", source, StringComparison.Ordinal);
        Assert.Contains("RebuildModuleRows", source, StringComparison.Ordinal);
        Assert.Contains("RefreshModuleRegistryAsync", source, StringComparison.Ordinal);
        Assert.Contains("InstallCandidatesAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetSelectedModuleIds", source, StringComparison.Ordinal);
        Assert.Contains("RestoreSelectedModulesAsync", source, StringComparison.Ordinal);
        Assert.Contains("ModuleSourcePreferences", source, StringComparison.Ordinal);
        Assert.Contains("TextKey.ButtonOpenModsFolder", source, StringComparison.Ordinal);
        Assert.Contains("TextKey.ButtonRefreshModules", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLatestModuleReleaseAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseChannel.GetReleaseTag", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WinForms_uses_shared_persistent_history_and_backup_services()
    {
        string source = File.ReadAllText(MainFormPath);

        Assert.Contains("JsonLinesOperationJournal _operationJournal", source, StringComparison.Ordinal);
        Assert.Contains("BackupSnapshotCatalog _backupCatalog", source, StringComparison.Ordinal);
        Assert.Contains("OperationHistoryPath", source, StringComparison.Ordinal);
        Assert.Contains("\"logs\", \"operations.jsonl\"", source, StringComparison.Ordinal);
        Assert.Contains("ModuleBackupStatus> _moduleBackupStatuses", source, StringComparison.Ordinal);
        Assert.Contains("row.BackupStatus", source, StringComparison.Ordinal);
        Assert.Contains("LoadOperationHistory();", source, StringComparison.Ordinal);
        Assert.Contains("RenderBackupStatuses();", source, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(source, "RefreshBackupStatuses();") >= 5,
            "Backup status must refresh at startup and after check, install, restore, and game-root changes.");
        Assert.Contains("LocalizedMessage.FromPersisted(", source, StringComparison.Ordinal);
        Assert.Contains("OperationSeverity.Error", source, StringComparison.Ordinal);
        Assert.Contains("OperationId =", source, StringComparison.Ordinal);
        Assert.Contains("ModuleIds =", source, StringComparison.Ordinal);
        Assert.Contains("BackupDirectory =", source, StringComparison.Ordinal);
        Assert.Contains("TechnicalErrorType =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_logTimestamps", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotLookupMetadata", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotMetadata", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WinForms_module_statuses_use_one_width_constrained_row_per_module()
    {
        string source = File.ReadAllText(MainFormPath);

        Assert.Contains("CreateModuleDetailLabel()", source, StringComparison.Ordinal);
        Assert.Contains("AutoEllipsis = true", source, StringComparison.Ordinal);
        Assert.Contains("AutoSize = false", source, StringComparison.Ordinal);
        Assert.Contains("ColumnCount = 3", source, StringComparison.Ordinal);
        Assert.Contains(
            "_modulesPanel.SetColumnSpan(_modulesLabel, 3)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_modulesPanel.Controls.Add(status, 1, rowIndex)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_modulesPanel.Controls.Add(backup, 2, rowIndex)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateModuleStatusPanel(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WinForms_and_Linux_use_the_shared_short_backup_timestamp()
    {
        string winFormsSource = File.ReadAllText(MainFormPath);
        string linuxSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "ScrapMechanicModManager.Desktop",
            "MainWindow.axaml.cs"));

        Assert.Contains(
            "_localizer.FormatShortLocalDateTime(",
            winFormsSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "_localizer.FormatShortLocalDateTime(",
            linuxSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".ToString(\"g\", culture)", winFormsSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToString(\"g\", culture)", linuxSource, StringComparison.Ordinal);
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

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
