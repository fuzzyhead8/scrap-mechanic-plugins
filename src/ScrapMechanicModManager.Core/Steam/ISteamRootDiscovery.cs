namespace ScrapMechanicModManager.Core.Steam;

public interface ISteamRootDiscovery
{
    IReadOnlyList<string> FindCandidateRoots();
}
