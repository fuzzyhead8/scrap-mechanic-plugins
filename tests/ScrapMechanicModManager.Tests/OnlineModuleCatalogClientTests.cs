using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class OnlineModuleCatalogClientTests : IDisposable
{
    private const string CatalogUrl =
        "https://raw.githubusercontent.com/fuzzyhead8/scrap-mechanic-plugins/main/distribution/catalog-v1.json";
    private const string PackageUrl =
        "https://github.com/fuzzyhead8/scrap-mechanic-plugins/releases/download/mods-v1/example.smmmod";
    private const string ValidHash =
        "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824";

    private readonly string _temporaryRoot = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public async Task Valid_online_catalog_is_loaded_and_atomically_cached()
    {
        string json = CatalogJson();
        var handler = new QueueHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
            Headers = { ETag = new EntityTagHeaderValue("\"catalog-v1\"") },
        });
        using var httpClient = new HttpClient(handler);
        var client = new OnlineModuleCatalogClient(
            httpClient,
            new Uri(CatalogUrl),
            CachePath);

        OnlineModuleCatalogLoadResult result = await client.LoadAsync();

        Assert.False(result.UsedCache);
        Assert.Equal("\"catalog-v1\"", result.ETag);
        ModuleCandidate candidate = Assert.Single(result.Snapshot.Candidates);
        Assert.Equal("example-mod", candidate.ModId);
        Assert.Equal(ModuleSourceKind.Online, candidate.SourceKind);
        Assert.Equal(PackageUrl, candidate.PackageDownloadUrl!.ToString());
        Assert.Equal(ValidHash, candidate.PackageSha256);
        Assert.True(candidate.DefaultSelected);
        Assert.True(candidate.CanInstall);
        Assert.Equal(json, await File.ReadAllTextAsync(CachePath));
        Assert.Empty(Directory.EnumerateFiles(_temporaryRoot, "*.tmp"));
    }

    [Fact]
    public async Task Cached_etag_is_sent_and_304_uses_the_valid_cache()
    {
        Directory.CreateDirectory(_temporaryRoot);
        await File.WriteAllTextAsync(CachePath, CatalogJson());
        await File.WriteAllTextAsync(CachePath + ".etag", "\"cached-etag\"");
        var handler = new QueueHttpHandler(
            new HttpResponseMessage(HttpStatusCode.NotModified));
        using var httpClient = new HttpClient(handler);
        var client = new OnlineModuleCatalogClient(
            httpClient,
            new Uri(CatalogUrl),
            CachePath);

        OnlineModuleCatalogLoadResult result = await client.LoadAsync();

        Assert.True(result.UsedCache);
        Assert.Equal("\"cached-etag\"", handler.LastRequest!.Headers.IfNoneMatch.Single().Tag);
        Assert.Single(result.Snapshot.Candidates);
    }

    [Fact]
    public async Task Invalid_network_catalog_falls_back_to_the_last_valid_cache()
    {
        Directory.CreateDirectory(_temporaryRoot);
        await File.WriteAllTextAsync(CachePath, CatalogJson());
        var handler = new QueueHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid json", Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);
        var client = new OnlineModuleCatalogClient(
            httpClient,
            new Uri(CatalogUrl),
            CachePath);

        OnlineModuleCatalogLoadResult result = await client.LoadAsync();

        Assert.True(result.UsedCache);
        Assert.Single(result.Snapshot.Candidates);
        Assert.Equal(CatalogJson(), await File.ReadAllTextAsync(CachePath));
    }

    [Fact]
    public async Task Corrupt_cache_cannot_hide_a_network_failure()
    {
        Directory.CreateDirectory(_temporaryRoot);
        await File.WriteAllTextAsync(CachePath, "not json");
        var handler = new QueueHttpHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var httpClient = new HttpClient(handler);
        var client = new OnlineModuleCatalogClient(
            httpClient,
            new Uri(CatalogUrl),
            CachePath);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.LoadAsync());
    }

    [Fact]
    public async Task Invalid_module_is_disabled_without_hiding_valid_catalog_modules()
    {
        string json = CatalogJson(
            Entry("valid", PackageUrl, ValidHash),
            Entry("broken", "http://example.test/broken.smmmod", "BAD"));
        var handler = new QueueHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);
        var client = new OnlineModuleCatalogClient(
            httpClient,
            new Uri(CatalogUrl),
            CachePath);

        OnlineModuleCatalogLoadResult result = await client.LoadAsync();

        Assert.Equal(2, result.Snapshot.Candidates.Count);
        Assert.True(Assert.Single(
            result.Snapshot.Candidates,
            candidate => candidate.ModId == "valid").CanInstall);
        ModuleCandidate broken = Assert.Single(
            result.Snapshot.Candidates,
            candidate => candidate.ModId == "broken");
        Assert.False(broken.CanInstall);
        Assert.Contains(
            broken.ValidationErrors,
            error => error.Contains("HTTPS GitHub release", StringComparison.Ordinal));
        Assert.Contains(
            broken.ValidationErrors,
            error => error.Contains("PackageSha256", StringComparison.Ordinal));
    }

    private string CachePath => Path.Combine(_temporaryRoot, "catalog-v1.json");

    private static string CatalogJson(params object[] entries) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        modules = entries.Length == 0
            ? new[] { Entry("example-mod", PackageUrl, ValidHash, defaultSelected: true) }
            : entries,
    });

    private static object Entry(
        string modId,
        string packageUrl,
        string packageSha256,
        bool defaultSelected = false) => new
        {
            definition = new
            {
                schemaVersion = 1,
                modId,
                version = "1.0.0",
                displayName = new { hungarian = $"{modId} HU", english = $"{modId} EN" },
                description = new { hungarian = "Leírás", english = "Description" },
                minimumManagerVersion = "0.2.0",
                supportedBuildIds = new[] { "24529696" },
                files = new[]
                {
                    new
                    {
                        source = "payload/example.lua",
                        target = $"Survival/Scripts/{modId}.lua",
                        sha256 = ValidHash,
                    },
                },
            },
            packageUrl,
            packageSha256,
            defaultSelected,
        };

    public void Dispose()
    {
        Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class QueueHttpHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
