using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ScrapMechanicModManager.Core.Steam;

[SupportedOSPlatform("windows")]
public sealed class SteamRootDiscovery
{
    public IReadOnlyList<string> FindCandidateRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfDirectory(roots, ReadRegistryValue(
            Registry.CurrentUser,
            @"Software\Valve\Steam",
            "SteamPath"));
        AddIfDirectory(roots, ReadRegistryValue(
            Registry.LocalMachine,
            @"Software\Valve\Steam",
            "InstallPath"));
        AddIfDirectory(roots, ReadRegistryValue(
            Registry.LocalMachine,
            @"Software\WOW6432Node\Valve\Steam",
            "InstallPath"));

        string programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        AddIfDirectory(roots, Path.Combine(programFilesX86, "Steam"));
        return roots.ToArray();
    }

    private static string? ReadRegistryValue(
        RegistryKey hive,
        string subKey,
        string valueName)
    {
        try
        {
            using RegistryKey? key = hive.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void AddIfDirectory(ISet<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            roots.Add(Path.GetFullPath(path));
        }
    }
}
