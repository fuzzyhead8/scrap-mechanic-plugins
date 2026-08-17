using ScrapMechanicModManager.Core.Platform;

namespace ScrapMechanicModManager.Tests;

public sealed class LinuxGamePlatformServiceTests
{
    [Fact]
    public void Native_steam_command_launches_the_supported_app_id()
    {
        var service = new LinuxGamePlatformService(useFlatpakSteam: false);

        GameLaunchCommand command = service.CreateLaunchCommand(devMode: false);

        Assert.Equal("steam", command.FileName);
        Assert.Equal(["-applaunch", "387990"], command.Arguments);
    }

    [Fact]
    public void Native_steam_command_appends_dev_mode_as_a_separate_argument()
    {
        var service = new LinuxGamePlatformService(useFlatpakSteam: false);

        GameLaunchCommand command = service.CreateLaunchCommand(devMode: true);

        Assert.Equal(["-applaunch", "387990", "-dev"], command.Arguments);
    }

    [Fact]
    public void Flatpak_command_uses_the_valve_application_id()
    {
        var service = new LinuxGamePlatformService(useFlatpakSteam: true);

        GameLaunchCommand command = service.CreateLaunchCommand(devMode: false);

        Assert.Equal("flatpak", command.FileName);
        Assert.Equal(
            ["run", "com.valvesoftware.Steam", "-applaunch", "387990"],
            command.Arguments);
    }

    [Theory]
    [InlineData("ScrapMechanic", null, true)]
    [InlineData("ScrapMechanic.exe", null, true)]
    [InlineData("ScrapMechanic.e", null, true)]
    [InlineData("ScrapMechanicModManager", null, false)]
    [InlineData("ScrapMechanicMo", null, false)]
    [InlineData("wine64", "/games/Scrap Mechanic/Release/ScrapMechanic.exe\0", true)]
    [InlineData("pressure-vessel", "steam-runtime", false)]
    [InlineData("mechanic-helper", "helper", false)]
    public void Process_matching_detects_Proton_game_processes(
        string processName,
        string? commandLine,
        bool expected)
    {
        Assert.Equal(
            expected,
            LinuxGamePlatformService.MatchesGameProcess(processName, commandLine));
    }

    [Fact]
    public void Flatpak_detection_uses_the_selected_steam_root()
    {
        Assert.True(LinuxGamePlatformService.IsFlatpakSteamRoot(
            "/home/player/.var/app/com.valvesoftware.Steam/.local/share/Steam"));
        Assert.False(LinuxGamePlatformService.IsFlatpakSteamRoot(
            "/home/player/.local/share/Steam"));
    }
}
