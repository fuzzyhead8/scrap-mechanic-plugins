using ScrapMechanicModManager.Core.Localization;
using ScrapMechanicModManager.Core.Settings;

namespace ScrapMechanicModManager.Tests;

public sealed class ManagerSettingsStoreTests : IDisposable
{
    private readonly string _temporaryRoot = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public async Task Missing_settings_default_to_Hungarian()
    {
        var store = new ManagerSettingsStore(SettingsPath);

        ManagerSettings settings = await store.LoadAsync();

        Assert.Null(settings.GameRoot);
        Assert.Equal(AppLanguage.Hungarian, settings.Language);
    }

    [Fact]
    public async Task English_and_game_root_round_trip()
    {
        var store = new ManagerSettingsStore(SettingsPath);
        var expected = new ManagerSettings("D:/Steam/Scrap Mechanic", AppLanguage.English);

        await store.SaveAsync(expected);
        ManagerSettings actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
        string json = await File.ReadAllTextAsync(SettingsPath);
        Assert.Contains("\"language\": \"english\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_game_root_settings_keep_the_path_and_default_to_Hungarian()
    {
        Directory.CreateDirectory(_temporaryRoot);
        await File.WriteAllTextAsync(
            SettingsPath,
            """
            {
              "GameRoot": "D:/Legacy/Scrap Mechanic"
            }
            """);
        var store = new ManagerSettingsStore(SettingsPath);

        ManagerSettings settings = await store.LoadAsync();

        Assert.Equal("D:/Legacy/Scrap Mechanic", settings.GameRoot);
        Assert.Equal(AppLanguage.Hungarian, settings.Language);
    }

    [Fact]
    public async Task Malformed_json_falls_back_safely()
    {
        Directory.CreateDirectory(_temporaryRoot);
        await File.WriteAllTextAsync(SettingsPath, "{ definitely not json");
        var store = new ManagerSettingsStore(SettingsPath);

        ManagerSettings settings = await store.LoadAsync();

        Assert.Equal(ManagerSettings.Default, settings);
    }

    [Fact]
    public async Task Unknown_language_falls_back_to_Hungarian_without_losing_game_root()
    {
        Directory.CreateDirectory(_temporaryRoot);
        await File.WriteAllTextAsync(
            SettingsPath,
            """
            {
              "gameRoot": "D:/Steam/Scrap Mechanic",
              "language": "klingon"
            }
            """);
        var store = new ManagerSettingsStore(SettingsPath);

        ManagerSettings settings = await store.LoadAsync();

        Assert.Equal("D:/Steam/Scrap Mechanic", settings.GameRoot);
        Assert.Equal(AppLanguage.Hungarian, settings.Language);
    }

    [Fact]
    public async Task Atomic_save_leaves_no_temporary_file()
    {
        var store = new ManagerSettingsStore(SettingsPath);

        await store.SaveAsync(new ManagerSettings(null, AppLanguage.English));

        Assert.Empty(Directory.EnumerateFiles(_temporaryRoot, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    private string SettingsPath => Path.Combine(_temporaryRoot, "settings.json");

    public void Dispose()
    {
        Directory.Delete(_temporaryRoot, recursive: true);
    }
}
