namespace ScrapMechanicModManager.Core.Validation;

public sealed record GameInstallValidationResult(
    bool IsValid,
    string? ProductVersion,
    string? SteamBuildId,
    IReadOnlyList<string> Errors);

public sealed class GameInstallValidator
{
    public const string ScrapMechanicAppId = "387990";

    public static IReadOnlyList<string> RequiredRelativePaths { get; } =
    [
        "Release/ScrapMechanic.exe",
        "Survival/Scripts/game/survival_loot.lua",
        "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua",
        "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_tapebot.lua",
        "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_totebot_blue.lua",
        "Survival/Scripts/game/loot/lootsources/robots_01/lootsource_totebot_green.lua",
    ];

    public GameInstallValidationResult Validate(
        string gameRoot,
        string? productVersion,
        string? steamBuildId,
        IReadOnlyCollection<string> supportedBuildIds)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            errors.Add($"A Scrap Mechanic könyvtár nem létezik: {gameRoot}");
        }
        else
        {
            foreach (string relativePath in RequiredRelativePaths)
            {
                string fullPath = Path.Combine(
                    gameRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    errors.Add($"Hiányzó Scrap Mechanic 1.0 fájl: {relativePath}");
                }
            }
        }

        if (!Version.TryParse(productVersion, out Version? version) || version.Major != 1)
        {
            errors.Add(
                $"Nem támogatott játékverzió: {productVersion ?? "ismeretlen"}. Scrap Mechanic 1.0 szükséges.");
        }

        if (string.IsNullOrWhiteSpace(steamBuildId))
        {
            errors.Add("A Steam buildid nem állapítható meg.");
        }
        else if (supportedBuildIds.Count > 0 && !supportedBuildIds.Contains(steamBuildId))
        {
            errors.Add($"A Steam buildid nem támogatott: {steamBuildId}.");
        }

        return new GameInstallValidationResult(
            errors.Count == 0,
            productVersion,
            steamBuildId,
            errors);
    }
}
