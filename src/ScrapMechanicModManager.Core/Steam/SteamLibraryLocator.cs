using System.Text.RegularExpressions;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager.Core.Steam;

public sealed partial class SteamLibraryLocator
{
    public IReadOnlyList<SteamInstallation> FindInstallations(string steamRoot)
    {
        if (string.IsNullOrWhiteSpace(steamRoot) || !Directory.Exists(steamRoot))
        {
            return [];
        }

        var libraryRoots = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
        {
            Path.GetFullPath(steamRoot),
        };

        string libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFoldersPath))
        {
            string content = File.ReadAllText(libraryFoldersPath);
            foreach (Match match in LibraryPathRegex().Matches(content))
            {
                string decodedPath = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(decodedPath))
                {
                    libraryRoots.Add(Path.GetFullPath(decodedPath));
                }
            }
        }

        var installations = new List<SteamInstallation>();
        foreach (string libraryRoot in libraryRoots)
        {
            string manifestPath = Path.Combine(
                libraryRoot,
                "steamapps",
                $"appmanifest_{GameInstallValidator.ScrapMechanicAppId}.acf");
            SteamInstallation? installation = ReadInstallation(libraryRoot, manifestPath);
            if (installation is not null)
            {
                installations.Add(installation);
            }
        }

        return installations;
    }

    private static SteamInstallation? ReadInstallation(
        string libraryRoot,
        string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        string content = File.ReadAllText(manifestPath);
        string appId = ReadValue(content, "appid");
        string installDirectory = ReadValue(content, "installdir");
        if (!string.Equals(
                appId,
                GameInstallValidator.ScrapMechanicAppId,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(installDirectory))
        {
            return null;
        }

        string gameRoot = Path.GetFullPath(
            Path.Combine(libraryRoot, "steamapps", "common", installDirectory));
        if (!Directory.Exists(gameRoot))
        {
            return null;
        }

        return new SteamInstallation(
            appId,
            ReadValue(content, "name"),
            ReadValue(content, "buildid"),
            ReadValue(content, "StateFlags"),
            installDirectory,
            Path.GetFullPath(libraryRoot),
            gameRoot,
            Path.GetFullPath(manifestPath));
    }

    private static string ReadValue(string content, string key)
    {
        Match match = Regex.Match(
            content,
            $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]*)\\\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPathRegex();
}
