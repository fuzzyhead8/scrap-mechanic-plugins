namespace ScrapMechanicModManager.Core.Updates;

public sealed record ModuleRegistryEntry(
    string ModId,
    IReadOnlyList<ModuleCandidate> Candidates,
    ModuleSourceKind SelectedSource,
    ModuleCandidate SelectedCandidate)
{
    public bool CanInstall => SelectedCandidate.CanInstall;

    public bool HasSourceChoice => Candidates
        .Select(candidate => candidate.SourceKind)
        .Distinct()
        .Skip(1)
        .Any();
}

public sealed record ModuleTargetConflict(
    string Target,
    IReadOnlyList<string> ModuleIds);

public sealed class ModuleRegistry
{
    private readonly Dictionary<string, ModuleRegistryEntry> _entriesById;

    private ModuleRegistry(IReadOnlyList<ModuleRegistryEntry> entries)
    {
        Entries = entries;
        _entriesById = entries.ToDictionary(
            entry => entry.ModId,
            StringComparer.OrdinalIgnoreCase);
        DefaultSelectedModuleIds = entries
            .Where(entry => entry.SelectedCandidate.DefaultSelected)
            .Select(entry => entry.ModId)
            .ToArray();
    }

    public IReadOnlyList<ModuleRegistryEntry> Entries { get; }

    public IReadOnlyList<string> DefaultSelectedModuleIds { get; }

    public static ModuleRegistry Create(
        IEnumerable<ModuleCandidate> candidates,
        IReadOnlyDictionary<string, ModuleSourceKind>? sourcePreferences = null,
        string? currentManagerVersion = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var entries = new List<ModuleRegistryEntry>();
        foreach (IGrouping<string, ModuleCandidate> group in candidates
                     .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ModId))
                     .GroupBy(candidate => candidate.ModId, StringComparer.OrdinalIgnoreCase))
        {
            ModuleCandidate[] choices = group
                .Select(candidate => string.IsNullOrWhiteSpace(currentManagerVersion)
                    ? candidate
                    : candidate.ForManagerVersion(currentManagerVersion))
                .OrderBy(candidate => candidate.SourceKind == ModuleSourceKind.Online ? 0 : 1)
                .ToArray();
            ModuleSourceKind preferredSource = ModuleSourceKind.Online;
            if (sourcePreferences is not null
                && sourcePreferences.TryGetValue(group.Key, out ModuleSourceKind storedSource))
            {
                preferredSource = storedSource;
            }

            ModuleCandidate selected = choices.FirstOrDefault(
                    candidate => candidate.SourceKind == preferredSource)
                ?? choices[0];
            entries.Add(new ModuleRegistryEntry(
                selected.ModId,
                choices,
                selected.SourceKind,
                selected));
        }

        return new ModuleRegistry(entries);
    }

    public IReadOnlyList<ModuleTargetConflict> FindTargetConflicts(
        IEnumerable<string> selectedModuleIds)
    {
        ArgumentNullException.ThrowIfNull(selectedModuleIds);
        var selected = new HashSet<string>(
            selectedModuleIds,
            StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, (string Original, List<string> Modules)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (ModuleRegistryEntry entry in Entries.Where(entry => selected.Contains(entry.ModId)))
        {
            foreach (ModFileEntry file in entry.SelectedCandidate.Definition.Files)
            {
                string normalized = file.Target.Replace('\\', '/');
                if (!targets.TryGetValue(normalized, out var target))
                {
                    target = (file.Target, []);
                    targets.Add(normalized, target);
                }
                if (!target.Modules.Contains(entry.ModId, StringComparer.OrdinalIgnoreCase))
                {
                    target.Modules.Add(entry.ModId);
                }
            }
        }

        return targets.Values
            .Where(target => target.Modules.Count > 1)
            .Select(target => new ModuleTargetConflict(target.Original, target.Modules))
            .ToArray();
    }

    public bool TryGetEntry(string modId, out ModuleRegistryEntry? entry) =>
        _entriesById.TryGetValue(modId, out entry);
}
