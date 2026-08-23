using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ModuleInstallCoordinatorTests : IDisposable
{
    private const string CacheTarget = "Cache/Bundle/core_data.cbo";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sm-module-install-{Guid.NewGuid():N}");

    [Fact]
    public async Task Install_two_modules_uses_one_snapshot_and_updates_both_targets()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string firstTarget = "Survival/Scripts/game/interactables/First.lua";
        const string secondTarget = "Survival/Scripts/game/interactables/Second.lua";
        string firstPath = CreateFile(gameRoot, firstTarget, "vanilla-first");
        string secondPath = CreateFile(gameRoot, secondTarget, "vanilla-second");
        string cachePath = CreateFile(gameRoot, CacheTarget, "compiled-cache");
        ModuleInstallRequest first = CreateRequest(
            "module-first",
            "first.zip",
            "modded-first",
            firstTarget);
        ModuleInstallRequest second = CreateRequest(
            "module-second",
            "second.zip",
            "modded-second",
            secondTarget);
        var coordinator = new ModuleInstallCoordinator();

        InstallResult result = await coordinator.InstallAsync(
            gameRoot,
            [first, second],
            backupRoot);

        Assert.Equal("modded-first", File.ReadAllText(firstPath));
        Assert.Equal("modded-second", File.ReadAllText(secondPath));
        Assert.Equal(2, result.InstalledFileCount);
        Assert.True(result.CacheBundleInvalidated);
        Assert.False(File.Exists(cachePath));
        Assert.Equal(result.BackupDirectory, Assert.Single(Directory.GetDirectories(backupRoot)));
        Assert.Equal(
            "compiled-cache",
            File.ReadAllText(Path.Combine(result.BackupDirectory, CacheTarget)));

        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            result.BackupDirectory,
            ".snapshot.json")));
        JsonElement[] modules = metadata.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, modules.Length);
        Assert.Contains(modules, module => module.GetProperty("modId").GetString() == "module-first");
        Assert.Contains(modules, module => module.GetProperty("modId").GetString() == "module-second");
        Assert.Equal(
            2,
            metadata.RootElement.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task Install_rejects_target_collisions_before_writing()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string target = "Survival/Scripts/game/interactables/Shared.lua";
        string targetPath = CreateFile(gameRoot, target, "vanilla");
        ModuleInstallRequest first = CreateRequest(
            "module-first",
            "first.zip",
            "first",
            target);
        ModuleInstallRequest second = CreateRequest(
            "module-second",
            "second.zip",
            "second",
            target);
        var coordinator = new ModuleInstallCoordinator();

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => coordinator.InstallAsync(gameRoot, [first, second], backupRoot));

        Assert.Contains("Duplicate Target", error.Message, StringComparison.Ordinal);
        Assert.Equal("vanilla", File.ReadAllText(targetPath));
        Assert.False(Directory.Exists(backupRoot));
    }

    [Fact]
    public async Task Install_rejects_modules_without_a_common_supported_build()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(gameRoot);
        ModuleInstallRequest first = CreateRequest(
            "module-first",
            "first.zip",
            "first",
            "Survival/Scripts/game/interactables/First.lua",
            buildId: "24529696");
        ModuleInstallRequest second = CreateRequest(
            "module-second",
            "second.zip",
            "second",
            "Survival/Scripts/game/interactables/Second.lua",
            buildId: "99999999");
        var coordinator = new ModuleInstallCoordinator();

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => coordinator.InstallAsync(gameRoot, [first, second], backupRoot));

        Assert.Contains("common supported Steam build", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(backupRoot));
    }

    [Fact]
    public async Task Failure_while_applying_the_second_module_rolls_back_every_target()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string firstTarget = "Survival/Scripts/game/interactables/First.lua";
        const string secondTarget = "blocked/Second.lua";
        string firstPath = CreateFile(gameRoot, firstTarget, "vanilla-first");
        string blockedParent = CreateFile(gameRoot, "blocked", "not-a-directory");
        ModuleInstallRequest first = CreateRequest(
            "module-first",
            "first.zip",
            "modded-first",
            firstTarget);
        ModuleInstallRequest second = CreateRequest(
            "module-second",
            "second.zip",
            "modded-second",
            secondTarget);
        var coordinator = new ModuleInstallCoordinator();

        await Assert.ThrowsAnyAsync<IOException>(
            () => coordinator.InstallAsync(gameRoot, [first, second], backupRoot));

        Assert.Equal("vanilla-first", File.ReadAllText(firstPath));
        Assert.Equal("not-a-directory", File.ReadAllText(blockedParent));
        Assert.False(File.Exists(Path.Combine(gameRoot, secondTarget)));
        Assert.Empty(Directory.GetFiles(gameRoot, "*.smmm-new-*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetDirectories(backupRoot));
    }

    [Fact]
    public async Task Restore_selected_module_preserves_other_modules_and_deletes_new_files()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string firstTarget = "Survival/Scripts/game/interactables/First.lua";
        const string secondTarget = "Survival/Scripts/game/interactables/Second.lua";
        string firstPath = CreateFile(gameRoot, firstTarget, "vanilla-first");
        string secondPath = Path.Combine(
            gameRoot,
            secondTarget.Replace('/', Path.DirectorySeparatorChar));
        CreateFile(gameRoot, CacheTarget, "cache-before-install");
        ModuleInstallRequest first = CreateRequest(
            "module-first",
            "first.zip",
            "modded-first",
            firstTarget);
        ModuleInstallRequest second = CreateRequest(
            "module-second",
            "second.zip",
            "modded-second",
            secondTarget);
        var coordinator = new ModuleInstallCoordinator();
        InstallResult install = await coordinator.InstallAsync(
            gameRoot,
            [first, second],
            backupRoot);
        CreateFile(gameRoot, CacheTarget, "cache-before-first-restore");

        bool firstCacheInvalidated = await coordinator.RestoreModuleAsync(
            gameRoot,
            install.BackupDirectory,
            "module-first");

        Assert.True(firstCacheInvalidated);
        Assert.Equal("vanilla-first", File.ReadAllText(firstPath));
        Assert.Equal("modded-second", File.ReadAllText(secondPath));
        Assert.False(File.Exists(Path.Combine(gameRoot, CacheTarget)));
        CreateFile(gameRoot, CacheTarget, "cache-before-second-restore");

        bool secondCacheInvalidated = await coordinator.RestoreModuleAsync(
            gameRoot,
            install.BackupDirectory,
            "module-second");

        Assert.True(secondCacheInvalidated);
        Assert.Equal("vanilla-first", File.ReadAllText(firstPath));
        Assert.False(File.Exists(secondPath));
        Assert.False(File.Exists(Path.Combine(gameRoot, CacheTarget)));
    }

    [Fact]
    public void Latest_snapshot_lookup_uses_module_metadata_and_ignores_legacy_snapshots()
    {
        string backupRoot = Path.Combine(_root, "backups");
        string firstSnapshot = CreateSnapshotMetadata(
            backupRoot,
            "20260101-first",
            "module-first");
        CreateSnapshotMetadata(backupRoot, "20260102-second", "module-second");
        CreateSnapshotMetadata(backupRoot, "20260103-legacy", modId: null);
        string corruptSnapshot = Path.Combine(backupRoot, "20260104-corrupt");
        Directory.CreateDirectory(corruptSnapshot);
        File.WriteAllText(
            Path.Combine(corruptSnapshot, ".snapshot.json"),
            "{ \"files\": [null] }");
        var coordinator = new ModuleInstallCoordinator();

        string? match = coordinator.FindLatestSnapshotForModule(
            backupRoot,
            "module-first");
        string? missing = coordinator.FindLatestSnapshotForModule(
            backupRoot,
            "module-missing");

        Assert.Equal(firstSnapshot, match);
        Assert.Null(missing);
    }

    [Fact]
    public void Latest_snapshot_lookup_skips_a_newer_incomplete_snapshot()
    {
        string backupRoot = Path.Combine(_root, "backups");
        string valid = CreateSnapshotMetadata(
            backupRoot,
            "20260101-valid",
            "module-first");
        CreateSnapshotMetadata(
            backupRoot,
            "20260102-incomplete",
            "module-first",
            createBackupFile: false);
        var coordinator = new ModuleInstallCoordinator();

        string? match = coordinator.FindLatestSnapshotForModule(
            backupRoot,
            "module-first");

        Assert.Equal(valid, match);
    }

    private static string CreateSnapshotMetadata(
        string backupRoot,
        string snapshotName,
        string? modId,
        bool createBackupFile = true)
    {
        string snapshot = Path.Combine(backupRoot, snapshotName);
        Directory.CreateDirectory(snapshot);
        if (modId is null)
        {
            File.WriteAllText(
                Path.Combine(snapshot, ".snapshot.json"),
                """
                {
                  "files": [
                    {
                      "target": "Survival/Scripts/example.lua",
                      "hadOriginal": true
                    }
                  ]
                }
                """);
            return snapshot;
        }

        File.WriteAllText(
            Path.Combine(snapshot, ".snapshot.json"),
            $$"""
            {
              "schemaVersion": 2,
              "modules": [
                {
                  "modId": "{{modId}}",
                  "version": "1.0.0"
                }
              ],
              "files": [
                {
                  "modId": "{{modId}}",
                  "target": "Survival/Scripts/example.lua",
                  "hadOriginal": true
                }
              ]
            }
            """);
        if (createBackupFile)
        {
            CreateFile(snapshot, "Survival/Scripts/example.lua", "original");
        }
        return snapshot;
    }

    private ModuleInstallRequest CreateRequest(
        string modId,
        string zipName,
        string content,
        string target,
        string buildId = "24529696")
    {
        string payloadRoot = Path.Combine(_root, "payloads");
        Directory.CreateDirectory(payloadRoot);
        string zipPath = Path.Combine(payloadRoot, zipName);
        string source = $"{modId}/module.lua";
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry(source);
            using var writer = new StreamWriter(
                entry.Open(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
        }

        return new ModuleInstallRequest(
            zipPath,
            new ModManifest
            {
                SchemaVersion = 1,
                ModId = modId,
                Version = "1.0.0",
                PayloadAsset = zipName,
                PayloadSha256 = HashFile(zipPath),
                SupportedBuildIds = [buildId],
                Files =
                [
                    new ModFileEntry
                    {
                        Source = source,
                        Target = target,
                        Sha256 = HashText(content),
                    },
                ],
            });
    }

    private static string CreateFile(string root, string relativePath, string content)
    {
        string path = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string HashText(string value) => Convert.ToHexString(
        SHA256.HashData(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(value)));

    private static string HashFile(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path)));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
