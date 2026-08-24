using System.Text.Json;
using ScrapMechanicModManager.Core.Localization;
using ScrapMechanicModManager.Core.Updates;

namespace ScrapMechanicModManager.Core.Settings;

public sealed class ManagerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public ManagerSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public ManagerSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return ManagerSettings.Default;
        }

        try
        {
            StoredSettings? stored = JsonSerializer.Deserialize<StoredSettings>(
                File.ReadAllText(_settingsPath),
                JsonOptions);
            return ToManagerSettings(stored);
        }
        catch (JsonException)
        {
            return ManagerSettings.Default;
        }
        catch (IOException)
        {
            return ManagerSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return ManagerSettings.Default;
        }
    }

    public async Task<ManagerSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return ManagerSettings.Default;
        }

        try
        {
            await using FileStream stream = File.OpenRead(_settingsPath);
            StoredSettings? stored = await JsonSerializer.DeserializeAsync<StoredSettings>(
                stream,
                JsonOptions,
                cancellationToken);
            return ToManagerSettings(stored);
        }
        catch (JsonException)
        {
            return ManagerSettings.Default;
        }
        catch (IOException)
        {
            return ManagerSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return ManagerSettings.Default;
        }
    }

    public async Task SaveAsync(
        ManagerSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The settings path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $"{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
        var stored = new StoredSettings(
            settings.GameRoot,
            settings.Language == AppLanguage.English ? "english" : "hungarian",
            settings.SelectedModuleIds,
            settings.ModuleSourcePreferences.ToDictionary(
                pair => pair.Key,
                pair => pair.Value == ModuleSourceKind.Local ? "local" : "online",
                StringComparer.OrdinalIgnoreCase));

        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    stored,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static ManagerSettings ToManagerSettings(StoredSettings? stored)
    {
        if (stored is null)
        {
            return ManagerSettings.Default;
        }

        return new ManagerSettings(
            string.IsNullOrWhiteSpace(stored.GameRoot) ? null : stored.GameRoot,
            AppLocalizer.ParseLanguage(stored.Language),
            stored.SelectedModuleIds,
            ParseSourcePreferences(stored.ModuleSourcePreferences));
    }

    private static IReadOnlyDictionary<string, ModuleSourceKind> ParseSourcePreferences(
        IReadOnlyDictionary<string, string>? storedPreferences)
    {
        var parsed = new Dictionary<string, ModuleSourceKind>(
            StringComparer.OrdinalIgnoreCase);
        if (storedPreferences is null) return parsed;

        foreach ((string moduleId, string source) in storedPreferences)
        {
            if (string.IsNullOrWhiteSpace(moduleId)) continue;
            if (string.Equals(source, "online", StringComparison.OrdinalIgnoreCase))
            {
                parsed[moduleId] = ModuleSourceKind.Online;
            }
            else if (string.Equals(source, "local", StringComparison.OrdinalIgnoreCase))
            {
                parsed[moduleId] = ModuleSourceKind.Local;
            }
        }
        return parsed;
    }

    private sealed record StoredSettings(
        string? GameRoot,
        string? Language,
        IReadOnlyList<string>? SelectedModuleIds,
        IReadOnlyDictionary<string, string>? ModuleSourcePreferences);
}
