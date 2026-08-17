namespace ScrapMechanicModManager.Core.Steam;

public sealed record SteamInstallation(
    string AppId,
    string Name,
    string BuildId,
    string StateFlags,
    string InstallDirectory,
    string LibraryRoot,
    string GameRoot,
    string AppManifestPath);
