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
        Assert.Contains("List<LocalizedMessage> _logMessages", code, StringComparison.Ordinal);
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
