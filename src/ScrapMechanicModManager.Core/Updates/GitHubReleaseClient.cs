using System.Net.Http.Headers;
using System.Text.Json;

namespace ScrapMechanicModManager.Core.Updates;

public sealed record ResolvedRelease(
    string TagName,
    ModManifest Manifest,
    Uri PayloadDownloadUrl);

public sealed class GitHubReleaseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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

    public async Task<ResolvedModuleRelease> GetLatestModuleReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        ReleaseIndex release = await GetReleaseIndexAsync(cancellationToken);
        if (!release.Assets.TryGetValue("modules.json", out Uri? catalogUrl))
        {
            (ModManifest manifest, Uri payloadUrl) = await ResolveManifestAsync(
                release,
                "manifest.json",
                expectedModId: null,
                cancellationToken);
            return new ResolvedModuleRelease(
                release.TagName,
                [
                    new ResolvedModule(
                        manifest.ModId,
                        "manifest.json",
                        DefaultSelected: true,
                        manifest,
                        payloadUrl),
                ],
                UsedLegacyManifest: true);
        }

        ModuleCatalog catalog = await DownloadJsonAsync<ModuleCatalog>(
            catalogUrl,
            "modules.json",
            cancellationToken);
        IReadOnlyList<string> catalogErrors = catalog.Validate();
        if (catalogErrors.Count > 0)
        {
            throw new InvalidDataException(
                "modules.json validation failed: " + string.Join("; ", catalogErrors));
        }

        var modules = new List<ResolvedModule>(catalog.Modules.Count);
        foreach (ModuleCatalogEntry entry in catalog.Modules)
        {
            (ModManifest manifest, Uri payloadUrl) = await ResolveManifestAsync(
                release,
                entry.ManifestAsset,
                entry.ModId,
                cancellationToken);
            modules.Add(new ResolvedModule(
                entry.ModId,
                entry.ManifestAsset,
                entry.DefaultSelected,
                manifest,
                payloadUrl));
        }

        ValidateUniqueTargets(modules);
        return new ResolvedModuleRelease(
            release.TagName,
            modules,
            UsedLegacyManifest: false);
    }

    public async Task<ResolvedRelease> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        ReleaseIndex release = await GetReleaseIndexAsync(cancellationToken);
        (ModManifest manifest, Uri payloadUrl) = await ResolveManifestAsync(
            release,
            "manifest.json",
            expectedModId: null,
            cancellationToken);
        return new ResolvedRelease(release.TagName, manifest, payloadUrl);
    }

    private async Task<ReleaseIndex> GetReleaseIndexAsync(
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
            "ScrapMechanicModManager",
            "1.0"));
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream releaseStream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
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

        return new ReleaseIndex(tagName, assets);
    }

    private async Task<(ModManifest Manifest, Uri PayloadUrl)> ResolveManifestAsync(
        ReleaseIndex release,
        string manifestAsset,
        string? expectedModId,
        CancellationToken cancellationToken)
    {
        if (!release.Assets.TryGetValue(manifestAsset, out Uri? manifestUrl))
        {
            throw new InvalidDataException(
                $"The latest release has no {manifestAsset} asset.");
        }

        ModManifest manifest = await DownloadJsonAsync<ModManifest>(
            manifestUrl,
            manifestAsset,
            cancellationToken);
        IReadOnlyList<string> errors = manifest.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                $"{manifestAsset} validation failed: " + string.Join("; ", errors));
        }
        if (expectedModId is not null
            && !string.Equals(
                expectedModId,
                manifest.ModId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{manifestAsset} ModId does not match catalog entry {expectedModId}.");
        }
        if (!release.Assets.TryGetValue(manifest.PayloadAsset, out Uri? payloadUrl))
        {
            throw new InvalidDataException(
                $"The latest release has no {manifest.PayloadAsset} asset.");
        }

        return (manifest, payloadUrl);
    }

    private async Task<T> DownloadJsonAsync<T>(
        Uri url,
        string assetName,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(
            stream,
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidDataException($"{assetName} is empty or invalid.");
    }

    private static void ValidateUniqueTargets(IReadOnlyList<ResolvedModule> modules)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedModule module in modules)
        {
            foreach (ModFileEntry file in module.Manifest.Files)
            {
                string target = file.Target.Replace('\\', '/');
                if (!targets.Add(target))
                {
                    throw new InvalidDataException(
                        $"Duplicate Target path across modules: {file.Target}.");
                }
            }
        }
    }

    private sealed record ReleaseIndex(
        string TagName,
        IReadOnlyDictionary<string, Uri> Assets);
}
