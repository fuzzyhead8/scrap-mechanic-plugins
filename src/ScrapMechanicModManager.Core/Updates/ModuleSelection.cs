namespace ScrapMechanicModManager.Core.Updates;

public static class ModuleSelection
{
    public static IReadOnlyList<ResolvedModule> FilterAvailable(
        IReadOnlyList<ResolvedModule> availableModules,
        IEnumerable<string> selectedModuleIds)
    {
        ArgumentNullException.ThrowIfNull(availableModules);
        ArgumentNullException.ThrowIfNull(selectedModuleIds);

        var selectedIds = new HashSet<string>(
            selectedModuleIds.Where(moduleId => !string.IsNullOrWhiteSpace(moduleId)),
            StringComparer.OrdinalIgnoreCase);
        return availableModules
            .Where(module => selectedIds.Contains(module.ModId))
            .ToArray();
    }
}
