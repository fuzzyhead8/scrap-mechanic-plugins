using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Settings;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class DistributionManifestTests
{
    private const string RobotPayloadSha256 =
        "D429E6C0A812346F375DC863573A731F95BB0354834CD4BE552D90EC32217767";

    [Fact]
    public async Task Distribution_manifest_matches_the_tested_payload_zip()
    {
        string repoRoot = FindRepoRoot();
        string manifestPath = Path.Combine(repoRoot, "distribution", "manifest.json");
        string zipPath = Path.Combine(repoRoot, "robots_01.zip");
        Assert.True(File.Exists(manifestPath), $"Missing {manifestPath}");

        ModManifest manifest = JsonSerializer.Deserialize<ModManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.NotNull(manifest);
        Assert.Empty(manifest.Validate());
        string supportedBuildsPath = Path.Combine(
            repoRoot,
            "distribution",
            "supported-builds.txt");
        Assert.True(File.Exists(supportedBuildsPath), $"Missing {supportedBuildsPath}");
        string[] supportedBuilds = (await File.ReadAllLinesAsync(supportedBuildsPath))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        Assert.Equal(manifest.SupportedBuildIds, supportedBuilds);

        var hashService = new HashService();
        Assert.True(await hashService.VerifyFileAsync(zipPath, manifest.PayloadSha256));
        Assert.Equal(RobotPayloadSha256, manifest.PayloadSha256, ignoreCase: true);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ModFileEntry file in manifest.Files)
        {
            ZipArchiveEntry? entry = archive.GetEntry(file.Source);
            Assert.NotNull(entry);
            await using Stream stream = entry.Open();
            Assert.Equal(
                file.Sha256,
                await hashService.ComputeSha256Async(stream),
                ignoreCase: true);
        }
    }

    [Fact]
    public async Task Distribution_declares_three_independent_modules()
    {
        string repoRoot = FindRepoRoot();
        string modulesPath = Path.Combine(repoRoot, "distribution", "modules.json");
        Assert.True(File.Exists(modulesPath), $"Missing {modulesPath}");
        using JsonDocument catalog = JsonDocument.Parse(
            await File.ReadAllTextAsync(modulesPath));
        Assert.Equal(1, catalog.RootElement.GetProperty("schemaVersion").GetInt32());
        JsonElement[] modules = catalog.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(3, modules.Length);
        Assert.Equal(
            3,
            modules.Select(item => item.GetProperty("modId").GetString()).Distinct().Count());
        Assert.Equal(
            3,
            modules.Select(item => item.GetProperty("manifestAsset").GetString()).Distinct().Count());
        Assert.True(modules.Single(item =>
            item.GetProperty("modId").GetString() == "scrap-mechanic-robot-loot")
            .GetProperty("defaultSelected")
            .GetBoolean());
        Assert.All(
            modules.Where(item =>
                item.GetProperty("modId").GetString() != "scrap-mechanic-robot-loot"),
            item => Assert.False(item.GetProperty("defaultSelected").GetBoolean()));

        var expectedAutomation = new[]
        {
            new
            {
                ModId = "scrap-mechanic-beehive-automation",
                ManifestAsset = "manifest-beehive-automation.json",
                PayloadAsset = "beehive-automation.zip",
                Source = "beehive-automation/InteractableBeehive.lua",
                Target = "Survival/Scripts/game/interactables/InteractableBeehive.lua",
                Staging = Path.Combine("mods", "beehive-automation", "InteractableBeehive.lua"),
            },
            new
            {
                ModId = "scrap-mechanic-freezer-automation",
                ManifestAsset = "manifest-freezer-automation.json",
                PayloadAsset = "freezer-automation.zip",
                Source = "freezer-automation/Freezer.lua",
                Target = "Survival/Scripts/game/interactables/Freezer.lua",
                Staging = Path.Combine("mods", "freezer-automation", "Freezer.lua"),
            },
        };
        var hashService = new HashService();
        foreach (var expected in expectedAutomation)
        {
            JsonElement module = Assert.Single(
                modules,
                item => item.GetProperty("modId").GetString() == expected.ModId);
            Assert.Equal(
                expected.ManifestAsset,
                module.GetProperty("manifestAsset").GetString());

            string manifestPath = Path.Combine(
                repoRoot,
                "distribution",
                expected.ManifestAsset);
            Assert.True(File.Exists(manifestPath), $"Missing {manifestPath}");
            ModManifest manifest = JsonSerializer.Deserialize<ModManifest>(
                await File.ReadAllTextAsync(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Empty(manifest.Validate());
            Assert.Equal(expected.ModId, manifest.ModId);
            Assert.Equal(expected.PayloadAsset, manifest.PayloadAsset);
            ModFileEntry file = Assert.Single(manifest.Files);
            Assert.Equal(expected.Source, file.Source);
            Assert.Equal(expected.Target, file.Target);
            string stagingText = await File.ReadAllTextAsync(
                Path.Combine(repoRoot, expected.Staging));
            byte[] canonicalBytes = Encoding.UTF8.GetBytes(
                stagingText.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n'));
            await using var canonicalStream = new MemoryStream(canonicalBytes);
            Assert.Equal(
                file.Sha256,
                await hashService.ComputeSha256Async(canonicalStream),
                ignoreCase: true);
        }
    }

    [Fact]
    public async Task Built_in_module_ids_match_the_release_catalog()
    {
        string repoRoot = FindRepoRoot();
        string modulesPath = Path.Combine(repoRoot, "distribution", "modules.json");
        ModuleCatalog catalog = JsonSerializer.Deserialize<ModuleCatalog>(
            await File.ReadAllTextAsync(modulesPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(
            BuiltInModuleIds.All.Order(StringComparer.Ordinal),
            catalog.Modules.Select(module => module.ModId).Order(StringComparer.Ordinal));
        Assert.Equal(
            BuiltInModuleIds.DefaultSelected,
            catalog.Modules
                .Where(module => module.DefaultSelected)
                .Select(module => module.ModId));
    }

    [Fact]
    public async Task Dynamic_catalog_declares_valid_immutable_module_packages()
    {
        string repoRoot = FindRepoRoot();
        string catalogPath = Path.Combine(repoRoot, "distribution", "catalog-v1.json");
        string json = await File.ReadAllTextAsync(catalogPath);
        using JsonDocument document = JsonDocument.Parse(json);
        string releaseTag = document.RootElement.GetProperty("releaseTag").GetString()!;
        OnlineModuleCatalog catalog = JsonSerializer.Deserialize<OnlineModuleCatalog>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Equal(3, catalog.Modules.Count);
        Assert.Equal(
            BuiltInModuleIds.All.Order(StringComparer.Ordinal),
            catalog.Modules.Select(module => module.Definition.ModId)
                .Order(StringComparer.Ordinal));
        Assert.All(catalog.Modules, module =>
        {
            Assert.Empty(module.Definition.Validate());
            Assert.Matches("^[0-9A-F]{64}$", module.PackageSha256);
            Assert.StartsWith(
                $"https://github.com/fuzzyhead8/scrap-mechanic-plugins/releases/download/{releaseTag}/",
                module.PackageUrl,
                StringComparison.Ordinal);
            Assert.EndsWith(".smmmod", module.PackageUrl, StringComparison.Ordinal);
            Assert.All(module.Definition.Files, file =>
                Assert.StartsWith("payload/", file.Source, StringComparison.Ordinal));
        });
        Assert.Equal(
            3,
            catalog.Modules.Select(module => module.PackageUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    [Fact]
    public void Launcher_release_version_is_independent_from_module_versions()
    {
        string repoRoot = FindRepoRoot();
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repoRoot,
            "distribution",
            "manifest.json")));
        string moduleVersion = manifest.RootElement
            .GetProperty("version")
            .GetString()!;
        string windowsVersion = ReadProjectVersion(Path.Combine(
            repoRoot,
            "src",
            "ScrapMechanicModManager",
            "ScrapMechanicModManager.csproj"));
        string linuxVersion = ReadProjectVersion(Path.Combine(
            repoRoot,
            "src",
            "ScrapMechanicModManager.Desktop",
            "ScrapMechanicModManager.Desktop.csproj"));

        Assert.Equal("0.2.0-preview.12", windowsVersion);
        Assert.Equal(windowsVersion, linuxVersion);
        Assert.Equal("0.2.0-preview.11", moduleVersion);
        Assert.NotEqual(moduleVersion, windowsVersion);
    }

    private static string ReadProjectVersion(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);
        return project.Descendants("Version").Single().Value;
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
