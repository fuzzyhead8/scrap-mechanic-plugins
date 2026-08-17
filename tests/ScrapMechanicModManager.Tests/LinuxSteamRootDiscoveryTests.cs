using ScrapMechanicModManager.Core.Steam;

namespace ScrapMechanicModManager.Tests;

public sealed class LinuxSteamRootDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sm-mod-manager-linux-steam-{Guid.NewGuid():N}");

    [Fact]
    public void Discovery_returns_existing_native_legacy_and_flatpak_roots()
    {
        string home = Path.Combine(_root, "home");
        string native = CreateDirectory(home, ".local/share/Steam");
        string legacy = CreateDirectory(home, ".steam/steam");
        string flatpak = CreateDirectory(
            home,
            ".var/app/com.valvesoftware.Steam/.local/share/Steam");
        var discovery = new LinuxSteamRootDiscovery(home);

        IReadOnlyList<string> roots = discovery.FindCandidateRoots();

        Assert.Equal(
            [Path.GetFullPath(native), Path.GetFullPath(legacy), Path.GetFullPath(flatpak)],
            roots);
    }

    [Fact]
    public void Discovery_skips_candidate_roots_that_do_not_exist()
    {
        string home = Path.Combine(_root, "home");
        Directory.CreateDirectory(home);
        string native = CreateDirectory(home, ".local/share/Steam");
        var discovery = new LinuxSteamRootDiscovery(home);

        IReadOnlyList<string> roots = discovery.FindCandidateRoots();

        Assert.Equal([Path.GetFullPath(native)], roots);
    }

    [Fact]
    public void Discovery_uses_the_current_user_home_by_default()
    {
        var discovery = new LinuxSteamRootDiscovery();

        IReadOnlyList<string> roots = discovery.FindCandidateRoots();

        Assert.All(roots, root => Assert.True(Path.IsPathFullyQualified(root)));
    }

    private static string CreateDirectory(string root, string relativePath)
    {
        string path = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
