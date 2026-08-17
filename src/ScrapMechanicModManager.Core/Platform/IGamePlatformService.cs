namespace ScrapMechanicModManager.Core.Platform;

public sealed record GameLaunchCommand(
    string FileName,
    IReadOnlyList<string> Arguments);

public interface IGamePlatformService
{
    bool IsGameRunning();

    GameLaunchCommand CreateLaunchCommand(bool devMode);

    void LaunchGame(bool devMode);
}
