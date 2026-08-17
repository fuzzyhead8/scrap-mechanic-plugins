using System.Collections;
using System.Reflection;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager.Tests;

public sealed class SteamLibraryLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sm-mod-manager-steam-{Guid.NewGuid():N}");

    [Fact]
    public void Locator_finds_Scrap_Mechanic_in_a_secondary_Steam_library()
    {
        string steamRoot = Path.Combine(_root, "Steam");
        string secondaryLibrary = Path.Combine(_root, "Secondary Library");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(secondaryLibrary, "steamapps", "common", "Scrap Mechanic"));

        string escapedLibrary = secondaryLibrary.Replace("\\", "\\\\");
        File.WriteAllText(
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0" { "path" "{{steamRoot.Replace("\\", "\\\\")}}" }
                "1" { "path" "{{escapedLibrary}}" "apps" { "387990" "20784162113" } }
            }
            """);
        File.WriteAllText(
            Path.Combine(secondaryLibrary, "steamapps", "appmanifest_387990.acf"),
            """
            "AppState"
            {
                "appid" "387990"
                "name" "Scrap Mechanic"
                "StateFlags" "4"
                "installdir" "Scrap Mechanic"
                "buildid" "24529696"
            }
            """);

        object installation = Assert.Single(InvokeFindInstallations(steamRoot));

        Assert.Equal("387990", ReadProperty<string>(installation, "AppId"));
        Assert.Equal("24529696", ReadProperty<string>(installation, "BuildId"));
        Assert.Equal("4", ReadProperty<string>(installation, "StateFlags"));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(secondaryLibrary, "steamapps", "common", "Scrap Mechanic")),
            ReadProperty<string>(installation, "GameRoot"));
    }

    [Fact]
    public void Locator_ignores_a_manifest_with_the_wrong_app_id()
    {
        string steamRoot = Path.Combine(_root, "Steam");
        string gameRoot = Path.Combine(steamRoot, "steamapps", "common", "Scrap Mechanic");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(
            Path.Combine(steamRoot, "steamapps", "appmanifest_387990.acf"),
            """
            "AppState"
            {
                "appid" "123"
                "StateFlags" "4"
                "installdir" "Scrap Mechanic"
                "buildid" "24529696"
            }
            """);

        Assert.Empty(InvokeFindInstallations(steamRoot));
    }

    private static IReadOnlyList<object> InvokeFindInstallations(string steamRoot)
    {
        Assembly assembly = typeof(GameInstallValidator).Assembly;
        Type? locatorType = assembly.GetType(
            "ScrapMechanicModManager.Core.Steam.SteamLibraryLocator");
        Assert.NotNull(locatorType);

        object locator = Activator.CreateInstance(locatorType)!;
        MethodInfo? method = locatorType.GetMethod("FindInstallations");
        Assert.NotNull(method);

        var result = Assert.IsAssignableFrom<IEnumerable>(
            method.Invoke(locator, [steamRoot]));
        return result.Cast<object>().ToArray();
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        PropertyInfo? property = instance.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property.GetValue(instance));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
