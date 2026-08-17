using System.Net.Http.Headers;
using System.Text.Json;

namespace ScrapMechanicModManager.Core.Updates;

public sealed record ResolvedRelease(
    string TagName,
    ModManifest Manifest,
    Uri PayloadDownloadUrl);

public sealed class GitHubReleaseClient
{
    private readonly HttpClient _httpClient;
    private readonly string _latestReleaseUrl;

    public GitHubReleaseClient(
        HttpClient httpClient,
        string owner,
        string repository)
    {
        _httpClient = httpClient;
        _latestReleaseUrl =
            $"https://api.github.com/repos/{owner}/{repository}/releases/latest";
    }

    public async Task<ResolvedRelease> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ScrapMechanicModManager", "1.0"));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream releaseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument releaseDocument = await JsonDocument.ParseAsync(
            releaseStream,
            cancellationToken: cancellationToken);
        JsonElement root = releaseDocument.RootElement;
        string tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("The GitHub release has no tag_name.");

        Dictionary<string, Uri> assets = root.GetProperty("assets")
            .EnumerateArray()
            .Select(asset => new
            {
                Name = asset.GetProperty("name").GetString(),
                Url = asset.GetProperty("browser_download_url").GetString(),
            })
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name)
                && Uri.TryCreate(asset.Url, UriKind.Absolute, out _))
            .ToDictionary(
                asset => asset.Name!,
                asset => new Uri(asset.Url!, UriKind.Absolute),
                StringComparer.OrdinalIgnoreCase);

        if (!assets.TryGetValue("manifest.json", out Uri? manifestUrl))
        {
            throw new InvalidDataException("The latest release has no manifest.json asset.");
        }

        using HttpResponseMessage manifestResponse = await _httpClient.GetAsync(
            manifestUrl,
            cancellationToken);
        manifestResponse.EnsureSuccessStatusCode();
        await using Stream manifestStream = await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
        ModManifest manifest = await JsonSerializer.DeserializeAsync<ModManifest>(
            manifestStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken)
            ?? throw new InvalidDataException("manifest.json is empty or invalid.");

        IReadOnlyList<string> errors = manifest.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                "manifest.json validation failed: " + string.Join("; ", errors));
        }

        if (!assets.TryGetValue(manifest.PayloadAsset, out Uri? payloadUrl))
        {
            throw new InvalidDataException(
                $"The latest release has no {manifest.PayloadAsset} asset.");
        }

        return new ResolvedRelease(tagName, manifest, payloadUrl);
    }
}
