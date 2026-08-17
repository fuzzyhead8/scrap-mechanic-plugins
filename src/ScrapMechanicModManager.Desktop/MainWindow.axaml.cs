using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Platform;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Steam;
using ScrapMechanicModManager.Core.Updates;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager.Desktop;

public sealed partial class MainWindow : Window
{
    private const string RepositoryOwner = "fuzzyhead8";
    private const string RepositoryName = "scrap-mechanic-plugins";

    private readonly TextBox _gameRoot;
    private readonly Button _browse;
    private readonly Button _check;
    private readonly Button _install;
    private readonly Button _restore;
    private readonly Button _launch;
    private readonly CheckBox _devMode;
    private readonly TextBlock _gameStatus;
    private readonly TextBlock _modStatus;
    private readonly ProgressBar _progress;
    private readonly TextBox _log;

    private readonly ISteamRootDiscovery _steamRootDiscovery =
        new LinuxSteamRootDiscovery();
    private readonly SteamLibraryLocator _steamLibraryLocator = new();
    private readonly GameInstallValidator _gameValidator = new();
    private readonly ExecutableVersionReader _versionReader = new();
    private readonly HashService _hashService = new();
    private readonly ModInstaller _installer = new();
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly GitHubReleaseClient _releaseClient;
    private SteamInstallation? _selectedInstallation;

    private static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScrapMechanicModManager");
    private static string BackupRoot => Path.Combine(AppDataRoot, "backups");
    private static string SettingsPath => Path.Combine(AppDataRoot, "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        _gameRoot = this.FindControl<TextBox>("GameRootTextBox")!;
        _browse = this.FindControl<Button>("BrowseButton")!;
        _check = this.FindControl<Button>("CheckButton")!;
        _install = this.FindControl<Button>("InstallButton")!;
        _restore = this.FindControl<Button>("RestoreButton")!;
        _launch = this.FindControl<Button>("LaunchButton")!;
        _devMode = this.FindControl<CheckBox>("DevModeCheckBox")!;
        _gameStatus = this.FindControl<TextBlock>("GameStatusText")!;
        _modStatus = this.FindControl<TextBlock>("ModStatusText")!;
        _progress = this.FindControl<ProgressBar>("ProgressBar")!;
        _log = this.FindControl<TextBox>("LogTextBox")!;

        _releaseClient = new GitHubReleaseClient(
            _httpClient,
            RepositoryOwner,
            RepositoryName);
        string appVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3)
            ?? "0.2.0";
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ScrapMechanicModManager-Linux", appVersion));

        Opened += async (_, _) => await RunBusyAsync(AutoDetectAsync);
        Closed += (_, _) =>
        {
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            _httpClient.Dispose();
        };
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(BrowseForGameRootAsync);
    }

    private async void OnCheckClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(CheckForUpdatesAsync);
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(InstallLatestAsync);
    }

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(RestoreLatestAsync);
    }

    private async void OnLaunchClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(() =>
        {
            LaunchGame();
            return Task.CompletedTask;
        });
    }

    private async Task AutoDetectAsync()
    {
        ManagerSettings? settings = await LoadSettingsAsync();
        if (!string.IsNullOrWhiteSpace(settings?.GameRoot)
            && Directory.Exists(settings.GameRoot))
        {
            try
            {
                _selectedInstallation = ResolveSelectedInstallation(settings.GameRoot);
                _gameRoot.Text = settings.GameRoot;
                ShowLocalGameStatus();
                Log("Mentett játékútvonal betöltve.");
                return;
            }
            catch (InvalidOperationException error)
            {
                Log("A mentett játékútvonal már nem érvényes: " + error.Message);
            }
        }

        foreach (string steamRoot in _steamRootDiscovery.FindCandidateRoots())
        {
            SteamInstallation? installation = _steamLibraryLocator
                .FindInstallations(steamRoot)
                .FirstOrDefault();
            if (installation is null)
            {
                continue;
            }

            _selectedInstallation = installation;
            _gameRoot.Text = installation.GameRoot;
            ShowLocalGameStatus();
            await SaveSettingsAsync(installation.GameRoot);
            Log($"Steam Proton telepítés automatikusan megtalálva: {installation.GameRoot}");
            return;
        }

        _gameStatus.Text = "Játék: nem található automatikusan";
        Log("A Scrap Mechanic nem található automatikusan. Használd a Tallózás gombot.");
    }

    private async Task BrowseForGameRootAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Válaszd ki a Scrap Mechanic gyökérmappáját",
                AllowMultiple = false,
            });
        string? selectedPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        _gameRoot.Text = selectedPath;
        _selectedInstallation = null;
        _gameStatus.Text = "Játék: útvonal megadva, ellenőrzés szükséges";
        _modStatus.Text = "Mod: nincs ellenőrizve";
    }

    private async Task CheckForUpdatesAsync()
    {
        (SteamInstallation installation, ResolvedRelease release, string productVersion) =
            await ResolveAndValidateLatestAsync();

        bool allCurrent = true;
        foreach (ModFileEntry file in release.Manifest.Files)
        {
            string target = SafeGamePath(installation.GameRoot, file.Target);
            if (!File.Exists(target)
                || !await _hashService.VerifyFileAsync(
                    target,
                    file.Sha256,
                    _lifetimeCancellation.Token))
            {
                allCurrent = false;
                break;
            }
        }

        _gameStatus.Text =
            $"Játék: Scrap Mechanic {productVersion} · Steam build {installation.BuildId}";
        _modStatus.Text = allCurrent
            ? $"Mod: naprakész ({release.Manifest.Version})"
            : $"Mod: telepítés/frissítés elérhető ({release.Manifest.Version})";
        Log($"Latest release: {release.TagName}; támogatott build: {installation.BuildId}.");
    }

    private async Task InstallLatestAsync()
    {
        EnsureGameIsNotRunning();
        (SteamInstallation installation, ResolvedRelease release, _) =
            await ResolveAndValidateLatestAsync();

        Directory.CreateDirectory(AppDataRoot);
        string temporaryZip = Path.Combine(
            Path.GetTempPath(),
            $"smmm-{Guid.NewGuid():N}-{release.Manifest.PayloadAsset}");
        try
        {
            Log($"Payload letöltése: {release.PayloadDownloadUrl}");
            using HttpResponseMessage response = await _httpClient.GetAsync(
                release.PayloadDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                _lifetimeCancellation.Token);
            response.EnsureSuccessStatusCode();
            await using (Stream source = await response.Content.ReadAsStreamAsync(
                _lifetimeCancellation.Token))
            await using (FileStream destination = File.Create(temporaryZip))
            {
                await source.CopyToAsync(destination, _lifetimeCancellation.Token);
            }

            EnsureGameIsNotRunning();
            InstallResult result = await _installer.InstallAsync(
                installation.GameRoot,
                temporaryZip,
                release.Manifest,
                BackupRoot,
                _lifetimeCancellation.Token);
            await SaveSettingsAsync(installation.GameRoot);
            _modStatus.Text = $"Mod: telepítve ({release.Manifest.Version})";
            Log($"Telepítve: {result.InstalledFileCount} fájl.");
            Log($"Backup: {result.BackupDirectory}");
            if (result.CacheBundleInvalidated)
            {
                Log("A core_data.cbo script-cache backupolva és invalidálva.");
            }
        }
        catch (UnauthorizedAccessException error)
        {
            throw new InvalidOperationException(
                "A Steam játékmappa nem írható. Ellenőrizd a könyvtár tulajdonosát és jogosultságait; ne futtasd a launchert sudo-val.",
                error);
        }
        finally
        {
            if (File.Exists(temporaryZip))
            {
                File.Delete(temporaryZip);
            }
        }
    }

    private async Task RestoreLatestAsync()
    {
        EnsureGameIsNotRunning();
        SteamInstallation installation = ResolveSelectedInstallation(RequireGameRoot());
        string? latestSnapshot = Directory.Exists(BackupRoot)
            ? Directory.GetDirectories(BackupRoot)
                .Where(path => File.Exists(Path.Combine(path, ".snapshot.json")))
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault()
            : null;
        if (latestSnapshot is null)
        {
            throw new InvalidOperationException("Nincs visszaállítható backup snapshot.");
        }

        bool confirmed = await ShowConfirmationAsync(
            "Backup visszaállítása",
            $"Visszaállítod ezt a mentést?\n\n{latestSnapshot}");
        if (!confirmed)
        {
            return;
        }

        bool cacheBundleInvalidated = await _installer.RestoreAsync(
            installation.GameRoot,
            latestSnapshot,
            _lifetimeCancellation.Token);
        _modStatus.Text = "Mod: backup visszaállítva";
        Log($"Backup visszaállítva: {latestSnapshot}");
        if (cacheBundleInvalidated)
        {
            Log("A core_data.cbo script-cache backupolva és invalidálva.");
        }
    }

    private async Task<(SteamInstallation Installation, ResolvedRelease Release, string ProductVersion)>
        ResolveAndValidateLatestAsync()
    {
        SteamInstallation installation = ResolveSelectedInstallation(RequireGameRoot());
        ResolvedRelease release = await _releaseClient.GetLatestReleaseAsync(
            _lifetimeCancellation.Token);
        string executable = Path.Combine(
            installation.GameRoot,
            "Release",
            "ScrapMechanic.exe");
        string productVersion = _versionReader.ReadProductVersion(executable);
        GameInstallValidationResult validation = _gameValidator.Validate(
            installation.GameRoot,
            productVersion,
            installation.BuildId,
            release.Manifest.SupportedBuildIds);
        if (!string.Equals(installation.StateFlags, "4", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A Steam telepítés nincs kész állapotban (StateFlags={installation.StateFlags}).");
        }
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
        }

        _selectedInstallation = installation;
        await SaveSettingsAsync(installation.GameRoot);
        return (installation, release, productVersion);
    }

    private SteamInstallation ResolveSelectedInstallation(string gameRoot)
    {
        string normalizedGameRoot = Path.GetFullPath(gameRoot.Trim());
        var roots = new HashSet<string>(
            _steamRootDiscovery.FindCandidateRoots(),
            StringComparer.Ordinal);
        DirectoryInfo? library = Directory.GetParent(normalizedGameRoot)?.Parent?.Parent;
        if (library is not null)
        {
            roots.Add(library.FullName);
        }

        SteamInstallation? match = roots
            .SelectMany(root => _steamLibraryLocator.FindInstallations(root))
            .FirstOrDefault(installation => string.Equals(
                Path.GetFullPath(installation.GameRoot),
                normalizedGameRoot,
                StringComparison.Ordinal));
        return match ?? throw new InvalidOperationException(
            "A kiválasztott mappához nem található érvényes appmanifest_387990.acf. " +
            "A Scrap Mechanic Steam telepítési gyökérmappáját add meg.");
    }

    private void ShowLocalGameStatus()
    {
        if (_selectedInstallation is null)
        {
            return;
        }

        string executable = Path.Combine(
            _selectedInstallation.GameRoot,
            "Release",
            "ScrapMechanic.exe");
        string version = _versionReader.ReadProductVersion(executable);
        GameInstallValidationResult validation = _gameValidator.Validate(
            _selectedInstallation.GameRoot,
            version,
            _selectedInstallation.BuildId,
            [_selectedInstallation.BuildId]);
        _gameStatus.Text = validation.IsValid
            ? $"Játék: Scrap Mechanic {version} · Steam build {_selectedInstallation.BuildId}"
            : "Játék: " + string.Join(" | ", validation.Errors);
    }

    private void LaunchGame()
    {
        SteamInstallation installation = ResolveSelectedInstallation(RequireGameRoot());
        var platformService = new LinuxGamePlatformService(
            LinuxGamePlatformService.IsFlatpakSteamRoot(installation.LibraryRoot));
        platformService.LaunchGame(_devMode.IsChecked == true);
        Log($"Játékindítás kérése: {installation.GameRoot}");
    }

    private void EnsureGameIsNotRunning()
    {
        SteamInstallation? installation = _selectedInstallation;
        bool flatpak = installation is not null
            && LinuxGamePlatformService.IsFlatpakSteamRoot(installation.LibraryRoot);
        var platformService = new LinuxGamePlatformService(flatpak);
        if (platformService.IsGameRunning())
        {
            throw new InvalidOperationException(
                "A Scrap Mechanic fut. Zárd be a játékot telepítés vagy restore előtt.");
        }
    }

    private static string SafeGamePath(string gameRoot, string relativePath)
    {
        if (!ModManifest.IsSafeRelativePath(relativePath))
        {
            throw new InvalidDataException($"Nem biztonságos manifest target: {relativePath}");
        }

        string root = Path.GetFullPath(gameRoot) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(
            gameRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"A manifest target kilép a game rootból: {relativePath}");
        }
        return target;
    }

    private string RequireGameRoot()
    {
        string value = _gameRoot.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Add meg a Scrap Mechanic útvonalát.");
        }
        return value;
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        SetBusy(true);
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            Log("A művelet megszakítva.");
        }
        catch (Exception error)
        {
            Log("HIBA: " + error.Message);
            await ShowMessageAsync("Scrap Mechanic Mod Manager", error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _progress.IsVisible = busy;
        _browse.IsEnabled = !busy;
        _check.IsEnabled = !busy;
        _install.IsEnabled = !busy;
        _restore.IsEnabled = !busy;
        _launch.IsEnabled = !busy;
    }

    private void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _log.Text = (_log.Text ?? string.Empty) + line;
        _log.CaretIndex = _log.Text.Length;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var closeButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            MinWidth = 90,
        };
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    closeButton,
                },
            },
        };
        closeButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var confirmButton = new Button { Content = "Visszaállítás", MinWidth = 110 };
        var cancelButton = new Button { Content = "Mégse", MinWidth = 90 };
        var dialog = new Window
        {
            Title = title,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        confirmButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 10,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancelButton, confirmButton },
                },
            },
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private static async Task<ManagerSettings?> LoadSettingsAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<ManagerSettings>(
                await File.ReadAllTextAsync(SettingsPath));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task SaveSettingsAsync(string gameRoot)
    {
        Directory.CreateDirectory(AppDataRoot);
        await File.WriteAllTextAsync(
            SettingsPath,
            JsonSerializer.Serialize(
                new ManagerSettings(gameRoot),
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record ManagerSettings(string GameRoot);
}
