namespace ScrapMechanicModManager.Tests;

public sealed class LinuxDesktopProjectTests
{
    [Fact]
    public void Solution_contains_the_Avalonia_linux_desktop_project()
    {
        string repoRoot = FindRepoRoot();
        string projectDirectory = Path.Combine(
            repoRoot,
            "src",
            "ScrapMechanicModManager.Desktop");
        string projectPath = Path.Combine(
            projectDirectory,
            "ScrapMechanicModManager.Desktop.csproj");

        Assert.True(File.Exists(projectPath), $"Missing {projectPath}");
        string project = File.ReadAllText(projectPath);
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", project);
        Assert.Contains("<RuntimeIdentifier>linux-x64</RuntimeIdentifier>", project);
        Assert.Contains("<PackageReference Include=\"Avalonia.Desktop\" Version=\"11.3.20\"", project);
        Assert.Contains(
            "..\\ScrapMechanicModManager.Core\\ScrapMechanicModManager.Core.csproj",
            project);

        Assert.True(File.Exists(Path.Combine(projectDirectory, "App.axaml")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "MainWindow.axaml")));
        Assert.True(File.Exists(Path.Combine(
            projectDirectory,
            "Assets",
            "ScrapMechanicModManager.png")));

        string solution = File.ReadAllText(Path.Combine(
            repoRoot,
            "ScrapMechanicModManager.sln"));
        Assert.Contains("ScrapMechanicModManager.Desktop", solution);
    }

    [Fact]
    public void Avalonia_window_uses_shared_localization_and_immediate_rerendering()
    {
        string repoRoot = FindRepoRoot();
        string projectDirectory = Path.Combine(
            repoRoot,
            "src",
            "ScrapMechanicModManager.Desktop");
        string xaml = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.axaml"));
        string code = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.axaml.cs"));

        Assert.Contains("x:Name=\"LanguageComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnLanguageChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AppLocalizer _localizer", code, StringComparison.Ordinal);
        Assert.Contains("ManagerSettingsStore _settingsStore", code, StringComparison.Ordinal);
        Assert.Contains("List<OperationRecord> _operationHistory", code, StringComparison.Ordinal);
        Assert.DoesNotContain("List<LocalizedMessage> _logMessages", code, StringComparison.Ordinal);
        Assert.Contains("ApplyLocalizedText", code, StringComparison.Ordinal);
        Assert.Contains("RenderLocalizedState", code, StringComparison.Ordinal);
        Assert.Contains("RenderLog", code, StringComparison.Ordinal);
        Assert.Contains("OnLanguageChangedAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record ManagerSettings", code, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", code, StringComparison.Ordinal);
        Assert.Contains("_settingsStore.Load()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_renders_dynamic_module_rows_and_source_controls()
    {
        string projectDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "ScrapMechanicModManager.Desktop");
        string xaml = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.axaml"));
        string code = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.axaml.cs"));

        Assert.Contains("x:Name=\"ModulesPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RefreshModulesButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenModsFolderButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RobotLootModuleCheckBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BeehiveAutomationModuleCheckBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FreezerAutomationModuleCheckBox", xaml, StringComparison.Ordinal);
        Assert.Contains("OnlineModuleCatalogClient _onlineCatalogClient", code, StringComparison.Ordinal);
        Assert.Contains("LocalModulePackageSource _localModuleSource", code, StringComparison.Ordinal);
        Assert.Contains("ModulePayloadAcquirer _payloadAcquirer", code, StringComparison.Ordinal);
        Assert.Contains("ModuleRegistry _moduleRegistry", code, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, ModuleRowControls> _moduleRows", code, StringComparison.Ordinal);
        Assert.Contains("RefreshModuleRegistryAsync", code, StringComparison.Ordinal);
        Assert.Contains("RebuildModuleRows", code, StringComparison.Ordinal);
        Assert.Contains("ChangeModuleSourceAsync", code, StringComparison.Ordinal);
        Assert.Contains("InstallCandidatesAsync", code, StringComparison.Ordinal);
        Assert.Contains("ModuleSourcePreferences", code, StringComparison.Ordinal);
        Assert.Contains("FindTargetConflicts", code, StringComparison.Ordinal);
        Assert.Contains("GetCommonSupportedBuildIds", code, StringComparison.Ordinal);
        Assert.Contains("RestoreSelectedModulesAsync", code, StringComparison.Ordinal);
        Assert.Contains("TextKey.ButtonRestoreSelectedModules", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLatestModuleReleaseAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ModuleSelection.FilterAvailable", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_uses_shared_persistent_history_and_backup_services()
    {
        string projectDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "ScrapMechanicModManager.Desktop");
        string xaml = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.axaml"));
        string code = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.axaml.cs"));

        Assert.Contains("x:Name=\"ModulesPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("JsonLinesOperationJournal _operationJournal", code, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, ModuleBackupStatus> _moduleBackupStatuses", code,
            StringComparison.Ordinal);
        Assert.Contains("BackupSnapshotCatalog _backupCatalog", code, StringComparison.Ordinal);
        Assert.Contains("OperationHistoryPath", code, StringComparison.Ordinal);
        Assert.Contains("\"logs\", \"operations.jsonl\"", code, StringComparison.Ordinal);
        Assert.Contains("LoadOperationHistoryAsync", code, StringComparison.Ordinal);
        Assert.Contains("RenderBackupStatuses();", code, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(code, "await RefreshBackupStatusesAsync();") >= 5,
            "Backup status must refresh at startup and after check, install, restore, and game-root changes.");
        Assert.Contains("Task.Run(", code, StringComparison.Ordinal);
        Assert.Contains("LocalizedMessage.FromPersisted(", code, StringComparison.Ordinal);
        Assert.Contains("OperationSeverity.Error", code, StringComparison.Ordinal);
        Assert.Contains("TechnicalErrorType =", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_logTimestamps", code, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotMetadata", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_reports_precise_executable_version_failures()
    {
        string code = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "ScrapMechanicModManager.Desktop",
            "MainWindow.axaml.cs"));

        Assert.Contains("ReadProductVersionForUser", code, StringComparison.Ordinal);
        Assert.Contains("catch (FileNotFoundException)", code, StringComparison.Ordinal);
        Assert.Contains("TextKey.ErrorGameExecutableMissing", code, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidDataException)", code, StringComparison.Ordinal);
        Assert.Contains("TextKey.ErrorGameVersionUnavailable", code, StringComparison.Ordinal);
        Assert.Contains("error.GetType().Name", code, StringComparison.Ordinal);
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
