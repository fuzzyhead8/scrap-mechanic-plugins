using System.Net.Http.Headers;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Localization;
using ScrapMechanicModManager.Core.Platform;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Settings;
using ScrapMechanicModManager.Core.Steam;
using ScrapMechanicModManager.Core.Updates;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager.Desktop;

public sealed partial class MainWindow : Window
{
    private const string RepositoryOwner = "fuzzyhead8";
    private const string RepositoryName = "scrap-mechanic-plugins";

    private readonly TextBlock _header;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _gameRootLabel;
    private readonly TextBlock _languageLabel;
    private readonly ComboBox _languageSelector;
    private readonly TextBlock _footer;
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
    private readonly AppLocalizer _localizer = new();
    private readonly ManagerSettingsStore _settingsStore = new(SettingsPath);
    private readonly List<LocalizedMessage> _logMessages = [];
    private readonly List<DateTime> _logTimestamps = [];
    private ManagerSettings _settings = ManagerSettings.Default;
    private LocalizedMessage _gameStatusMessage = new(TextKey.GameStatusNotChecked);
    private LocalizedMessage _modStatusMessage = new(TextKey.ModStatusNotChecked);
    private SteamInstallation? _selectedInstallation;
    private bool _applyingLanguage;

    private static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScrapMechanicModManager");
    private static string BackupRoot => Path.Combine(AppDataRoot, "backups");
    private static string SettingsPath => Path.Combine(AppDataRoot, "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        _header = this.FindControl<TextBlock>("HeaderText")!;
        _subtitle = this.FindControl<TextBlock>("SubtitleText")!;
        _gameRootLabel = this.FindControl<TextBlock>("GameRootLabelText")!;
        _languageLabel = this.FindControl<TextBlock>("LanguageLabelText")!;
        _languageSelector = this.FindControl<ComboBox>("LanguageComboBox")!;
        _footer = this.FindControl<TextBlock>("FooterText")!;
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

        _settings = _settingsStore.Load();
        _localizer.Language = _settings.Language;
        ApplyLocalizedText();

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

    private async void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingLanguage) return;
        await RunBusyAsync(OnLanguageChangedAsync);
    }

    private async Task AutoDetectAsync()
    {
        if (!string.IsNullOrWhiteSpace(_settings.GameRoot)
            && Directory.Exists(_settings.GameRoot))
        {
            try
            {
                _selectedInstallation = ResolveSelectedInstallation(_settings.GameRoot);
                _gameRoot.Text = _settings.GameRoot;
                ShowLocalGameStatus();
                Log(TextKey.LogSavedGameRootLoaded);
                return;
            }
            catch (UserFacingException)
            {
                Log(TextKey.LogSavedGameRootInvalid, _settings.GameRoot);
            }
        }
        else if (!string.IsNullOrWhiteSpace(_settings.GameRoot))
        {
            Log(TextKey.LogSavedGameRootInvalid, _settings.GameRoot);
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
            await SaveCurrentSettingsAsync(installation.GameRoot);
            Log(TextKey.LogAutoDetectedSteamProtonInstall, installation.GameRoot);
            return;
        }

        SetGameStatus(TextKey.GameStatusNotFoundAutomatically);
        Log(TextKey.LogAutoDetectFailedUseBrowse);
    }

    private async Task BrowseForGameRootAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = _localizer.Get(TextKey.DialogSelectGameRootTitle),
                AllowMultiple = false,
            });
        string? selectedPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        _gameRoot.Text = selectedPath;
        _selectedInstallation = null;
        SetGameStatus(TextKey.GameStatusPathProvidedNeedsCheck);
        SetModStatus(TextKey.ModStatusNotChecked);
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

        SetGameStatus(TextKey.GameStatusReady, productVersion, installation.BuildId);
        SetModStatus(
            allCurrent ? TextKey.ModStatusUpToDate : TextKey.ModStatusUpdateAvailable,
            release.Manifest.Version);
        Log(TextKey.LogLatestRelease, release.TagName, installation.BuildId);
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
            Log(TextKey.LogPayloadDownload, release.PayloadDownloadUrl);
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
            await SaveCurrentSettingsAsync(installation.GameRoot);
            SetModStatus(TextKey.ModStatusInstalled, release.Manifest.Version);
            Log(TextKey.LogInstalledFiles, result.InstalledFileCount);
            Log(TextKey.LogBackupDirectory, result.BackupDirectory);
            if (result.CacheBundleInvalidated)
            {
                Log(TextKey.LogScriptCacheInvalidated);
            }
        }
        catch (UnauthorizedAccessException error)
        {
            throw new UserFacingException(
                TextKey.ErrorSteamGameDirectoryNotWritable,
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
            throw new UserFacingException(TextKey.ErrorNoBackupSnapshot);
        }

        bool confirmed = await ShowConfirmationAsync(
            TextKey.DialogRestoreBackupTitle,
            TextKey.DialogRestoreBackupMessage,
            TextKey.DialogButtonRestore,
            latestSnapshot);
        if (!confirmed)
        {
            return;
        }

        bool cacheBundleInvalidated = await _installer.RestoreAsync(
            installation.GameRoot,
            latestSnapshot,
            _lifetimeCancellation.Token);
        SetModStatus(TextKey.ModStatusBackupRestored);
        Log(TextKey.LogBackupRestored, latestSnapshot);
        if (cacheBundleInvalidated)
        {
            Log(TextKey.LogScriptCacheInvalidated);
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
            throw new UserFacingException(
                TextKey.ErrorSteamInstallNotReady,
                installation.StateFlags);
        }
        if (!validation.IsValid)
        {
            throw new UserFacingException(TextKey.ErrorGameValidationFailed);
        }

        _selectedInstallation = installation;
        await SaveCurrentSettingsAsync(installation.GameRoot);
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
        return match ?? throw new UserFacingException(
            TextKey.ErrorInvalidAppManifestForSelectedFolder);
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
        if (validation.IsValid)
        {
            SetGameStatus(TextKey.GameStatusReady, version, _selectedInstallation.BuildId);
        }
        else
        {
            SetGameStatus(TextKey.GameStatusInvalid);
        }
    }

    private void LaunchGame()
    {
        SteamInstallation installation = ResolveSelectedInstallation(RequireGameRoot());
        var platformService = new LinuxGamePlatformService(
            LinuxGamePlatformService.IsFlatpakSteamRoot(installation.LibraryRoot));
        platformService.LaunchGame(_devMode.IsChecked == true);
        Log(TextKey.LogLaunchRequested, installation.GameRoot);
    }

    private void EnsureGameIsNotRunning()
    {
        SteamInstallation? installation = _selectedInstallation;
        bool flatpak = installation is not null
            && LinuxGamePlatformService.IsFlatpakSteamRoot(installation.LibraryRoot);
        var platformService = new LinuxGamePlatformService(flatpak);
        if (platformService.IsGameRunning())
        {
            throw new UserFacingException(TextKey.ErrorGameRunning);
        }
    }

    private static string SafeGamePath(string gameRoot, string relativePath)
    {
        if (!ModManifest.IsSafeRelativePath(relativePath))
        {
            throw new UserFacingException(TextKey.ErrorUnsafeManifestTarget, relativePath);
        }

        string root = Path.GetFullPath(gameRoot) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(
            gameRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.Ordinal))
        {
            throw new UserFacingException(
                TextKey.ErrorManifestTargetEscapesGameRoot,
                relativePath);
        }
        return target;
    }

    private string RequireGameRoot()
    {
        string value = _gameRoot.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UserFacingException(TextKey.ErrorMissingGameRoot);
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
            Log(TextKey.LogOperationCanceled);
        }
        catch (Exception error)
        {
            string message = GetUserFacingError(error);
            Log(TextKey.LogError, message);
            await ShowMessageAsync(TextKey.DialogErrorTitle, message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string GetUserFacingError(Exception error)
    {
        if (error is UserFacingException userFacing)
        {
            return userFacing.UserMessage.Render(_localizer);
        }
        if (error is UnauthorizedAccessException)
        {
            return _localizer.Get(TextKey.ErrorPermissionDenied);
        }
        if (error is HttpRequestException)
        {
            return _localizer.Get(TextKey.ErrorLatestReleaseUnavailable);
        }
        return _localizer.Get(TextKey.ErrorOperationFailed);
    }

    private void SetBusy(bool busy)
    {
        _progress.IsVisible = busy;
        _browse.IsEnabled = !busy;
        _check.IsEnabled = !busy;
        _install.IsEnabled = !busy;
        _restore.IsEnabled = !busy;
        _launch.IsEnabled = !busy;
        _languageSelector.IsEnabled = !busy;
    }

    private async Task OnLanguageChangedAsync()
    {
        if (_applyingLanguage || _languageSelector.SelectedIndex < 0)
        {
            return;
        }

        AppLanguage language = _languageSelector.SelectedIndex == 1
            ? AppLanguage.English
            : AppLanguage.Hungarian;
        if (_localizer.Language == language)
        {
            return;
        }

        _localizer.Language = language;
        ApplyLocalizedText();
        string languageName = _localizer.Get(
            language == AppLanguage.English
                ? TextKey.LanguageEnglish
                : TextKey.LanguageHungarian);
        Log(TextKey.LogLanguageChanged, languageName);
        await SaveCurrentSettingsAsync();
    }

    private void ApplyLocalizedText()
    {
        Title = _localizer.Get(TextKey.AppTitle);
        _header.Text = _localizer.Get(TextKey.AppHeader);
        _subtitle.Text = _localizer.Get(TextKey.AppSubtitleLinux);
        _gameRootLabel.Text = _localizer.Get(TextKey.GameRootLabel);
        _gameRoot.Watermark = _localizer.Get(TextKey.GameRootWatermarkLinux);
        _browse.Content = _localizer.Get(TextKey.ButtonBrowse);
        _check.Content = _localizer.Get(TextKey.ButtonCheck);
        _install.Content = _localizer.Get(TextKey.ButtonInstallUpdate);
        _restore.Content = _localizer.Get(TextKey.ButtonRestore);
        _launch.Content = _localizer.Get(TextKey.ButtonLaunchGame);
        _devMode.Content = _localizer.Get(TextKey.CheckBoxDevMode);
        _languageLabel.Text = _localizer.Get(TextKey.LanguageLabel);
        _footer.Text = _localizer.Get(TextKey.LinuxPreviewFooter);

        _applyingLanguage = true;
        try
        {
            _languageSelector.ItemsSource = new[]
            {
                _localizer.Get(TextKey.LanguageHungarian),
                _localizer.Get(TextKey.LanguageEnglish),
            };
            _languageSelector.SelectedIndex = _localizer.Language == AppLanguage.English ? 1 : 0;
        }
        finally
        {
            _applyingLanguage = false;
        }

        RenderLocalizedState();
    }

    private void RenderLocalizedState()
    {
        _gameStatus.Text = _gameStatusMessage.Render(_localizer);
        _modStatus.Text = _modStatusMessage.Render(_localizer);
        RenderLog();
    }

    private void SetGameStatus(TextKey key, params object?[] arguments)
    {
        _gameStatusMessage = new LocalizedMessage(key, arguments);
        _gameStatus.Text = _gameStatusMessage.Render(_localizer);
    }

    private void SetModStatus(TextKey key, params object?[] arguments)
    {
        _modStatusMessage = new LocalizedMessage(key, arguments);
        _modStatus.Text = _modStatusMessage.Render(_localizer);
    }

    private void Log(TextKey key, params object?[] arguments)
    {
        _logMessages.Add(new LocalizedMessage(key, arguments));
        _logTimestamps.Add(DateTime.Now);
        RenderLog();
    }

    private void RenderLog()
    {
        var text = new StringBuilder();
        for (int index = 0; index < _logMessages.Count; index++)
        {
            text.Append('[')
                .Append(_logTimestamps[index].ToString("HH:mm:ss"))
                .Append("] ")
                .AppendLine(_logMessages[index].Render(_localizer));
        }
        _log.Text = text.ToString();
        _log.CaretIndex = _log.Text.Length;
    }

    private async Task SaveCurrentSettingsAsync(string? gameRoot = null)
    {
        string? currentRoot = gameRoot;
        if (string.IsNullOrWhiteSpace(currentRoot))
        {
            currentRoot = string.IsNullOrWhiteSpace(_gameRoot.Text)
                ? _settings.GameRoot
                : _gameRoot.Text.Trim();
        }
        _settings = new ManagerSettings(currentRoot, _localizer.Language);
        await _settingsStore.SaveAsync(_settings, _lifetimeCancellation.Token);
    }

    private async Task ShowMessageAsync(TextKey titleKey, string message)
    {
        var closeButton = new Button
        {
            Content = _localizer.Get(TextKey.DialogButtonOk),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            MinWidth = 90,
        };
        var dialog = new Window
        {
            Title = _localizer.Get(titleKey),
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

    private async Task<bool> ShowConfirmationAsync(
        TextKey titleKey,
        TextKey messageKey,
        TextKey confirmButtonKey,
        params object?[] arguments)
    {
        var confirmButton = new Button
        {
            Content = _localizer.Get(confirmButtonKey),
            MinWidth = 110,
        };
        var cancelButton = new Button
        {
            Content = _localizer.Get(TextKey.DialogButtonCancel),
            MinWidth = 90,
        };
        var dialog = new Window
        {
            Title = _localizer.Get(titleKey),
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
                    Text = _localizer.Get(messageKey, arguments),
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

    private sealed class UserFacingException(TextKey key, params object?[] arguments) : Exception
    {
        public LocalizedMessage UserMessage { get; } = new(key, arguments);
    }
}
