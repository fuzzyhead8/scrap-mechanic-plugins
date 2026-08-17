using System.Reflection;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager.Tests;

public sealed class GameInstallValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sm-mod-manager-validator-{Guid.NewGuid():N}");

    [Fact]
    public void Validator_rejects_a_missing_1_0_directory_structure()
    {
        Directory.CreateDirectory(_root);

        object result = InvokeValidate(
            _root,
            "1.0.5.876",
            "24529696",
            ["24529696"]);

        Assert.False(ReadProperty<bool>(result, "IsValid"));
        Assert.Contains(
            ReadProperty<IReadOnlyList<string>>(result, "Errors"),
            error => error.Contains("lootsource_haybot.lua", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_accepts_the_supported_1_0_structure()
    {
        CreateRequiredGameFiles();

        object result = InvokeValidate(
            _root,
            "1.0.5.876",
            "24529696",
            ["24529696"]);

        Assert.True(ReadProperty<bool>(result, "IsValid"));
        Assert.Empty(ReadProperty<IReadOnlyList<string>>(result, "Errors"));
    }

    [Fact]
    public void Validator_rejects_pre_1_0_versions()
    {
        CreateRequiredGameFiles();

        object result = InvokeValidate(
            _root,
            "0.7.3.776",
            "24529696",
            ["24529696"]);

        Assert.False(ReadProperty<bool>(result, "IsValid"));
        Assert.Contains(
            ReadProperty<IReadOnlyList<string>>(result, "Errors"),
            error => error.Contains("1.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_rejects_an_unapproved_steam_build()
    {
        CreateRequiredGameFiles();

        object result = InvokeValidate(
            _root,
            "1.0.5.876",
            "99999999",
            ["24529696"]);

        Assert.False(ReadProperty<bool>(result, "IsValid"));
        Assert.Contains(
            ReadProperty<IReadOnlyList<string>>(result, "Errors"),
            error => error.Contains("99999999", StringComparison.Ordinal));
    }

    private static object InvokeValidate(
        string gameRoot,
        string productVersion,
        string buildId,
        IReadOnlyCollection<string> supportedBuildIds)
    {
        var validator = new GameInstallValidator();
        MethodInfo? method = typeof(GameInstallValidator).GetMethod("Validate");
        Assert.NotNull(method);

        return method.Invoke(
            validator,
            [gameRoot, productVersion, buildId, supportedBuildIds])!;
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        PropertyInfo? property = instance.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property.GetValue(instance));
    }

    private void CreateRequiredGameFiles()
    {
        string[] relativePaths =
        [
            "Release/ScrapMechanic.exe",
            "Survival/Scripts/game/survival_loot.lua",
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua",
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_tapebot.lua",
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_totebot_blue.lua",
            "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_totebot_green.lua",
        ];

        foreach (string relativePath in relativePaths)
        {
            string fullPath = Path.Combine(
                _root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "fixture");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
