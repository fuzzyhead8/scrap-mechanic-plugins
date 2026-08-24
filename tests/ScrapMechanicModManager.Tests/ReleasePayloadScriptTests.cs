using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ReleasePayloadScriptTests : IDisposable
{
    private const string RobotPayloadSha256 =
        "D429E6C0A812346F375DC863573A731F95BB0354834CD4BE552D90EC32217767";

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
            "0.2.0-preview.1",
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
        Assert.Contains("Legacy release payloads created", output, StringComparison.Ordinal);

        string manifestPath = Path.Combine(_output, "manifest.json");
        string payloadPath = Path.Combine(_output, "robots_01.zip");
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(payloadPath));
        ModManifest manifest = JsonSerializer.Deserialize<ModManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Empty(manifest.Validate());
        Assert.Equal("0.2.0-preview.1", manifest.Version);
        var hashService = new HashService();
        Assert.True(await hashService.VerifyFileAsync(
            payloadPath,
            manifest.PayloadSha256));
        Assert.Equal(RobotPayloadSha256, manifest.PayloadSha256, ignoreCase: true);

        string modulesPath = Path.Combine(_output, "modules.json");
        Assert.True(File.Exists(modulesPath));
        using JsonDocument catalog = JsonDocument.Parse(
            await File.ReadAllTextAsync(modulesPath));
        Assert.Equal(1, catalog.RootElement.GetProperty("schemaVersion").GetInt32());
        JsonElement[] modules = catalog.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(3, modules.Length);

        var expectedModules = new[]
        {
            new
            {
                ModId = "scrap-mechanic-robot-loot",
                ManifestAsset = "manifest.json",
                PayloadAsset = "robots_01.zip",
                Source = "robots_01/lootsource_haybot.lua",
                DefaultSelected = true,
            },
            new
            {
                ModId = "scrap-mechanic-beehive-automation",
                ManifestAsset = "manifest-beehive-automation.json",
                PayloadAsset = "beehive-automation.zip",
                Source = "beehive-automation/InteractableBeehive.lua",
                DefaultSelected = false,
            },
            new
            {
                ModId = "scrap-mechanic-freezer-automation",
                ManifestAsset = "manifest-freezer-automation.json",
                PayloadAsset = "freezer-automation.zip",
                Source = "freezer-automation/Freezer.lua",
                DefaultSelected = false,
            },
        };

        foreach (var expected in expectedModules)
        {
            JsonElement module = Assert.Single(
                modules,
                item => item.GetProperty("modId").GetString() == expected.ModId);
            Assert.Equal(
                expected.ManifestAsset,
                module.GetProperty("manifestAsset").GetString());
            Assert.Equal(
                expected.DefaultSelected,
                module.GetProperty("defaultSelected").GetBoolean());

            string moduleManifestPath = Path.Combine(_output, expected.ManifestAsset);
            string modulePayloadPath = Path.Combine(_output, expected.PayloadAsset);
            Assert.True(File.Exists(moduleManifestPath));
            Assert.True(File.Exists(modulePayloadPath));
            ModManifest moduleManifest = JsonSerializer.Deserialize<ModManifest>(
                await File.ReadAllTextAsync(moduleManifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Empty(moduleManifest.Validate());
            Assert.Equal(expected.ModId, moduleManifest.ModId);
            Assert.Equal(expected.PayloadAsset, moduleManifest.PayloadAsset);
            Assert.Equal("0.2.0-preview.1", moduleManifest.Version);
            Assert.True(await hashService.VerifyFileAsync(
                modulePayloadPath,
                moduleManifest.PayloadSha256));

            using ZipArchive archive = ZipFile.OpenRead(modulePayloadPath);
            Assert.NotNull(archive.GetEntry(expected.Source));
            foreach (ModFileEntry file in moduleManifest.Files)
            {
                ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(
                    archive.GetEntry(file.Source));
                await using Stream stream = entry.Open();
                using var content = new MemoryStream();
                await stream.CopyToAsync(content);
                content.Position = 0;
                Assert.Equal(
                    file.Sha256,
                    await hashService.ComputeSha256Async(content),
                    ignoreCase: true);
                if (!file.Source.StartsWith("robots_01/", StringComparison.Ordinal))
                {
                    Assert.DoesNotContain('\r', Encoding.UTF8.GetString(content.ToArray()));
                }
            }
        }
    }

    [Fact]
    public void Release_workflow_tests_and_publishes_all_module_assets()
    {
        string repoRoot = FindRepoRoot();
        string workflow = File.ReadAllText(Path.Combine(
            repoRoot,
            ".github",
            "workflows",
            "release.yml"));

        Assert.Contains(
            "node --test tests/automation-mods.test.mjs",
            workflow,
            StringComparison.Ordinal);
        foreach (string asset in new[]
        {
            "modules.json",
            "manifest.json",
            "robots_01.zip",
            "manifest-beehive-automation.json",
            "beehive-automation.zip",
            "manifest-freezer-automation.json",
            "freezer-automation.zip",
        })
        {
            Assert.Contains(
                $"artifacts/release/{asset}",
                workflow,
                StringComparison.Ordinal);
        }
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
