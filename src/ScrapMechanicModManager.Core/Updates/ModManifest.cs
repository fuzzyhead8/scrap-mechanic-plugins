using System.Text.RegularExpressions;

namespace ScrapMechanicModManager.Core.Updates;

public sealed partial class ModManifest
{
    private static readonly HashSet<string> ReservedWindowsDeviceNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public int SchemaVersion { get; init; }
    public string ModId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string PayloadAsset { get; init; } = string.Empty;
    public string PayloadSha256 { get; init; } = string.Empty;
    public IReadOnlyList<string> SupportedBuildIds { get; init; } = [];
    public IReadOnlyList<ModFileEntry> Files { get; init; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != 1) errors.Add($"Unsupported SchemaVersion: {SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(ModId)) errors.Add("ModId is required.");
        if (!IsSemanticVersion(Version)) errors.Add($"Invalid Version: {Version}.");
        if (!IsSafeAssetName(PayloadAsset)) errors.Add($"Invalid PayloadAsset: {PayloadAsset}.");
        if (!IsSha256(PayloadSha256)) errors.Add("PayloadSha256 must be 64 hexadecimal characters.");
        if (SupportedBuildIds.Count == 0) errors.Add("At least one SupportedBuildId is required.");
        if (Files.Count == 0) errors.Add("At least one file mapping is required.");

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModFileEntry file in Files)
        {
            if (!IsSafeRelativePath(file.Source))
            {
                errors.Add($"Invalid Source path: {file.Source}.");
            }
            if (!IsSafeRelativePath(file.Target))
            {
                errors.Add($"Invalid Target path: {file.Target}.");
            }
            if (!IsSha256(file.Sha256))
            {
                errors.Add($"Sha256 is invalid for {file.Source}.");
            }
            string normalizedTarget = file.Target.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(normalizedTarget) && !targets.Add(normalizedTarget))
            {
                errors.Add($"Duplicate Target path: {file.Target}.");
            }
        }

        return errors;
    }

    public bool SupportsBuild(string buildId) =>
        SupportedBuildIds.Contains(buildId, StringComparer.Ordinal);

    public static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
        {
            return false;
        }

        string normalized = value.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.EndsWith('/')) return false;

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(IsSafePathSegment);
    }

    public static bool IsSafeAssetName(string value) =>
        IsSafeRelativePath(value)
        && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)
        && !value.Contains("..");

    public static bool IsSemanticVersion(string value) =>
        SemanticVersionRegex().IsMatch(value ?? string.Empty);

    private static bool IsSha256(string value) => Sha256Regex().IsMatch(value ?? string.Empty);

    private static bool IsSafePathSegment(string segment)
    {
        if (segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' '))
        {
            return false;
        }
        if (segment.Any(character => character < ' '
            || character is '<' or '>' or '"' or '|' or '?' or '*'))
        {
            return false;
        }

        string deviceName = segment.Split('.', 2)[0];
        return !ReservedWindowsDeviceNames.Contains(deviceName);
    }

    [GeneratedRegex(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)" +
        "(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?" +
        "(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

public sealed class ModFileEntry
{
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
}
