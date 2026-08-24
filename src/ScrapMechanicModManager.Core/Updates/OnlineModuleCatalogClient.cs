using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ScrapMechanicModManager.Core.Updates;

public sealed class OnlineModuleCatalogClient
{
    public static readonly Uri DefaultCatalogUri = new(
        "https://raw.githubusercontent.com/fuzzyhead8/" +
        "scrap-mechanic-plugins/main/distribution/catalog-v1.json");

    private const long DefaultMaximumCatalogBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _catalogUri;
    private readonly string _cachePath;
    private readonly long _maximumCatalogBytes;

    public OnlineModuleCatalogClient(
        HttpClient httpClient,
        Uri catalogUri,
        string cachePath,
        long maximumCatalogBytes = DefaultMaximumCatalogBytes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _catalogUri = catalogUri ?? throw new ArgumentNullException(nameof(catalogUri));
        if (!_catalogUri.IsAbsoluteUri || _catalogUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Catalog URL must use HTTPS.", nameof(catalogUri));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        _cachePath = Path.GetFullPath(cachePath);
        _maximumCatalogBytes = maximumCatalogBytes;
    }

    public async Task<OnlineModuleCatalogLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        CachedCatalog? cache = await TryReadCacheAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _catalogUri);
            if (cache?.ETag is not null
                && EntityTagHeaderValue.TryParse(cache.ETag, out EntityTagHeaderValue? etag))
            {
                request.Headers.IfNoneMatch.Add(etag);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return cache is not null
                    ? new OnlineModuleCatalogLoadResult(
                        cache.Snapshot,
                        UsedCache: true,
                        cache.ETag)
                    : throw new InvalidDataException(
                        "The server returned 304 but no valid catalog cache exists.");
            }

            response.EnsureSuccessStatusCode();
            byte[] bytes = await ReadLimitedContentAsync(response, cancellationToken);
            ModuleSourceSnapshot snapshot = ParseCatalog(bytes);
            string? responseETag = response.Headers.ETag?.Tag;
            await WriteCacheAsync(bytes, responseETag, cancellationToken);
            return new OnlineModuleCatalogLoadResult(
                snapshot,
                UsedCache: false,
                responseETag);
        }
        catch (Exception error) when (cache is not null
            && error is HttpRequestException
                or IOException
                or JsonException
                or InvalidDataException
                or UnauthorizedAccessException)
        {
            return new OnlineModuleCatalogLoadResult(
                cache.Snapshot,
                UsedCache: true,
                cache.ETag);
        }
    }

    private async Task<CachedCatalog?> TryReadCacheAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath)) return null;

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(_cachePath, cancellationToken);
            if (bytes.LongLength > _maximumCatalogBytes) return null;
            ModuleSourceSnapshot snapshot = ParseCatalog(bytes);
            string etagPath = GetETagPath();
            string? etag = File.Exists(etagPath)
                ? (await File.ReadAllTextAsync(etagPath, cancellationToken)).Trim()
                : null;
            if (string.IsNullOrWhiteSpace(etag)) etag = null;
            return new CachedCatalog(snapshot, etag);
        }
        catch (Exception error) when (error is IOException
            or JsonException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<byte[]> ReadLimitedContentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > _maximumCatalogBytes)
        {
            throw new InvalidDataException("Online catalog exceeds the size limit.");
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > _maximumCatalogBytes)
            {
                throw new InvalidDataException("Online catalog exceeds the size limit.");
            }
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static ModuleSourceSnapshot ParseCatalog(byte[] bytes)
    {
        OnlineModuleCatalog catalog = JsonSerializer.Deserialize<OnlineModuleCatalog>(
            bytes,
            JsonOptions)
            ?? throw new InvalidDataException("Online catalog is empty or invalid.");
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported online catalog SchemaVersion: {catalog.SchemaVersion}.");
        }
        if (catalog.Modules is null || catalog.Modules.Count == 0)
        {
            throw new InvalidDataException("Online catalog must contain at least one module.");
        }

        var candidates = new List<ModuleCandidate>();
        var issues = new List<ModuleSourceIssue>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OnlineModuleCatalogEntry? entry in catalog.Modules)
        {
            if (entry?.Definition is null)
            {
                issues.Add(new ModuleSourceIssue(
                    "online catalog",
                    ["Online module definition cannot be null."]));
                continue;
            }

            var errors = new List<string>();
            try
            {
                errors.AddRange(entry.Definition.Validate());
            }
            catch (Exception error) when (error is NullReferenceException or ArgumentException)
            {
                errors.Add($"Online module definition is incomplete: {error.Message}");
            }

            Uri? packageUri = null;
            if (!Uri.TryCreate(entry.PackageUrl, UriKind.Absolute, out Uri? parsedUri)
                || !IsImmutableGitHubReleaseUrl(parsedUri))
            {
                errors.Add("PackageUrl must be an HTTPS GitHub release download URL.");
            }
            else
            {
                packageUri = parsedUri;
            }

            string packageName = packageUri is null
                ? "package.smmmod"
                : Path.GetFileName(packageUri.AbsolutePath);
            ModManifest installManifest = entry.Definition.CreateInstallManifest(
                packageName,
                entry.PackageSha256);
            errors.AddRange(installManifest.Validate()
                .Where(error => error.Contains("PayloadSha256", StringComparison.Ordinal)
                    || error.Contains("PayloadAsset", StringComparison.Ordinal))
                .Select(error => error.Replace(
                    "PayloadSha256",
                    "PackageSha256",
                    StringComparison.Ordinal)));
            if (string.IsNullOrWhiteSpace(entry.Definition.ModId)
                || !ids.Add(entry.Definition.ModId))
            {
                errors.Add($"Duplicate or empty online ModId: {entry.Definition.ModId}.");
            }

            candidates.Add(new ModuleCandidate(
                entry.Definition,
                ModuleSourceKind.Online,
                entry.PackageSha256,
                packageUri,
                LocalPackagePath: null,
                entry.DefaultSelected,
                errors.Distinct(StringComparer.Ordinal).ToArray()));
        }

        return new ModuleSourceSnapshot(candidates, issues);
    }

    private static bool IsImmutableGitHubReleaseUrl(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal)
        && uri.AbsolutePath.EndsWith(".smmmod", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);

    private async Task WriteCacheAsync(
        byte[] bytes,
        string? etag,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_cachePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Catalog cache path has no parent directory.");
        }
        Directory.CreateDirectory(directory);
        await AtomicWriteAsync(_cachePath, bytes, cancellationToken);

        string etagPath = GetETagPath();
        if (string.IsNullOrWhiteSpace(etag))
        {
            if (File.Exists(etagPath)) File.Delete(etagPath);
        }
        else
        {
            await AtomicWriteAsync(
                etagPath,
                System.Text.Encoding.UTF8.GetBytes(etag),
                cancellationToken);
        }
    }

    private static async Task AtomicWriteAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string GetETagPath() => _cachePath + ".etag";

    private sealed record CachedCatalog(
        ModuleSourceSnapshot Snapshot,
        string? ETag);
}
