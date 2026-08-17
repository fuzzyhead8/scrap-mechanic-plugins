namespace ScrapMechanicModManager.Core.Steam;

public sealed class LinuxSteamRootDiscovery : ISteamRootDiscovery
{
    private static readonly string[] RelativeCandidateRoots =
    [
        ".local/share/Steam",
        ".steam/steam",
        ".var/app/com.valvesoftware.Steam/.local/share/Steam",
    ];

    private readonly string _homeDirectory;

    public LinuxSteamRootDiscovery(string? homeDirectory = null)
    {
        _homeDirectory = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : homeDirectory;
    }

    public IReadOnlyList<string> FindCandidateRoots()
    {
        if (string.IsNullOrWhiteSpace(_homeDirectory))
        {
            return [];
        }

        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (string relativeRoot in RelativeCandidateRoots)
        {
            string candidate = Path.GetFullPath(Path.Combine(
                _homeDirectory,
                relativeRoot.Replace('/', Path.DirectorySeparatorChar)));
            if (Directory.Exists(candidate))
            {
                roots.Add(candidate);
            }
        }
        return roots.ToArray();
    }
}
