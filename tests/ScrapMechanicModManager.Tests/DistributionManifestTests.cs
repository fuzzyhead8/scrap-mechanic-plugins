using System.IO.Compression;
using System.Text.Json;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class DistributionManifestTests
{
    [Fact]
    public async Task Distribution_manifest_matches_the_tested_payload_zip()
    {
        string repoRoot = FindRepoRoot();
        string manifestPath = Path.Combine(repoRoot, "distribution", "manifest.json");
        string zipPath = Path.Combine(repoRoot, "robots_01.zip");
        Assert.True(File.Exists(manifestPath), $"Missing {manifestPath}");

        ModManifest manifest = JsonSerializer.Deserialize<ModManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.NotNull(manifest);
        Assert.Empty(manifest.Validate());
        string supportedBuildsPath = Path.Combine(
            repoRoot,
            "distribution",
            "supported-builds.txt");
        Assert.True(File.Exists(supportedBuildsPath), $"Missing {supportedBuildsPath}");
        string[] supportedBuilds = (await File.ReadAllLinesAsync(supportedBuildsPath))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        Assert.Equal(manifest.SupportedBuildIds, supportedBuilds);

        var hashService = new HashService();
        Assert.True(await hashService.VerifyFileAsync(zipPath, manifest.PayloadSha256));

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ModFileEntry file in manifest.Files)
        {
            ZipArchiveEntry? entry = archive.GetEntry(file.Source);
            Assert.NotNull(entry);
            await using Stream stream = entry.Open();
            Assert.Equal(
                file.Sha256,
                await hashService.ComputeSha256Async(stream),
                ignoreCase: true);
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "robots_01.zip")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
