using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class LocalModulePackageSourceTests : IDisposable
{
    private readonly string _temporaryRoot = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Valid_smmmod_is_loaded_as_an_unverified_local_candidate()
    {
        string packagePath = CreatePackage("example-mod", "print('safe')");
        var source = new LocalModulePackageSource(ModsPath);

        ModuleSourceSnapshot snapshot = source.Load();

        Assert.Empty(snapshot.Issues);
        ModuleCandidate candidate = Assert.Single(snapshot.Candidates);
        Assert.Equal("example-mod", candidate.ModId);
        Assert.Equal(ModuleSourceKind.Local, candidate.SourceKind);
        Assert.Equal(packagePath, candidate.LocalPackagePath);
        Assert.Null(candidate.PackageDownloadUrl);
        Assert.False(candidate.DefaultSelected);
        Assert.True(candidate.CanInstall);
        Assert.Matches("^[A-F0-9]{64}$", candidate.PackageSha256);
        Assert.Equal("Példa mod", candidate.Definition.DisplayName.Hungarian);
        Assert.Equal("Example mod", candidate.Definition.DisplayName.English);
    }

    [Fact]
    public void File_hash_mismatch_keeps_the_module_visible_but_disables_install()
    {
        CreatePackage("broken-mod", "actual", declaredPayload: "different");
        var source = new LocalModulePackageSource(ModsPath);

        ModuleSourceSnapshot snapshot = source.Load();

        ModuleCandidate candidate = Assert.Single(snapshot.Candidates);
        Assert.False(candidate.CanInstall);
        Assert.Contains(
            candidate.ValidationErrors,
            error => error.Contains("SHA-256 mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsafe_or_duplicate_entries_are_isolated_as_package_issues()
    {
        Directory.CreateDirectory(ModsPath);
        CreateRawZip(
            Path.Combine(ModsPath, "unsafe.smmmod"),
            [("../evil.lua", "bad")]);
        CreateRawZip(
            Path.Combine(ModsPath, "duplicate.smmmod"),
            [("module.json", "{}"), ("module.json", "{}")]);
        var source = new LocalModulePackageSource(ModsPath);

        ModuleSourceSnapshot snapshot = source.Load();

        Assert.Empty(snapshot.Candidates);
        Assert.Equal(2, snapshot.Issues.Count);
        Assert.Contains(
            snapshot.Issues,
            issue => issue.Errors.Any(error => error.Contains("Unsafe ZIP path", StringComparison.Ordinal)));
        Assert.Contains(
            snapshot.Issues,
            issue => issue.Errors.Any(error => error.Contains("Duplicate ZIP entry", StringComparison.Ordinal)));
    }

    [Fact]
    public void Package_limits_block_zip_bombs_before_payload_extraction()
    {
        CreatePackage("large-mod", new string('x', 64));
        var source = new LocalModulePackageSource(
            ModsPath,
            new ModulePackageLimits(
                MaxPackageBytes: 4096,
                MaxEntries: 16,
                MaxSingleEntryBytes: 16,
                MaxTotalUncompressedBytes: 32,
                MaxManifestBytes: 4096));

        ModuleSourceSnapshot snapshot = source.Load();

        Assert.Empty(snapshot.Candidates);
        ModuleSourceIssue issue = Assert.Single(snapshot.Issues);
        Assert.Contains(
            issue.Errors,
            error => error.Contains("entry size limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_mods_directory_is_created_and_returns_an_empty_snapshot()
    {
        var source = new LocalModulePackageSource(ModsPath);

        ModuleSourceSnapshot snapshot = source.Load();

        Assert.True(Directory.Exists(ModsPath));
        Assert.Empty(snapshot.Candidates);
        Assert.Empty(snapshot.Issues);
    }

    private string ModsPath => Path.Combine(_temporaryRoot, "mods");

    private string CreatePackage(
        string modId,
        string actualPayload,
        string? declaredPayload = null)
    {
        Directory.CreateDirectory(ModsPath);
        string path = Path.Combine(ModsPath, $"{modId}.smmmod");
        string declaredHash = Sha256(declaredPayload ?? actualPayload);
        string manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            modId,
            version = "1.0.0",
            displayName = new { hungarian = "Példa mod", english = "Example mod" },
            description = new { hungarian = "Leírás", english = "Description" },
            minimumManagerVersion = "0.2.0",
            supportedBuildIds = new[] { "24529696" },
            files = new[]
            {
                new
                {
                    source = "payload/example.lua",
                    target = "Survival/Scripts/example.lua",
                    sha256 = declaredHash,
                },
            },
        });
        CreateRawZip(path, [("module.json", manifest), ("payload/example.lua", actualPayload)]);
        return path;
    }

    private static void CreateRawZip(
        string path,
        IReadOnlyList<(string Name, string Content)> entries)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }

    private static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public void Dispose()
    {
        Directory.Delete(_temporaryRoot, recursive: true);
    }
}
