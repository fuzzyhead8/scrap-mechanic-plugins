using System.Net;
using System.Security.Cryptography;
using System.Text;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ModulePayloadAcquirerTests : IDisposable
{
    private const string Payload = "hello";
    private static readonly string PayloadHash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Payload)));
    private readonly string _temporaryRoot = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public async Task Local_candidate_is_verified_without_an_HTTP_request()
    {
        string packagePath = Path.Combine(_temporaryRoot, "local.smmmod");
        await File.WriteAllTextAsync(packagePath, Payload);
        var handler = new CountingHttpHandler(Payload);
        using var httpClient = new HttpClient(handler);
        var acquirer = new ModulePayloadAcquirer(httpClient, _temporaryRoot);

        await using ModulePayloadLease lease = await acquirer.AcquireAsync(
            Candidate(ModuleSourceKind.Local, packagePath: packagePath));

        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(packagePath, lease.PayloadPath);
        Assert.False(lease.DeleteOnDispose);
        Assert.Equal(PayloadHash, lease.Manifest.PayloadSha256);
    }

    [Fact]
    public async Task Online_payload_is_downloaded_only_when_acquired_and_temp_file_is_deleted()
    {
        var handler = new CountingHttpHandler(Payload);
        using var httpClient = new HttpClient(handler);
        var acquirer = new ModulePayloadAcquirer(httpClient, _temporaryRoot);
        ModuleCandidate candidate = Candidate(ModuleSourceKind.Online);

        Assert.Equal(0, handler.RequestCount);
        string temporaryPath;
        await using (ModulePayloadLease lease = await acquirer.AcquireAsync(candidate))
        {
            temporaryPath = lease.PayloadPath;
            Assert.Equal(1, handler.RequestCount);
            Assert.True(lease.DeleteOnDispose);
            Assert.True(File.Exists(temporaryPath));
            Assert.Equal(Payload, await File.ReadAllTextAsync(temporaryPath));
        }

        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task GitHub_release_asset_redirect_host_is_accepted()
    {
        var handler = new RedirectedResponseHttpHandler(
            Payload,
            new Uri(
                "https://release-assets.githubusercontent.com/" +
                "github-production-release-asset/1336928071/asset-id?signed=value"));
        using var httpClient = new HttpClient(handler);
        var acquirer = new ModulePayloadAcquirer(httpClient, _temporaryRoot);

        await using ModulePayloadLease lease = await acquirer.AcquireAsync(
            Candidate(ModuleSourceKind.Online));

        Assert.Equal(Payload, await File.ReadAllTextAsync(lease.PayloadPath));
    }

    [Fact]
    public async Task Untrusted_redirect_host_is_rejected()
    {
        var handler = new RedirectedResponseHttpHandler(
            Payload,
            new Uri("https://example.com/github-production-release-asset/file"));
        using var httpClient = new HttpClient(handler);
        var acquirer = new ModulePayloadAcquirer(httpClient, _temporaryRoot);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            acquirer.AcquireAsync(Candidate(ModuleSourceKind.Online)));

        Assert.Contains("GitHub release", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hash_mismatch_deletes_the_partial_download_before_failing()
    {
        var handler = new CountingHttpHandler("tampered");
        using var httpClient = new HttpClient(handler);
        var acquirer = new ModulePayloadAcquirer(httpClient, _temporaryRoot);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            acquirer.AcquireAsync(Candidate(ModuleSourceKind.Online)));

        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(_temporaryRoot, "smmm-*.smmmod"));
    }

    [Fact]
    public async Task Oversized_online_payload_is_rejected_before_install()
    {
        var handler = new CountingHttpHandler(new string('x', 64));
        using var httpClient = new HttpClient(handler);
        var acquirer = new ModulePayloadAcquirer(
            httpClient,
            _temporaryRoot,
            new ModulePackageLimits(
                MaxPackageBytes: 16,
                MaxEntries: 16,
                MaxSingleEntryBytes: 16,
                MaxTotalUncompressedBytes: 32,
                MaxManifestBytes: 4096));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            acquirer.AcquireAsync(Candidate(ModuleSourceKind.Online)));

        Assert.Contains("size limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(_temporaryRoot, "smmm-*.smmmod"));
    }

    private static ModuleCandidate Candidate(
        ModuleSourceKind source,
        string? packagePath = null)
    {
        var definition = new ModulePackageDefinition
        {
            SchemaVersion = 1,
            ModId = "example-mod",
            Version = "1.0.0",
            DisplayName = new LocalizedModuleText("Hungarian example", "Example"),
            Description = new LocalizedModuleText("Hungarian description", "Description"),
            MinimumManagerVersion = "0.2.0",
            SupportedBuildIds = ["24529696"],
            Files =
            [
                new ModFileEntry
                {
                    Source = "payload/example.lua",
                    Target = "Survival/Scripts/example.lua",
                    Sha256 = PayloadHash,
                },
            ],
        };
        return new ModuleCandidate(
            definition,
            source,
            PayloadHash,
            source == ModuleSourceKind.Online
                ? new Uri(
                    "https://github.com/fuzzyhead8/scrap-mechanic-plugins/" +
                    "releases/download/mods-v1/example.smmmod")
                : null,
            source == ModuleSourceKind.Local ? packagePath : null,
            DefaultSelected: false,
            ValidationErrors: []);
    }

    public void Dispose()
    {
        Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class RedirectedResponseHttpHandler(string content, Uri finalUri)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri),
                Content = new StringContent(content, Encoding.UTF8),
            });
        }
    }

    private sealed class CountingHttpHandler(string content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content, Encoding.UTF8),
            });
        }
    }
}
