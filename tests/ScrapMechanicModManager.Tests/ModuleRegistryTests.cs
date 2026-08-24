using ScrapMechanicModManager.Core.Localization;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Tests;

public sealed class ModuleRegistryTests
{
    private const string ValidHash =
        "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824";

    [Fact]
    public void Online_candidate_creates_an_installable_registry_entry()
    {
        ModuleCandidate candidate = Candidate(
            "example-mod",
            ModuleSourceKind.Online,
            "Survival/Scripts/example.lua",
            defaultSelected: true);

        ModuleRegistry registry = ModuleRegistry.Create([candidate]);

        ModuleRegistryEntry entry = Assert.Single(registry.Entries);
        Assert.Equal("example-mod", entry.ModId);
        Assert.Equal(ModuleSourceKind.Online, entry.SelectedSource);
        Assert.Same(candidate, entry.SelectedCandidate);
        Assert.True(entry.CanInstall);
        Assert.Equal(["example-mod"], registry.DefaultSelectedModuleIds);
    }

    [Fact]
    public void Online_and_local_candidates_require_an_explicit_local_preference()
    {
        ModuleCandidate online = Candidate(
            "example-mod",
            ModuleSourceKind.Online,
            "Survival/Scripts/online.lua");
        ModuleCandidate local = Candidate(
            "EXAMPLE-MOD",
            ModuleSourceKind.Local,
            "Survival/Scripts/local.lua");

        ModuleRegistry defaultRegistry = ModuleRegistry.Create([local, online]);
        ModuleRegistry localRegistry = ModuleRegistry.Create(
            [local, online],
            new Dictionary<string, ModuleSourceKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["example-mod"] = ModuleSourceKind.Local,
            });

        ModuleRegistryEntry defaultEntry = Assert.Single(defaultRegistry.Entries);
        Assert.Equal(2, defaultEntry.Candidates.Count);
        Assert.Equal(ModuleSourceKind.Online, defaultEntry.SelectedSource);
        Assert.Same(online, defaultEntry.SelectedCandidate);

        ModuleRegistryEntry localEntry = Assert.Single(localRegistry.Entries);
        Assert.Equal(ModuleSourceKind.Local, localEntry.SelectedSource);
        Assert.Same(local, localEntry.SelectedCandidate);
    }

    [Fact]
    public void Localized_name_uses_the_requested_language_and_safe_fallback()
    {
        var translated = new LocalizedModuleText("Magyar név", "English name");
        var hungarianOnly = new LocalizedModuleText("Csak magyar", string.Empty);

        Assert.Equal("Magyar név", translated.Get(AppLanguage.Hungarian));
        Assert.Equal("English name", translated.Get(AppLanguage.English));
        Assert.Equal("Csak magyar", hungarianOnly.Get(AppLanguage.English));
    }

    [Fact]
    public void Selected_candidates_report_target_conflicts_without_hiding_other_modules()
    {
        ModuleCandidate first = Candidate(
            "first",
            ModuleSourceKind.Online,
            "Survival/Scripts/shared.lua");
        ModuleCandidate second = Candidate(
            "second",
            ModuleSourceKind.Local,
            "survival/scripts/SHARED.lua");
        ModuleCandidate independent = Candidate(
            "independent",
            ModuleSourceKind.Online,
            "Survival/Scripts/independent.lua");
        ModuleRegistry registry = ModuleRegistry.Create([first, second, independent]);

        IReadOnlyList<ModuleTargetConflict> conflicts = registry.FindTargetConflicts(
            ["first", "second", "independent"]);

        ModuleTargetConflict conflict = Assert.Single(conflicts);
        Assert.Equal("Survival/Scripts/shared.lua", conflict.Target, ignoreCase: true);
        Assert.Equal(["first", "second"], conflict.ModuleIds.OrderBy(id => id));
        Assert.Equal(3, registry.Entries.Count);
    }

    private static ModuleCandidate Candidate(
        string modId,
        ModuleSourceKind source,
        string target,
        bool defaultSelected = false)
    {
        var definition = new ModulePackageDefinition
        {
            SchemaVersion = 1,
            ModId = modId,
            Version = "1.0.0",
            DisplayName = new LocalizedModuleText("Példa", "Example"),
            Description = new LocalizedModuleText("Leírás", "Description"),
            MinimumManagerVersion = "0.2.0",
            SupportedBuildIds = ["24529696"],
            Files =
            [
                new ModFileEntry
                {
                    Source = "payload/example.lua",
                    Target = target,
                    Sha256 = ValidHash,
                },
            ],
        };

        return new ModuleCandidate(
            definition,
            source,
            ValidHash,
            source == ModuleSourceKind.Online
                ? new Uri($"https://github.com/example/releases/download/v1/{modId}.smmmod")
                : null,
            source == ModuleSourceKind.Local ? $"C:/mods/{modId}.smmmod" : null,
            defaultSelected,
            []);
    }
}
