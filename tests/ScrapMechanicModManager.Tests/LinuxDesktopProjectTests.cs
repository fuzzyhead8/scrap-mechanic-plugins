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
