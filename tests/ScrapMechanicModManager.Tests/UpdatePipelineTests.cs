using System.Net;
using System.Text;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class UpdatePipelineTests
{
    private const string ValidHash =
        "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824";

    [Fact]
    public void Manifest_accepts_a_complete_supported_release()
    {
        ModManifest manifest = CreateValidManifest();

        Assert.Empty(manifest.Validate());
        Assert.True(manifest.SupportsBuild("24529696"));
        Assert.False(manifest.SupportsBuild("99999999"));
    }

    [Fact]
    public void Manifest_accepts_a_semantic_prerelease_version()
    {
        ModManifest manifest = CreateValidManifest("0.2.0-preview.1");

        Assert.Empty(manifest.Validate());
    }

    [Fact]
    public void Manifest_rejects_path_traversal_and_invalid_hashes()
    {
        var manifest = new ModManifest
        {
            SchemaVersion = 1,
            ModId = "robot-loot",
            Version = "1.0.0",
            PayloadAsset = "robots_01.zip",
            PayloadSha256 = "bad",
            SupportedBuildIds = ["24529696"],
            Files =
            [
                new ModFileEntry
                {
                    Source = "../evil.lua",
                    Target = "../outside.lua",
                    Sha256 = "bad",
                },
            ],
        };

        IReadOnlyList<string> errors = manifest.Validate();

        Assert.Contains(errors, error => error.Contains("PayloadSha256", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Source", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Target", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Sha256", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NUL")]
    [InlineData("folder/CON.lua")]
    [InlineData("folder/AUX.txt")]
    [InlineData("folder/COM1")]
    [InlineData("folder/lpt9.data")]
    [InlineData("folder/trailing.")]
    [InlineData("folder/trailing ")]
    [InlineData("folder/invalid?.lua")]
    public void Manifest_rejects_Windows_unsafe_path_segments(string target)
    {
        ModManifest manifest = CreateValidManifest(target: target);

        Assert.Contains(
            manifest.Validate(),
            error => error.Contains("Invalid Target path", StringComparison.Ordinal));
    }

    [Fact]
    public void Module_definition_rejects_an_invalid_minimum_manager_version()
    {
        var definition = new ModulePackageDefinition
        {
            SchemaVersion = 1,
            ModId = "example",
            Version = "1.0.0",
            DisplayName = new LocalizedModuleText("Hungarian", "English"),
            MinimumManagerVersion = "not-a-version",
            SupportedBuildIds = ["24529696"],
            Files =
            [
                new ModFileEntry
                {
                    Source = "payload/example.lua",
                    Target = "Survival/Scripts/example.lua",
                    Sha256 = ValidHash,
                },
            ],
        };

        Assert.Contains(
            definition.Validate(),
            error => error.Contains("MinimumManagerVersion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hash_service_computes_uppercase_sha256()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var service = new HashService();

        string hash = await service.ComputeSha256Async(stream);

        Assert.Equal(ValidHash, hash);
    }

    [Fact]
    public async Task GitHub_client_resolves_manifest_and_payload_assets()
    {
        const string releaseUrl =
            "https://api.github.com/repos/fuzzyhead8/scrap-mechanic-plugins/releases/latest";
        const string manifestUrl = "https://example.test/manifest.json";
        const string payloadUrl = "https://example.test/robots_01.zip";

        string releaseJson = $$"""
        {
          "tag_name": "v1.0.0",
          "assets": [
            { "name": "manifest.json", "browser_download_url": "{{manifestUrl}}" },
            { "name": "robots_01.zip", "browser_download_url": "{{payloadUrl}}" }
          ]
        }
        """;
        string manifestJson = $$"""
        {
          "schemaVersion": 1,
          "modId": "robot-loot",
          "version": "1.0.0",
          "payloadAsset": "robots_01.zip",
          "payloadSha256": "{{ValidHash}}",
          "supportedBuildIds": ["24529696"],
          "files": [
            {
              "source": "robots_01/lootsource_haybot.lua",
              "target": "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua",
              "sha256": "{{ValidHash}}"
            }
          ]
        }
        """;

        using var httpClient = new HttpClient(
            new FakeHttpHandler(new Dictionary<string, string>
            {
                [releaseUrl] = releaseJson,
                [manifestUrl] = manifestJson,
            }));
        var client = new GitHubReleaseClient(
            httpClient,
            "fuzzyhead8",
            "scrap-mechanic-plugins");

        ResolvedRelease release = await client.GetLatestReleaseAsync();

        Assert.Equal("v1.0.0", release.TagName);
        Assert.Equal("1.0.0", release.Manifest.Version);
        Assert.Equal(payloadUrl, release.PayloadDownloadUrl.ToString());
    }

    [Fact]
    public async Task GitHub_client_can_target_a_prerelease_tag()
    {
        const string releaseUrl =
            "https://api.github.com/repos/fuzzyhead8/scrap-mechanic-plugins/releases/tags/v0.2.0-preview.6";
        const string manifestUrl = "https://example.test/manifest.json";
        const string payloadUrl = "https://example.test/robots_01.zip";
        string releaseJson = $$"""
        {
          "tag_name": "v0.2.0-preview.6",
          "assets": [
            { "name": "manifest.json", "browser_download_url": "{{manifestUrl}}" },
            { "name": "robots_01.zip", "browser_download_url": "{{payloadUrl}}" }
          ]
        }
        """;
        string manifestJson = $$"""
        {
          "schemaVersion": 1,
          "modId": "scrap-mechanic-robot-loot",
          "version": "0.2.0-preview.6",
          "payloadAsset": "robots_01.zip",
          "payloadSha256": "{{ValidHash}}",
          "supportedBuildIds": ["24529696"],
          "files": [{
            "source": "robots_01/lootsource_haybot.lua",
            "target": "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua",
            "sha256": "{{ValidHash}}"
          }]
        }
        """;
        using var httpClient = new HttpClient(
            new FakeHttpHandler(new Dictionary<string, string>
            {
                [releaseUrl] = releaseJson,
                [manifestUrl] = manifestJson,
            }));
        var client = new GitHubReleaseClient(
            httpClient,
            "fuzzyhead8",
            "scrap-mechanic-plugins",
            "v0.2.0-preview.6");

        ResolvedRelease release = await client.GetLatestReleaseAsync();

        Assert.Equal("v0.2.0-preview.6", release.TagName);
    }

    private static ModManifest CreateValidManifest(
        string version = "1.0.0",
        string target =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua")
    {
        return new ModManifest
        {
            SchemaVersion = 1,
            ModId = "robot-loot",
            Version = version,
            PayloadAsset = "robots_01.zip",
            PayloadSha256 = ValidHash,
            SupportedBuildIds = ["24529696"],
            Files =
            [
                new ModFileEntry
                {
                    Source = "robots_01/lootsource_haybot.lua",
                    Target = target,
                    Sha256 = ValidHash,
                },
            ],
        };
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
