using System.Net;
using System.Text;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ModuleCatalogTests
{
    private const string ValidHash =
        "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824";
    private const string ReleaseUrl =
        "https://api.github.com/repos/fuzzyhead8/scrap-mechanic-plugins/releases/latest";

    [Fact]
    public void Catalog_accepts_three_unique_safe_module_entries()
    {
        var catalog = new ModuleCatalog
        {
            SchemaVersion = 1,
            Modules =
            [
                Entry("robot", "manifest.json", defaultSelected: true),
                Entry("beehive", "manifest-beehive.json"),
                Entry("freezer", "manifest-freezer.json"),
            ],
        };

        Assert.Empty(catalog.Validate());
    }

    [Fact]
    public void Catalog_rejects_duplicate_ids_assets_and_unsafe_asset_names()
    {
        var catalog = new ModuleCatalog
        {
            SchemaVersion = 1,
            Modules =
            [
                Entry("robot", "manifest.json"),
                Entry("ROBOT", "manifest.json"),
                Entry("freezer", "../manifest-freezer.json"),
            ],
        };

        IReadOnlyList<string> errors = catalog.Validate();

        Assert.Contains(errors, error => error.Contains("Duplicate ModId", StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains("Duplicate ManifestAsset", StringComparison.Ordinal));
        Assert.Contains(
            errors,
            error => error.Contains("Invalid ManifestAsset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GitHub_client_resolves_catalog_manifests_and_payload_assets()
    {
        const string catalogUrl = "https://example.test/modules.json";
        const string robotManifestUrl = "https://example.test/manifest.json";
        const string beeManifestUrl = "https://example.test/manifest-beehive.json";
        const string freezerManifestUrl = "https://example.test/manifest-freezer.json";
        const string robotPayloadUrl = "https://example.test/robots.zip";
        const string beePayloadUrl = "https://example.test/beehive.zip";
        const string freezerPayloadUrl = "https://example.test/freezer.zip";

        using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, string>
        {
            [ReleaseUrl] = ReleaseJson(new Dictionary<string, string>
            {
                ["modules.json"] = catalogUrl,
                ["manifest.json"] = robotManifestUrl,
                ["manifest-beehive.json"] = beeManifestUrl,
                ["manifest-freezer.json"] = freezerManifestUrl,
                ["robots.zip"] = robotPayloadUrl,
                ["beehive.zip"] = beePayloadUrl,
                ["freezer.zip"] = freezerPayloadUrl,
            }),
            [catalogUrl] = CatalogJson(
                ("robot", "manifest.json", true),
                ("beehive", "manifest-beehive.json", false),
                ("freezer", "manifest-freezer.json", false)),
            [robotManifestUrl] = ManifestJson(
                "robot",
                "robots.zip",
                "Survival/Scripts/game/loot/robot.lua"),
            [beeManifestUrl] = ManifestJson(
                "beehive",
                "beehive.zip",
                "Survival/Scripts/game/interactables/InteractableBeehive.lua"),
            [freezerManifestUrl] = ManifestJson(
                "freezer",
                "freezer.zip",
                "Survival/Scripts/game/interactables/Freezer.lua"),
        }));
        var client = new GitHubReleaseClient(
            httpClient,
            "fuzzyhead8",
            "scrap-mechanic-plugins");

        ResolvedModuleRelease release = await client.GetLatestModuleReleaseAsync();

        Assert.Equal("v1.0.0", release.TagName);
        Assert.False(release.UsedLegacyManifest);
        Assert.Equal(3, release.Modules.Count);
        ResolvedModule robot = Assert.Single(release.Modules, module => module.ModId == "robot");
        Assert.True(robot.DefaultSelected);
        Assert.Equal(robotPayloadUrl, robot.PayloadDownloadUrl.ToString());
        Assert.Contains(release.Modules, module => module.ModId == "beehive");
        Assert.Contains(release.Modules, module => module.ModId == "freezer");
    }

    [Fact]
    public async Task GitHub_client_falls_back_to_legacy_manifest_without_catalog()
    {
        const string manifestUrl = "https://example.test/manifest.json";
        const string payloadUrl = "https://example.test/robots.zip";
        using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, string>
        {
            [ReleaseUrl] = ReleaseJson(new Dictionary<string, string>
            {
                ["manifest.json"] = manifestUrl,
                ["robots.zip"] = payloadUrl,
            }),
            [manifestUrl] = ManifestJson(
                "robot",
                "robots.zip",
                "Survival/Scripts/game/loot/robot.lua"),
        }));
        var client = new GitHubReleaseClient(
            httpClient,
            "fuzzyhead8",
            "scrap-mechanic-plugins");

        ResolvedModuleRelease release = await client.GetLatestModuleReleaseAsync();

        Assert.True(release.UsedLegacyManifest);
        ResolvedModule module = Assert.Single(release.Modules);
        Assert.Equal("robot", module.ModId);
        Assert.True(module.DefaultSelected);
        Assert.Equal("manifest.json", module.ManifestAsset);
        Assert.Equal(payloadUrl, module.PayloadDownloadUrl.ToString());
    }

    [Fact]
    public async Task GitHub_client_rejects_a_catalog_with_a_missing_manifest_asset()
    {
        const string catalogUrl = "https://example.test/modules.json";
        using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, string>
        {
            [ReleaseUrl] = ReleaseJson(new Dictionary<string, string>
            {
                ["modules.json"] = catalogUrl,
            }),
            [catalogUrl] = CatalogJson(("beehive", "manifest-beehive.json", false)),
        }));
        var client = new GitHubReleaseClient(
            httpClient,
            "fuzzyhead8",
            "scrap-mechanic-plugins");

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetLatestModuleReleaseAsync());

        Assert.Contains("manifest-beehive.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHub_client_rejects_a_manifest_with_a_missing_payload_asset()
    {
        const string catalogUrl = "https://example.test/modules.json";
        const string manifestUrl = "https://example.test/manifest-beehive.json";
        using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, string>
        {
            [ReleaseUrl] = ReleaseJson(new Dictionary<string, string>
            {
                ["modules.json"] = catalogUrl,
                ["manifest-beehive.json"] = manifestUrl,
            }),
            [catalogUrl] = CatalogJson(("beehive", "manifest-beehive.json", false)),
            [manifestUrl] = ManifestJson(
                "beehive",
                "beehive.zip",
                "Survival/Scripts/game/interactables/InteractableBeehive.lua"),
        }));
        var client = new GitHubReleaseClient(
            httpClient,
            "fuzzyhead8",
            "scrap-mechanic-plugins");

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetLatestModuleReleaseAsync());

        Assert.Contains("beehive.zip", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHub_client_rejects_duplicate_targets_across_modules()
    {
        const string catalogUrl = "https://example.test/modules.json";
        const string firstManifestUrl = "https://example.test/manifest-first.json";
        const string secondManifestUrl = "https://example.test/manifest-second.json";
        const string target = "Survival/Scripts/game/interactables/Shared.lua";
        using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, string>
        {
            [ReleaseUrl] = ReleaseJson(new Dictionary<string, string>
            {
                ["modules.json"] = catalogUrl,
                ["manifest-first.json"] = firstManifestUrl,
                ["manifest-second.json"] = secondManifestUrl,
                ["first.zip"] = "https://example.test/first.zip",
                ["second.zip"] = "https://example.test/second.zip",
            }),
            [catalogUrl] = CatalogJson(
                ("first", "manifest-first.json", false),
                ("second", "manifest-second.json", false)),
            [firstManifestUrl] = ManifestJson("first", "first.zip", target),
            [secondManifestUrl] = ManifestJson("second", "second.zip", target),
        }));
        var client = new GitHubReleaseClient(
            httpClient,
            "fuzzyhead8",
            "scrap-mechanic-plugins");

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetLatestModuleReleaseAsync());

        Assert.Contains("Duplicate Target", error.Message, StringComparison.Ordinal);
    }

    private static ModuleCatalogEntry Entry(
        string modId,
        string manifestAsset,
        bool defaultSelected = false) => new()
        {
            ModId = modId,
            ManifestAsset = manifestAsset,
            DefaultSelected = defaultSelected,
        };

    private static string CatalogJson(
        params (string ModId, string ManifestAsset, bool DefaultSelected)[] modules)
    {
        string entries = string.Join(
            ",",
            modules.Select(module => $$"""
            {
              "modId": "{{module.ModId}}",
              "manifestAsset": "{{module.ManifestAsset}}",
              "defaultSelected": {{module.DefaultSelected.ToString().ToLowerInvariant()}}
            }
            """));
        return $$"""
        {
          "schemaVersion": 1,
          "modules": [{{entries}}]
        }
        """;
    }

    private static string ManifestJson(string modId, string payloadAsset, string target) => $$"""
    {
      "schemaVersion": 1,
      "modId": "{{modId}}",
      "version": "1.0.0",
      "payloadAsset": "{{payloadAsset}}",
      "payloadSha256": "{{ValidHash}}",
      "supportedBuildIds": ["24529696"],
      "files": [
        {
          "source": "{{modId}}/module.lua",
          "target": "{{target}}",
          "sha256": "{{ValidHash}}"
        }
      ]
    }
    """;

    private static string ReleaseJson(IReadOnlyDictionary<string, string> assets)
    {
        string entries = string.Join(
            ",",
            assets.Select(asset => $$"""
            {
              "name": "{{asset.Key}}",
              "browser_download_url": "{{asset.Value}}"
            }
            """));
        return $$"""
        {
          "tag_name": "v1.0.0",
          "assets": [{{entries}}]
        }
        """;
    }

    private sealed class FakeHttpHandler(
        IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (!responses.TryGetValue(url, out string? content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }
}
