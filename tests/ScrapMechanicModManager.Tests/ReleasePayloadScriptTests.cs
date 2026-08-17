using System.Diagnostics;
using System.Text.Json;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ReleasePayloadScriptTests : IDisposable
{
    private readonly string _output = Path.Combine(
        Path.GetTempPath(),
        $"sm-release-{Guid.NewGuid():N}");

    [Fact]
    public async Task Script_builds_a_hash_verified_release_directory()
    {
        string repoRoot = FindRepoRoot();
        string script = Path.Combine(repoRoot, "scripts", "New-ReleasePayload.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[]
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            script,
            "-Version",
            "0.1.0",
            "-BuildIds",
            "24529696",
            "-OutputDirectory",
            _output,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        Assert.True(process.ExitCode == 0, $"stdout: {output}\nstderr: {error}");

        string manifestPath = Path.Combine(_output, "manifest.json");
        string payloadPath = Path.Combine(_output, "robots_01.zip");
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(payloadPath));
        ModManifest manifest = JsonSerializer.Deserialize<ModManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Empty(manifest.Validate());
        Assert.True(await new HashService().VerifyFileAsync(
            payloadPath,
            manifest.PayloadSha256));
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
        if (Directory.Exists(_output)) Directory.Delete(_output, recursive: true);
    }
}
