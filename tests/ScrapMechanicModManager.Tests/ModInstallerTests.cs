using System.IO.Compression;
using System.Security.Cryptography;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ModInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sm-mod-manager-install-{Guid.NewGuid():N}");

    [Fact]
    public async Task Install_backs_up_the_current_file_before_replacing_it()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string targetPath = CreateFile(gameRoot, targetRelative, "vanilla");
        (string zipPath, ModManifest manifest) = CreatePayload("modded", targetRelative);
        var installer = new ModInstaller();

        InstallResult result = await installer.InstallAsync(
            gameRoot,
            zipPath,
            manifest,
            backupRoot);

        Assert.Equal("modded", File.ReadAllText(targetPath));
        Assert.Equal(1, result.InstalledFileCount);
        Assert.False(result.CacheBundleInvalidated);
        string backupPath = Path.Combine(
            result.BackupDirectory,
            targetRelative.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal("vanilla", File.ReadAllText(backupPath));
    }

    [Fact]
    public async Task Restore_reinstates_the_pre_install_file()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string targetPath = CreateFile(gameRoot, targetRelative, "vanilla");
        (string zipPath, ModManifest manifest) = CreatePayload("modded", targetRelative);
        var installer = new ModInstaller();
        InstallResult install = await installer.InstallAsync(
            gameRoot,
            zipPath,
            manifest,
            backupRoot);

        bool cacheBundleInvalidated = await installer.RestoreAsync(
            gameRoot,
            install.BackupDirectory);

        Assert.Equal("vanilla", File.ReadAllText(targetPath));
        Assert.False(cacheBundleInvalidated);
    }

    [Fact]
    public async Task Install_backs_up_and_invalidates_the_core_data_bundle()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string cachePath = CreateFile(
            gameRoot,
            "Cache/Bundle/core_data.cbo",
            "stale-cache");
        (string zipPath, ModManifest manifest) = CreatePayload("modded", targetRelative);
        var installer = new ModInstaller();

        InstallResult result = await installer.InstallAsync(
            gameRoot,
            zipPath,
            manifest,
            backupRoot);

        Assert.True(result.CacheBundleInvalidated);
        Assert.False(File.Exists(cachePath));
        string cacheBackupPath = Path.Combine(
            result.BackupDirectory,
            "Cache",
            "Bundle",
            "core_data.cbo");
        Assert.Equal("stale-cache", File.ReadAllText(cacheBackupPath));
    }

    [Theory]
    [InlineData("Cache/Bundle/core_data.cbo")]
    [InlineData("Cache//Bundle/core_data.cbo")]
    [InlineData("Cache./Bundle/core_data.cbo")]
    [InlineData("CACHE~1/Bundle/core_data.cbo")]
    [InlineData("Cache/Anything.bin")]
    public async Task Install_rejects_manifest_targets_inside_the_generated_cache_directory(
        string reservedTarget)
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        string cachePath = CreateFile(
            gameRoot,
            "Cache/Bundle/core_data.cbo",
            "stale-cache");
        (string zipPath, ModManifest manifest) = CreatePayload(
            "payload-cache",
            reservedTarget);
        var installer = new ModInstaller();

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(gameRoot, zipPath, manifest, backupRoot));

        Assert.Contains("Cache", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("stale-cache", File.ReadAllText(cachePath));
    }

    [Fact]
    public async Task Restore_backs_up_and_invalidates_the_rebuilt_core_data_bundle()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        CreateFile(gameRoot, targetRelative, "vanilla");
        (string zipPath, ModManifest manifest) = CreatePayload("modded", targetRelative);
        var installer = new ModInstaller();
        InstallResult install = await installer.InstallAsync(
            gameRoot,
            zipPath,
            manifest,
            backupRoot);
        string cachePath = CreateFile(
            gameRoot,
            "Cache/Bundle/core_data.cbo",
            "rebuilt-mod-cache");

        await installer.RestoreAsync(gameRoot, install.BackupDirectory);

        Assert.False(File.Exists(cachePath));
        string invalidationDirectory = Path.Combine(
            install.BackupDirectory,
            ".cache-invalidations");
        Assert.Contains(
            Directory.EnumerateFiles(invalidationDirectory, "*.cbo"),
            file => File.ReadAllText(file) == "rebuilt-mod-cache");
    }

    [Fact]
    public async Task Repeated_restore_keeps_only_the_latest_cache_invalidation_backup()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        CreateFile(gameRoot, targetRelative, "vanilla");
        (string zipPath, ModManifest manifest) = CreatePayload("modded", targetRelative);
        var installer = new ModInstaller();
        InstallResult install = await installer.InstallAsync(
            gameRoot,
            zipPath,
            manifest,
            backupRoot);
        CreateFile(gameRoot, "Cache/Bundle/core_data.cbo", "first-cache");
        await installer.RestoreAsync(gameRoot, install.BackupDirectory);
        CreateFile(gameRoot, "Cache/Bundle/core_data.cbo", "second-cache");

        await installer.RestoreAsync(gameRoot, install.BackupDirectory);

        string invalidationDirectory = Path.Combine(
            install.BackupDirectory,
            ".cache-invalidations");
        string backup = Assert.Single(
            Directory.EnumerateFiles(invalidationDirectory, "*.cbo"));
        Assert.Equal("second-cache", File.ReadAllText(backup));
    }

    [Fact]
    public async Task Restore_accepts_a_v0_1_0_snapshot_without_cache_metadata()
    {
        string gameRoot = Path.Combine(_root, "game");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string targetPath = CreateFile(gameRoot, targetRelative, "modded");
        string cachePath = CreateFile(
            gameRoot,
            "Cache/Bundle/core_data.cbo",
            "stale-mod-cache");
        string snapshotRoot = Path.Combine(_root, "v0.1.0-snapshot");
        CreateFile(snapshotRoot, targetRelative, "vanilla");
        File.WriteAllText(
            Path.Combine(snapshotRoot, ".snapshot.json"),
            "{\"Files\":[{\"Target\":\"" + targetRelative +
            "\",\"HadOriginal\":true}]}");
        var installer = new ModInstaller();

        bool cacheBundleInvalidated = await installer.RestoreAsync(
            gameRoot,
            snapshotRoot);

        Assert.Equal("vanilla", File.ReadAllText(targetPath));
        Assert.True(cacheBundleInvalidated);
        Assert.False(File.Exists(cachePath));
    }

    [Fact]
    public async Task Install_cache_invalidation_failure_rolls_back_mod_files()
    {
        if (!OperatingSystem.IsWindows()) return;

        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string targetPath = CreateFile(gameRoot, targetRelative, "vanilla");
        string cachePath = CreateFile(
            gameRoot,
            "Cache/Bundle/core_data.cbo",
            "locked-cache");
        (string zipPath, ModManifest manifest) = CreatePayload("modded", targetRelative);
        var installer = new ModInstaller();
        using FileStream cacheLock = new(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            installer.InstallAsync(gameRoot, zipPath, manifest, backupRoot));

        Assert.Equal("vanilla", File.ReadAllText(targetPath));
        Assert.Equal("locked-cache", File.ReadAllText(cachePath));
    }

    [Fact]
    public async Task Restore_cache_invalidation_failure_rolls_back_restored_files()
    {
        if (!OperatingSystem.IsWindows()) return;

        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string targetPath = CreateFile(gameRoot, targetRelative, "vanilla");
        (string zipPath, ModManifest manifest) = CreatePayload("modded", targetRelative);
        var installer = new ModInstaller();
        InstallResult install = await installer.InstallAsync(
            gameRoot,
            zipPath,
            manifest,
            backupRoot);
        string cachePath = CreateFile(
            gameRoot,
            "Cache/Bundle/core_data.cbo",
            "locked-cache");
        using FileStream cacheLock = new(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            installer.RestoreAsync(gameRoot, install.BackupDirectory));

        Assert.Equal("modded", File.ReadAllText(targetPath));
        Assert.Equal("locked-cache", File.ReadAllText(cachePath));
    }

    [Fact]
    public async Task Install_failure_rolls_back_and_removes_temporary_files()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetOne =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        const string blockedTarget =
            "Survival/Scripts/game/loot/lootsources/robots_01/existing-directory";
        string targetOnePath = CreateFile(gameRoot, targetOne, "vanilla-one");
        string blockedTargetPath = Path.Combine(
            gameRoot,
            blockedTarget.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(blockedTargetPath);
        Directory.CreateDirectory(_root);
        string zipPath = Path.Combine(_root, "install-failure.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "robots_01/lootsource_haybot.lua", "modded-one");
            WriteEntry(archive, "robots_01/lootsource_tapebot.lua", "modded-two");
        }
        var manifest = new ModManifest
        {
            SchemaVersion = 1,
            ModId = "robot-loot",
            Version = "1.0.0",
            PayloadAsset = "robots_01.zip",
            PayloadSha256 = HashFile(zipPath),
            SupportedBuildIds = ["24529696"],
            Files =
            [
                new ModFileEntry
                {
                    Source = "robots_01/lootsource_haybot.lua",
                    Target = targetOne,
                    Sha256 = HashText("modded-one"),
                },
                new ModFileEntry
                {
                    Source = "robots_01/lootsource_tapebot.lua",
                    Target = blockedTarget,
                    Sha256 = HashText("modded-two"),
                },
            ],
        };
        var installer = new ModInstaller();

        Exception error = await Assert.ThrowsAnyAsync<Exception>(() =>
            installer.InstallAsync(gameRoot, zipPath, manifest, backupRoot));
        Assert.True(
            error is UnauthorizedAccessException or IOException,
            $"Unexpected exception type: {error.GetType().FullName}");

        Assert.Equal("vanilla-one", File.ReadAllText(targetOnePath));
        Assert.True(Directory.Exists(blockedTargetPath));
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(blockedTargetPath)!,
            "*.smmm-new-*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Restore_failure_rolls_back_files_already_restored()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetOne =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        const string targetTwo =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_tapebot.lua";
        string targetOnePath = CreateFile(gameRoot, targetOne, "vanilla-one");
        string targetTwoPath = CreateFile(gameRoot, targetTwo, "vanilla-two");
        Directory.CreateDirectory(_root);
        string zipPath = Path.Combine(_root, "two-files.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "robots_01/lootsource_haybot.lua", "modded-one");
            WriteEntry(archive, "robots_01/lootsource_tapebot.lua", "modded-two");
        }
        var manifest = new ModManifest
        {
            SchemaVersion = 1,
            ModId = "robot-loot",
            Version = "1.0.0",
            PayloadAsset = "robots_01.zip",
            PayloadSha256 = HashFile(zipPath),
            SupportedBuildIds = ["24529696"],
            Files =
            [
                new ModFileEntry
                {
                    Source = "robots_01/lootsource_haybot.lua",
                    Target = targetOne,
                    Sha256 = HashText("modded-one"),
                },
                new ModFileEntry
                {
                    Source = "robots_01/lootsource_tapebot.lua",
                    Target = targetTwo,
                    Sha256 = HashText("modded-two"),
                },
            ],
        };
        var installer = new ModInstaller();
        InstallResult install = await installer.InstallAsync(
            gameRoot,
            zipPath,
            manifest,
            backupRoot);
        File.Delete(Path.Combine(
            install.BackupDirectory,
            targetTwo.Replace('/', Path.DirectorySeparatorChar)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.RestoreAsync(gameRoot, install.BackupDirectory));

        Assert.Equal("modded-one", File.ReadAllText(targetOnePath));
        Assert.Equal("modded-two", File.ReadAllText(targetTwoPath));
    }

    [Fact]
    public async Task Hash_failure_does_not_change_the_game_file()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string targetPath = CreateFile(gameRoot, targetRelative, "vanilla");
        (string zipPath, ModManifest validManifest) = CreatePayload("modded", targetRelative);
        ModManifest invalidManifest = CopyManifest(
            validManifest,
            files:
            [
                new ModFileEntry
                {
                    Source = validManifest.Files[0].Source,
                    Target = targetRelative,
                    Sha256 = new string('A', 64),
                },
            ]);
        var installer = new ModInstaller();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(gameRoot, zipPath, invalidManifest, backupRoot));

        Assert.Equal("vanilla", File.ReadAllText(targetPath));
    }

    [Fact]
    public async Task Oversized_zip_entry_is_rejected_before_writing()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string targetPath = CreateFile(gameRoot, targetRelative, "vanilla");
        (string zipPath, ModManifest manifest) = CreatePayload(
            new string('x', 64),
            targetRelative);
        var installer = new ModInstaller(
            packageLimits: new ModulePackageLimits(
                MaxPackageBytes: 4096,
                MaxEntries: 16,
                MaxSingleEntryBytes: 16,
                MaxTotalUncompressedBytes: 32,
                MaxManifestBytes: 4096));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(gameRoot, zipPath, manifest, backupRoot));

        Assert.Contains("entry size limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("vanilla", File.ReadAllText(targetPath));
    }

    [Fact]
    public async Task Zip_path_traversal_is_rejected_before_writing()
    {
        string gameRoot = Path.Combine(_root, "game");
        string backupRoot = Path.Combine(_root, "backups");
        const string targetRelative =
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua";
        string targetPath = CreateFile(gameRoot, targetRelative, "vanilla");
        (string zipPath, ModManifest manifest) = CreatePayload(
            "modded",
            targetRelative,
            includeTraversalEntry: true);
        var installer = new ModInstaller();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(gameRoot, zipPath, manifest, backupRoot));

        Assert.Equal("vanilla", File.ReadAllText(targetPath));
        Assert.False(File.Exists(Path.Combine(_root, "evil.lua")));
    }

    private (string ZipPath, ModManifest Manifest) CreatePayload(
        string content,
        string targetRelative,
        bool includeTraversalEntry = false)
    {
        Directory.CreateDirectory(_root);
        string zipPath = Path.Combine(_root, $"payload-{Guid.NewGuid():N}.zip");
        const string sourceRelative = "robots_01/lootsource_haybot.lua";
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, sourceRelative, content);
            if (includeTraversalEntry) WriteEntry(archive, "../evil.lua", "evil");
        }

        return (zipPath, new ModManifest
        {
            SchemaVersion = 1,
            ModId = "robot-loot",
            Version = "1.0.0",
            PayloadAsset = "robots_01.zip",
            PayloadSha256 = HashFile(zipPath),
            SupportedBuildIds = ["24529696"],
            Files =
            [
                new ModFileEntry
                {
                    Source = sourceRelative,
                    Target = targetRelative,
                    Sha256 = HashText(content),
                },
            ],
        });
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }

    private static string CreateFile(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static ModManifest CopyManifest(
        ModManifest source,
        IReadOnlyList<ModFileEntry> files) => new()
        {
            SchemaVersion = source.SchemaVersion,
            ModId = source.ModId,
            Version = source.Version,
            PayloadAsset = source.PayloadAsset,
            PayloadSha256 = source.PayloadSha256,
            SupportedBuildIds = source.SupportedBuildIds,
            Files = files,
        };

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
