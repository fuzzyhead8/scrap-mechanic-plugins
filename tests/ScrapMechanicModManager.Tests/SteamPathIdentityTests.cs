using System.Diagnostics;
using ScrapMechanicModManager.Core.Steam;

namespace ScrapMechanicModManager.Tests;

public sealed class SteamPathIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sm-mod-manager-steam-link-{Guid.NewGuid():N}");
    private readonly List<string> _directoryLinks = [];

    [Fact]
    public void Normalize_resolves_a_symlinked_parent_directory()
    {
        (string actualRoot, string linkedRoot) = CreateLinkedSteamLayout();
        string actualGameRoot = Path.Combine(
            actualRoot,
            "steamapps",
            "common",
            "Scrap Mechanic");
        string linkedGameRoot = Path.Combine(
            linkedRoot,
            "steamapps",
            "common",
            "Scrap Mechanic");

        string normalized = SteamPathIdentity.Normalize(linkedGameRoot);

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(actualGameRoot)),
            normalized,
            OperatingSystem.IsWindows());
        Assert.True(SteamPathIdentity.AreEquivalent(actualGameRoot, linkedGameRoot));
    }

    [Fact]
    public void Locator_returns_canonical_paths_for_a_symlinked_Steam_root()
    {
        (string actualRoot, string linkedRoot) = CreateLinkedSteamLayout();
        var locator = new SteamLibraryLocator();

        SteamInstallation installation = Assert.Single(locator.FindInstallations(linkedRoot));

        Assert.Equal(
            SteamPathIdentity.Normalize(actualRoot),
            installation.LibraryRoot,
            OperatingSystem.IsWindows());
        Assert.Equal(
            SteamPathIdentity.Normalize(Path.Combine(
                actualRoot,
                "steamapps",
                "common",
                "Scrap Mechanic")),
            installation.GameRoot,
            OperatingSystem.IsWindows());
    }

    private (string ActualRoot, string LinkedRoot) CreateLinkedSteamLayout()
    {
        string steamDirectory = Path.Combine(_root, ".steam");
        string actualRoot = Path.Combine(steamDirectory, "debian-installation");
        string linkedRoot = Path.Combine(steamDirectory, "steam");
        string gameRoot = Path.Combine(
            actualRoot,
            "steamapps",
            "common",
            "Scrap Mechanic");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(
            Path.Combine(actualRoot, "steamapps", "appmanifest_387990.acf"),
            """
            "AppState"
            {
                "appid" "387990"
                "name" "Scrap Mechanic"
                "StateFlags" "4"
                "installdir" "Scrap Mechanic"
                "buildid" "24529696"
            }
            """);
        CreateDirectoryLink(linkedRoot, actualRoot);
        _directoryLinks.Add(linkedRoot);
        return (actualRoot, linkedRoot);
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo(
            "cmd.exe",
            $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not create test junction.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output + error);
    }

    public void Dispose()
    {
        foreach (string link in _directoryLinks)
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
