using System.Security.Cryptography;
using System.Text;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ModuleStatusEvaluatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public async Task Vanilla_target_without_module_snapshot_is_not_installed()
    {
        ModManifest manifest = CreateManifest("modded");
        CreateTarget("vanilla");
        var evaluator = new ModuleStatusEvaluator();

        ModuleInstallState state = await evaluator.EvaluateAsync(
            _root,
            Path.Combine(_root, "backups"),
            manifest);

        Assert.Equal(ModuleInstallState.NotInstalled, state);
    }

    [Fact]
    public async Task Matching_target_is_up_to_date_without_requiring_a_snapshot()
    {
        ModManifest manifest = CreateManifest("modded");
        CreateTarget("modded");
        var evaluator = new ModuleStatusEvaluator();

        ModuleInstallState state = await evaluator.EvaluateAsync(
            _root,
            Path.Combine(_root, "backups"),
            manifest);

        Assert.Equal(ModuleInstallState.UpToDate, state);
    }

    [Fact]
    public async Task Mismatching_target_with_module_snapshot_has_an_update_available()
    {
        ModManifest manifest = CreateManifest("new-version");
        CreateTarget("old-version");
        string snapshot = Path.Combine(_root, "backups", "20260101-module");
        Directory.CreateDirectory(snapshot);
        await File.WriteAllTextAsync(
            Path.Combine(snapshot, ".snapshot.json"),
            """
            {
              "files": [
                {
                  "modId": "example-module",
                  "target": "Survival/Scripts/example.lua",
                  "hadOriginal": true
                }
              ]
            }
            """);
        var evaluator = new ModuleStatusEvaluator();

        ModuleInstallState state = await evaluator.EvaluateAsync(
            _root,
            Path.Combine(_root, "backups"),
            manifest);

        Assert.Equal(ModuleInstallState.UpdateAvailable, state);
    }

    [Fact]
    public async Task Target_matching_the_snapshot_backup_is_not_installed_after_restore()
    {
        ModManifest manifest = CreateManifest("modded");
        CreateTarget("vanilla");
        string snapshot = Path.Combine(_root, "backups", "20260101-module");
        string backupTarget = Path.Combine(
            snapshot,
            "Survival",
            "Scripts",
            "example.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
        await File.WriteAllTextAsync(backupTarget, "vanilla", new UTF8Encoding(false));
        await File.WriteAllTextAsync(
            Path.Combine(snapshot, ".snapshot.json"),
            """
            {
              "files": [
                {
                  "modId": "example-module",
                  "target": "Survival/Scripts/example.lua",
                  "hadOriginal": true
                }
              ]
            }
            """);
        var evaluator = new ModuleStatusEvaluator();

        ModuleInstallState state = await evaluator.EvaluateAsync(
            _root,
            Path.Combine(_root, "backups"),
            manifest);

        Assert.Equal(ModuleInstallState.NotInstalled, state);
    }

    private ModManifest CreateManifest(string expectedContent) => new()
    {
        SchemaVersion = 1,
        ModId = "example-module",
        Version = "1.0.0",
        PayloadAsset = "example.zip",
        PayloadSha256 = new string('A', 64),
        SupportedBuildIds = ["24529696"],
        Files =
        [
            new ModFileEntry
            {
                Source = "example/module.lua",
                Target = "Survival/Scripts/example.lua",
                Sha256 = Convert.ToHexString(SHA256.HashData(
                    new UTF8Encoding(false).GetBytes(expectedContent))),
            },
        ],
    };

    private void CreateTarget(string content)
    {
        string target = Path.Combine(_root, "Survival", "Scripts", "example.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
