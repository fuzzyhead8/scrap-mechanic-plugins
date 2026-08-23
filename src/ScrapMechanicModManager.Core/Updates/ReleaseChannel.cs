namespace ScrapMechanicModManager.Core.Updates;

public static class ReleaseChannel
{
    public static string? GetReleaseTag(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        string version = informationalVersion.Split('+', 2)[0].Trim();
        if (!version.Contains('-', StringComparison.Ordinal))
        {
            return null;
        }

        return version.StartsWith('v') ? version : $"v{version}";
    }
}
