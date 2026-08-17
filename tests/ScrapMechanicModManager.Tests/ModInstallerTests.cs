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

        var method = typeof(ModInstaller).GetMethod("RestoreAsync");
        Assert.NotNull(method);
        var restoreTask = Assert.IsAssignableFrom<Task>(
            method.Invoke(installer, [gameRoot, install.BackupDirectory, CancellationToken.None]));
        await restoreTask;

        Assert.Equal("vanilla", File.ReadAllText(targetPath));
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

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            installer.InstallAsync(gameRoot, zipPath, manifest, backupRoot));

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
