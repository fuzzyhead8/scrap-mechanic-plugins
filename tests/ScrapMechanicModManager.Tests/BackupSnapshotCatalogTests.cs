using System.Security.Cryptography;
using System.Text.Json;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Settings;

namespace ScrapMechanicModManager.Tests;

public sealed class BackupSnapshotCatalogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Valid_multi_module_snapshot_is_reported_without_changing_backup_files()
    {
        string backupRoot = Path.Combine(_root, "backups");
        string snapshot = CreateSnapshot(
            backupRoot,
            "20260823_122550_747-modules-preview6",
            modules:
            [
                (BuiltInModuleIds.RobotLoot, "0.2.0-preview.6"),
                (BuiltInModuleIds.BeehiveAutomation, "0.2.0-preview.6"),
            ],
            files:
            [
                (BuiltInModuleIds.RobotLoot, "Survival/Scripts/robot.lua", true),
                (BuiltInModuleIds.BeehiveAutomation, "Survival/Scripts/bee.lua", false),
            ]);
        IReadOnlyDictionary<string, string> before = CaptureTree(backupRoot);
        var catalog = new BackupSnapshotCatalog();

        ModuleBackupStatus robot = catalog.GetModuleStatus(
            backupRoot,
            BuiltInModuleIds.RobotLoot);
        ModuleBackupStatus beehive = catalog.GetModuleStatus(
            backupRoot,
            BuiltInModuleIds.BeehiveAutomation);
        IReadOnlyList<BackupSnapshotInfo> snapshots = catalog.Scan(backupRoot);

        Assert.Equal(BackupSnapshotState.Available, robot.State);
        Assert.Equal(snapshot, robot.SnapshotDirectory);
        Assert.Equal("0.2.0-preview.6", robot.Version);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 23, 12, 25, 50, 747, TimeSpan.Zero),
            robot.CreatedAtUtc);
        Assert.Equal(BackupSnapshotState.Available, beehive.State);
        Assert.Equal(snapshot, beehive.SnapshotDirectory);
        BackupSnapshotInfo info = Assert.Single(snapshots);
        Assert.Equal(BackupSnapshotState.Available, info.State);
        Assert.Equal(
            [BuiltInModuleIds.RobotLoot, BuiltInModuleIds.BeehiveAutomation],
            info.ModuleIds);
        Assert.Equal(before, CaptureTree(backupRoot));
    }

    [Fact]
    public void Newer_corrupt_snapshot_does_not_hide_an_older_valid_module_backup()
    {
        string backupRoot = Path.Combine(_root, "backups");
        string valid = CreateSnapshot(
            backupRoot,
            "20260822_100000_000-valid",
            [(BuiltInModuleIds.BeehiveAutomation, "0.2.0-preview.5")],
            [(BuiltInModuleIds.BeehiveAutomation, "Survival/Scripts/bee.lua", true)]);
        CreateSnapshot(
            backupRoot,
            "20260823_100000_000-corrupt",
            [(BuiltInModuleIds.BeehiveAutomation, "0.2.0-preview.6")],
            [(BuiltInModuleIds.BeehiveAutomation, "Survival/Scripts/missing.lua", true)],
            createOriginalBackups: false);
        var catalog = new BackupSnapshotCatalog();

        ModuleBackupStatus status = catalog.GetModuleStatus(
            backupRoot,
            BuiltInModuleIds.BeehiveAutomation);

        Assert.Equal(BackupSnapshotState.Available, status.State);
        Assert.Equal(valid, status.SnapshotDirectory);
        Assert.Equal("0.2.0-preview.5", status.Version);
        Assert.Contains(
            catalog.Scan(backupRoot),
            snapshot => snapshot.State == BackupSnapshotState.Corrupt);
    }

    [Fact]
    public void Corrupt_snapshot_without_a_valid_fallback_is_reported()
    {
        string backupRoot = Path.Combine(_root, "backups");
        CreateSnapshot(
            backupRoot,
            "20260823_100000_000-corrupt",
            [(BuiltInModuleIds.FreezerAutomation, "0.2.0-preview.6")],
            [(BuiltInModuleIds.FreezerAutomation, "../outside.lua", false)]);
        var catalog = new BackupSnapshotCatalog();

        ModuleBackupStatus status = catalog.GetModuleStatus(
            backupRoot,
            BuiltInModuleIds.FreezerAutomation);

        Assert.Equal(BackupSnapshotState.Corrupt, status.State);
        Assert.Null(status.SnapshotDirectory);
    }

    [Fact]
    public void Legacy_snapshot_is_visible_but_not_a_module_restore_candidate()
    {
        string backupRoot = Path.Combine(_root, "backups");
        string snapshot = Path.Combine(backupRoot, "20260817_100000_000-legacy");
        Directory.CreateDirectory(snapshot);
        File.WriteAllText(
            Path.Combine(snapshot, ".snapshot.json"),
            """
            {
              "files": [
                {
                  "target": "Survival/Scripts/legacy.lua",
                  "hadOriginal": true
                }
              ]
            }
            """);
        var catalog = new BackupSnapshotCatalog();

        ModuleBackupStatus status = catalog.GetModuleStatus(
            backupRoot,
            BuiltInModuleIds.RobotLoot);

        Assert.Equal(BackupSnapshotState.Legacy, status.State);
        Assert.Null(status.SnapshotDirectory);
        Assert.Equal(
            BackupSnapshotState.Legacy,
            Assert.Single(catalog.Scan(backupRoot)).State);
    }

    [Fact]
    public void Missing_backup_root_reports_no_backup()
    {
        string backupRoot = Path.Combine(_root, "missing");
        var catalog = new BackupSnapshotCatalog();

        ModuleBackupStatus status = catalog.GetModuleStatus(
            backupRoot,
            BuiltInModuleIds.RobotLoot);

        Assert.Equal(BackupSnapshotState.None, status.State);
        Assert.Null(status.SnapshotDirectory);
        Assert.Empty(catalog.Scan(backupRoot));
    }

    [Fact]
    public void Malformed_and_missing_metadata_are_reported_as_corrupt()
    {
        string backupRoot = Path.Combine(_root, "backups");
        string malformed = Path.Combine(backupRoot, "20260823_100000_000-malformed");
        Directory.CreateDirectory(malformed);
        File.WriteAllText(Path.Combine(malformed, ".snapshot.json"), "{ bad json");
        Directory.CreateDirectory(Path.Combine(
            backupRoot,
            "20260823_110000_000-missing-metadata"));
        var catalog = new BackupSnapshotCatalog();

        IReadOnlyList<BackupSnapshotInfo> snapshots = catalog.Scan(backupRoot);
        ModuleBackupStatus status = catalog.GetModuleStatus(
            backupRoot,
            BuiltInModuleIds.RobotLoot);

        Assert.Equal(2, snapshots.Count);
        Assert.All(
            snapshots,
            snapshot => Assert.Equal(BackupSnapshotState.Corrupt, snapshot.State));
        Assert.Equal(BackupSnapshotState.Corrupt, status.State);
    }

    [Fact]
    public void Valid_unknown_module_does_not_claim_a_built_in_module_backup()
    {
        string backupRoot = Path.Combine(_root, "backups");
        CreateSnapshot(
            backupRoot,
            "20260823_100000_000-other",
            [("other-module", "1.0.0")],
            [("other-module", "Survival/Scripts/other.lua", false)]);
        var catalog = new BackupSnapshotCatalog();

        ModuleBackupStatus status = catalog.GetModuleStatus(
            backupRoot,
            BuiltInModuleIds.RobotLoot);

        Assert.Equal(BackupSnapshotState.None, status.State);
    }

    private static string CreateSnapshot(
        string backupRoot,
        string snapshotName,
        IReadOnlyList<(string ModId, string Version)> modules,
        IReadOnlyList<(string ModId, string Target, bool HadOriginal)> files,
        bool createOriginalBackups = true)
    {
        string snapshot = Path.Combine(backupRoot, snapshotName);
        Directory.CreateDirectory(snapshot);
        var metadata = new
        {
            schemaVersion = 2,
            modules = modules.Select(module => new
            {
                modId = module.ModId,
                version = module.Version,
            }),
            files = files.Select(file => new
            {
                modId = file.ModId,
                target = file.Target,
                hadOriginal = file.HadOriginal,
            }),
        };
        File.WriteAllText(
            Path.Combine(snapshot, ".snapshot.json"),
            JsonSerializer.Serialize(metadata));
        if (createOriginalBackups)
        {
            foreach ((string _, string target, bool hadOriginal) in files)
            {
                if (!hadOriginal || target.Contains("..", StringComparison.Ordinal)) continue;
                string backup = Path.Combine(
                    snapshot,
                    target.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.WriteAllText(backup, "original");
            }
        }
        return snapshot;
    }

    private static IReadOnlyDictionary<string, string> CaptureTree(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
