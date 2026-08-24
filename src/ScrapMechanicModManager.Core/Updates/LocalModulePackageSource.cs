using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScrapMechanicModManager.Core.Updates;

public sealed record ModuleSourceIssue(
    string Source,
    IReadOnlyList<string> Errors);

public sealed record ModuleSourceSnapshot(
    IReadOnlyList<ModuleCandidate> Candidates,
    IReadOnlyList<ModuleSourceIssue> Issues);

public sealed class LocalModulePackageSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _modsDirectory;
    private readonly ModulePackageLimits _limits;

    public LocalModulePackageSource(
        string modsDirectory,
        ModulePackageLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsDirectory);
        _modsDirectory = Path.GetFullPath(modsDirectory);
        _limits = limits ?? ModulePackageLimits.Default;
    }

    public ModuleSourceSnapshot Load()
    {
        Directory.CreateDirectory(_modsDirectory);
        var candidates = new List<ModuleCandidate>();
        var issues = new List<ModuleSourceIssue>();

        foreach (string packagePath in Directory.EnumerateFiles(
                     _modsDirectory,
                     "*.smmmod",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                candidates.Add(LoadPackage(packagePath));
            }
            catch (Exception error) when (error is InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or JsonException)
            {
                issues.Add(new ModuleSourceIssue(packagePath, [error.Message]));
            }
        }

        return new ModuleSourceSnapshot(candidates, issues);
    }

    private ModuleCandidate LoadPackage(string packagePath)
    {
        var packageInfo = new FileInfo(packagePath);
        if (packageInfo.Length > _limits.MaxPackageBytes)
        {
            throw new InvalidDataException(
                $"Package exceeds compressed size limit: {packageInfo.Name}.");
        }

        string packageHash;
        using (FileStream hashStream = File.OpenRead(packagePath))
        {
            packageHash = Convert.ToHexString(SHA256.HashData(hashStream));
        }

        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > _limits.MaxEntries)
        {
            throw new InvalidDataException(
                $"Package exceeds entry count limit: {archive.Entries.Count}.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(
            StringComparer.OrdinalIgnoreCase);
        long totalUncompressedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            if (!ModManifest.IsSafeRelativePath(normalized))
            {
                throw new InvalidDataException($"Unsafe ZIP path: {entry.FullName}");
            }
            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException($"Symbolic link ZIP entry is not allowed: {entry.FullName}");
            }
            if (!entries.TryAdd(normalized, entry))
            {
                throw new InvalidDataException($"Duplicate ZIP entry: {entry.FullName}");
            }
            if (entry.Length > _limits.MaxSingleEntryBytes)
            {
                throw new InvalidDataException(
                    $"ZIP entry size limit exceeded: {entry.FullName}");
            }
            totalUncompressedBytes = checked(totalUncompressedBytes + entry.Length);
            if (totalUncompressedBytes > _limits.MaxTotalUncompressedBytes)
            {
                throw new InvalidDataException("Package uncompressed size limit exceeded.");
            }
        }

        if (!entries.TryGetValue("module.json", out ZipArchiveEntry? manifestEntry)
            || string.IsNullOrEmpty(manifestEntry.Name))
        {
            throw new InvalidDataException("Package must contain one root module.json file.");
        }
        if (manifestEntry.Length > _limits.MaxManifestBytes)
        {
            throw new InvalidDataException("module.json exceeds manifest size limit.");
        }

        ModulePackageDefinition definition;
        using (Stream manifestStream = manifestEntry.Open())
        {
            definition = JsonSerializer.Deserialize<ModulePackageDefinition>(
                manifestStream,
                JsonOptions)
                ?? throw new InvalidDataException("module.json is empty or invalid.");
        }

        var validationErrors = new List<string>();
        try
        {
            validationErrors.AddRange(definition.Validate());
        }
        catch (Exception error) when (error is NullReferenceException or ArgumentException)
        {
            validationErrors.Add($"module.json is incomplete: {error.Message}");
        }

        IReadOnlyList<ModFileEntry> files = definition.Files ?? [];
        foreach (ModFileEntry file in files)
        {
            string source = (file.Source ?? string.Empty).Replace('\\', '/');
            if (!source.StartsWith("payload/", StringComparison.OrdinalIgnoreCase))
            {
                validationErrors.Add($"Payload Source must be under payload/: {file.Source}.");
                continue;
            }
            if (!entries.TryGetValue(source, out ZipArchiveEntry? payloadEntry)
                || string.IsNullOrEmpty(payloadEntry.Name))
            {
                validationErrors.Add($"Missing payload file: {file.Source}.");
                continue;
            }

            using Stream payload = payloadEntry.Open();
            string actualHash = Convert.ToHexString(SHA256.HashData(payload));
            if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                validationErrors.Add($"File SHA-256 mismatch: {file.Source}.");
            }
        }

        return new ModuleCandidate(
            definition,
            ModuleSourceKind.Local,
            packageHash,
            PackageDownloadUrl: null,
            LocalPackagePath: packagePath,
            DefaultSelected: false,
            validationErrors);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        int unixMode = entry.ExternalAttributes >> 16;
        return (unixMode & UnixFileTypeMask) == UnixSymbolicLink;
    }
}
