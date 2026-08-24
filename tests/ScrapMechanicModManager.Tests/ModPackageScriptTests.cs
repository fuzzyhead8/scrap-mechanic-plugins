using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ScrapMechanicModManager.Core.Settings;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ModPackageScriptTests : IDisposable
{
    private const string RobotPayloadSha256 =
        "D429E6C0A812346F375DC863573A731F95BB0354834CD4BE552D90EC32217767";

    private readonly string _outputOne = Path.Combine(
        Path.GetTempPath(),
        $"smmm-packages-{Guid.NewGuid():N}-one");
    private readonly string _outputTwo = Path.Combine(
        Path.GetTempPath(),
        $"smmm-packages-{Guid.NewGuid():N}-two");

    [Fact]
    public async Task Script_builds_deterministic_validated_packages_and_catalog()
    {
        string repoRoot = FindRepoRoot();
        string committedCatalogPath = Path.Combine(
            repoRoot,
            "distribution",
            "catalog-v1.json");
        Assert.True(File.Exists(committedCatalogPath), $"Missing {committedCatalogPath}");
        using JsonDocument committedCatalog = JsonDocument.Parse(
            await File.ReadAllTextAsync(committedCatalogPath));
        string releaseTag = committedCatalog.RootElement
            .GetProperty("releaseTag")
            .GetString()!;

        await RunScriptAsync(repoRoot, releaseTag, _outputOne);
        await RunScriptAsync(repoRoot, releaseTag, _outputTwo);

        string firstCatalogPath = Path.Combine(_outputOne, "catalog-v1.json");
        string secondCatalogPath = Path.Combine(_outputTwo, "catalog-v1.json");
        Assert.Equal(
            await File.ReadAllBytesAsync(firstCatalogPath),
            await File.ReadAllBytesAsync(secondCatalogPath));
        Assert.Equal(
            await File.ReadAllBytesAsync(committedCatalogPath),
            await File.ReadAllBytesAsync(firstCatalogPath));

        OnlineModuleCatalog catalog = JsonSerializer.Deserialize<OnlineModuleCatalog>(
            await File.ReadAllTextAsync(firstCatalogPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Equal(3, catalog.Modules.Count);
        Assert.Equal(3, catalog.Modules.Select(module => module.Definition.ModId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(catalog.Modules.Single(module =>
            module.Definition.ModId == BuiltInModuleIds.RobotLoot).DefaultSelected);
        Assert.All(catalog.Modules.Where(module =>
            module.Definition.ModId != BuiltInModuleIds.RobotLoot),
            module => Assert.False(module.DefaultSelected));

        foreach (OnlineModuleCatalogEntry catalogEntry in catalog.Modules)
        {
            ModulePackageDefinition definition = catalogEntry.Definition;
            Assert.Empty(definition.Validate());
            Assert.StartsWith(
                $"https://github.com/fuzzyhead8/scrap-mechanic-plugins/releases/download/{releaseTag}/",
                catalogEntry.PackageUrl,
                StringComparison.Ordinal);
            Assert.EndsWith(".smmmod", catalogEntry.PackageUrl, StringComparison.Ordinal);

            string packageName = Path.GetFileName(new Uri(catalogEntry.PackageUrl).AbsolutePath);
            string firstPackage = Path.Combine(_outputOne, packageName);
            string secondPackage = Path.Combine(_outputTwo, packageName);
            Assert.True(File.Exists(firstPackage), $"Missing {firstPackage}");
            Assert.Equal(
                await File.ReadAllBytesAsync(firstPackage),
                await File.ReadAllBytesAsync(secondPackage));
            Assert.Equal(
                catalogEntry.PackageSha256,
                await ComputeSha256Async(firstPackage),
                ignoreCase: true);

            using ZipArchive archive = ZipFile.OpenRead(firstPackage);
            Assert.NotNull(archive.GetEntry("module.json"));
            Assert.All(
                archive.Entries.Where(entry => entry.FullName != "module.json"),
                entry => Assert.StartsWith("payload/", entry.FullName,
                    StringComparison.Ordinal));
            foreach (ModFileEntry file in definition.Files)
            {
                ZipArchiveEntry? payload = archive.GetEntry(file.Source);
                Assert.NotNull(payload);
                await using Stream payloadStream = payload.Open();
                Assert.Equal(
                    file.Sha256,
                    await ComputeSha256Async(payloadStream),
                    ignoreCase: true);
            }
        }

        var localSource = new LocalModulePackageSource(_outputOne);
        ModuleSourceSnapshot snapshot = localSource.Load();
        Assert.Empty(snapshot.Issues);
        Assert.Equal(3, snapshot.Candidates.Count);
        Assert.All(snapshot.Candidates, candidate => Assert.True(candidate.CanInstall));

        string robotPayload = Path.Combine(repoRoot, "robots_01.zip");
        Assert.Equal(
            RobotPayloadSha256,
            await ComputeSha256Async(robotPayload),
            ignoreCase: true);
    }

    [Fact]
    public void Mod_catalog_workflow_is_independent_from_launcher_releases()
    {
        string repoRoot = FindRepoRoot();
        string workflow = File.ReadAllText(Path.Combine(
            repoRoot,
            ".github",
            "workflows",
            "mod-catalog-release.yml"));
        string packageScript = File.ReadAllText(Path.Combine(
            repoRoot,
            "scripts",
            "New-ModPackages.ps1"));

        Assert.StartsWith("#Requires -Version 7", packageScript, StringComparison.Ordinal);
        Assert.Contains("mods-v*", workflow, StringComparison.Ordinal);
        Assert.Contains("New-ModPackages.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("softprops/action-gh-release", workflow, StringComparison.Ordinal);
        Assert.Contains("catalog-v1.json", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ScrapMechanicModManager.exe", workflow,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("linux-x64", workflow, StringComparison.OrdinalIgnoreCase);

        string launcherWorkflow = File.ReadAllText(Path.Combine(
            repoRoot,
            ".github",
            "workflows",
            "release.yml"));
        Assert.Contains("New-ReleasePayload.ps1", launcherWorkflow, StringComparison.Ordinal);
        Assert.Contains("legacy", launcherWorkflow, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RunScriptAsync(
        string repoRoot,
        string releaseTag,
        string outputDirectory)
    {
        string script = Path.Combine(repoRoot, "scripts", "New-ModPackages.ps1");
        Assert.True(File.Exists(script), $"Missing {script}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[]
        {
            "-NoProfile",
            "-File",
            script,
            "-ReleaseTag",
            releaseTag,
            "-OutputDirectory",
            outputDirectory,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        Assert.True(process.ExitCode == 0, $"stdout: {output}\nstderr: {error}");
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return await ComputeSha256Async(stream);
    }

    private static async Task<string> ComputeSha256Async(Stream stream)
    {
        byte[] hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
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
        foreach (string path in new[] { _outputOne, _outputTwo })
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}
