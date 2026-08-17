namespace ScrapMechanicModManager.Core.Steam;

public static class SteamPathIdentity
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path.Trim());
        string root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The path has no filesystem root.", nameof(path));
        string relativePath = Path.GetRelativePath(root, fullPath);
        if (relativePath == ".")
        {
            return Path.TrimEndingDirectorySeparator(fullPath);
        }

        string current = root;
        foreach (string segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string next = Path.Combine(current, segment);
            FileSystemInfo? entry = Directory.Exists(next)
                ? new DirectoryInfo(next)
                : File.Exists(next)
                    ? new FileInfo(next)
                    : null;
            current = ResolveLink(entry)?.FullName ?? next;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    public static bool AreEquivalent(string left, string right) =>
        string.Equals(
            Normalize(left),
            Normalize(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static FileSystemInfo? ResolveLink(FileSystemInfo? entry)
    {
        if (entry is null)
        {
            return null;
        }

        try
        {
            return entry.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
