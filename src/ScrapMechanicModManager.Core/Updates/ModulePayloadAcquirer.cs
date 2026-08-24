using System.Security.Cryptography;

namespace ScrapMechanicModManager.Core.Updates;

public sealed class ModulePayloadLease : IAsyncDisposable
{
    internal ModulePayloadLease(
        string payloadPath,
        ModManifest manifest,
        bool deleteOnDispose)
    {
        PayloadPath = payloadPath;
        Manifest = manifest;
        DeleteOnDispose = deleteOnDispose;
    }

    public string PayloadPath { get; }
    public ModManifest Manifest { get; }
    public bool DeleteOnDispose { get; }

    public ValueTask DisposeAsync()
    {
        if (DeleteOnDispose && File.Exists(PayloadPath))
        {
            File.Delete(PayloadPath);
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class ModulePayloadAcquirer
{
    private readonly HttpClient _httpClient;
    private readonly string _temporaryDirectory;
    private readonly ModulePackageLimits _limits;

    public ModulePayloadAcquirer(
        HttpClient httpClient,
        string temporaryDirectory,
        ModulePackageLimits? limits = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryDirectory);
        _temporaryDirectory = Path.GetFullPath(temporaryDirectory);
        _limits = limits ?? ModulePackageLimits.Default;
    }

    public async Task<ModulePayloadLease> AcquireAsync(
        ModuleCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.CanInstall)
        {
            throw new InvalidDataException(
                $"Module {candidate.ModId} cannot be installed: " +
                string.Join("; ", candidate.ValidationErrors));
        }

        return candidate.SourceKind switch
        {
            ModuleSourceKind.Local => await AcquireLocalAsync(candidate, cancellationToken),
            ModuleSourceKind.Online => await AcquireOnlineAsync(candidate, cancellationToken),
            _ => throw new InvalidDataException(
                $"Unsupported module source: {candidate.SourceKind}."),
        };
    }

    private async Task<ModulePayloadLease> AcquireLocalAsync(
        ModuleCandidate candidate,
        CancellationToken cancellationToken)
    {
        string path = candidate.LocalPackagePath
            ?? throw new InvalidDataException(
                $"Local module {candidate.ModId} has no package path.");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Local module package was not found.", path);
        }
        if (new FileInfo(path).Length > _limits.MaxPackageBytes)
        {
            throw new InvalidDataException("Local module package exceeds the size limit.");
        }

        await using FileStream stream = File.OpenRead(path);
        string actualHash = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
        EnsureHash(candidate, actualHash);
        return new ModulePayloadLease(
            path,
            candidate.CreateInstallManifest(),
            deleteOnDispose: false);
    }

    private async Task<ModulePayloadLease> AcquireOnlineAsync(
        ModuleCandidate candidate,
        CancellationToken cancellationToken)
    {
        Uri uri = candidate.PackageDownloadUrl
            ?? throw new InvalidDataException(
                $"Online module {candidate.ModId} has no download URL.");
        EnsureSafeDownloadUri(uri);

        Directory.CreateDirectory(_temporaryDirectory);
        string temporaryPath = Path.Combine(
            _temporaryDirectory,
            $"smmm-{Guid.NewGuid():N}.smmmod");
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            EnsureSafeResponseUri(response.RequestMessage?.RequestUri ?? uri);
            if (response.Content.Headers.ContentLength > _limits.MaxPackageBytes)
            {
                throw new InvalidDataException(
                    "Online module package exceeds the size limit.");
            }

            await using Stream input = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > _limits.MaxPackageBytes)
                {
                    throw new InvalidDataException(
                        "Online module package exceeds the size limit.");
                }
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await output.FlushAsync(cancellationToken);
            string actualHash = Convert.ToHexString(hash.GetHashAndReset());
            EnsureHash(candidate, actualHash);

            return new ModulePayloadLease(
                temporaryPath,
                candidate.CreateInstallManifest(),
                deleteOnDispose: true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    private static void EnsureHash(ModuleCandidate candidate, string actualHash)
    {
        if (!string.Equals(
                actualHash,
                candidate.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Module package SHA-256 mismatch for {candidate.ModId}.");
        }
    }

    private static void EnsureSafeDownloadUri(Uri uri)
    {
        if (!IsSafeUri(uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal)
            || !uri.AbsolutePath.EndsWith(".smmmod", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Module package URL must be an HTTPS GitHub release download URL.");
        }
    }

    private static void EnsureSafeResponseUri(Uri uri)
    {
        if (IsSafeUri(uri)
            && (string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith(
                    "/github-production-release-asset/",
                    StringComparison.Ordinal)))
        {
            return;
        }

        EnsureSafeDownloadUri(uri);
    }

    private static bool IsSafeUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);
}
