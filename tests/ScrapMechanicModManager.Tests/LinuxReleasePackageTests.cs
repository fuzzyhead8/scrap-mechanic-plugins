using System.Diagnostics;

namespace ScrapMechanicModManager.Tests;

public sealed class LinuxReleasePackageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sm-mod-manager-linux-package-{Guid.NewGuid():N}");

    [Fact]
    public void Package_script_creates_a_portable_linux_archive()
    {
        string repoRoot = FindRepoRoot();
        string publishDirectory = Path.Combine(_root, "publish");
        string outputDirectory = Path.Combine(_root, "release");
        Directory.CreateDirectory(publishDirectory);
        File.WriteAllText(
            Path.Combine(publishDirectory, "ScrapMechanicModManager"),
            "fake-linux-executable");
        File.WriteAllText(
            Path.Combine(publishDirectory, "libSkiaSharp.so"),
            "fake-native-library");
        File.WriteAllText(
            Path.Combine(publishDirectory, "ScrapMechanicModManager.pdb"),
            "debug-symbols-must-not-ship");

        ProcessResult package = Run(
            OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(repoRoot, "scripts", "New-LinuxReleasePackage.ps1"),
            "-Version",
            "0.2.0",
            "-PublishDirectory",
            publishDirectory,
            "-OutputDirectory",
            outputDirectory);
        Assert.True(
            package.ExitCode == 0,
            $"Packager failed with exit code {package.ExitCode}:\n{package.Output}");

        string archive = Path.Combine(
            outputDirectory,
            "ScrapMechanicModManager-linux-x64.tar.gz");
        Assert.True(File.Exists(archive), $"Missing {archive}\n{package.Output}");
        ProcessResult listing = Run("tar", "-tzf", archive);
        Assert.Equal(0, listing.ExitCode);
        Assert.Contains(
            "ScrapMechanicModManager-linux-x64/ScrapMechanicModManager",
            listing.Output);
        Assert.Contains(
            "ScrapMechanicModManager-linux-x64/scrap-mechanic-mod-manager",
            listing.Output);
        Assert.Contains(
            "ScrapMechanicModManager-linux-x64/scrap-mechanic-mod-manager.desktop",
            listing.Output);
        Assert.Contains(
            "ScrapMechanicModManager-linux-x64/ScrapMechanicModManager.png",
            listing.Output);
        Assert.Contains(
            "ScrapMechanicModManager-linux-x64/README-Linux.txt",
            listing.Output);
        Assert.Contains(
            "ScrapMechanicModManager-linux-x64/VERSION",
            listing.Output);
        Assert.DoesNotContain(".pdb", listing.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_workflow_builds_and_publishes_the_linux_archive()
    {
        string repoRoot = FindRepoRoot();
        string workflow = File.ReadAllText(Path.Combine(
            repoRoot,
            ".github",
            "workflows",
            "release.yml"));

        Assert.Contains("ubuntu-latest", workflow);
        Assert.Contains("ScrapMechanicModManager.Desktop.csproj", workflow);
        Assert.Contains("ScrapMechanicModManager-linux-x64.tar.gz", workflow);
        Assert.Contains(
            "prerelease: ${{ contains(github.ref_name, '-') }}",
            workflow);
    }

    private static ProcessResult Run(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            standardOutput + Environment.NewLine + standardError);
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
