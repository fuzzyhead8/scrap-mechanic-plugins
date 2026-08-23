using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ScrapMechanicModManager.Core.History;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Localization;
using ScrapMechanicModManager.Core.Platform;
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
    private readonly TextBlock _modulesLabel;
    private readonly CheckBox _robotLootModule;
    private readonly CheckBox _beehiveAutomationModule;
    private readonly CheckBox _freezerAutomationModule;
    private readonly TextBlock _robotLootStatus;
    private readonly TextBlock _beehiveAutomationStatus;
    private readonly TextBlock _freezerAutomationStatus;
    private readonly TextBlock _robotLootBackupStatus;
    private readonly TextBlock _beehiveAutomationBackupStatus;
    private readonly TextBlock _freezerAutomationBackupStatus;
    private readonly TextBlock _gameStatus;
    private readonly TextBlock _modStatus;
    private readonly ProgressBar _progress;
    private readonly TextBox _log;

    private readonly ISteamRootDiscovery _steamRootDiscovery =
        new LinuxSteamRootDiscovery();
    private readonly SteamLibraryLocator _steamLibraryLocator = new();
    private readonly GameInstallValidator _gameValidator = new();
    private readonly ExecutableVersionReader _versionReader = new();
    private readonly ModuleStatusEvaluator _moduleStatusEvaluator = new();
    private readonly ModuleInstallCoordinator _moduleInstaller = new();
    private readonly BackupSnapshotCatalog _backupCatalog = new();
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly GitHubReleaseClient _releaseClient;
    private readonly AppLocalizer _localizer = new();
    private readonly ManagerSettingsStore _settingsStore = new(SettingsPath);
    private readonly JsonLinesOperationJournal _operationJournal = new(OperationHistoryPath);
    private readonly List<OperationRecord> _operationHistory = [];
    private ManagerSettings _settings = ManagerSettings.Default;
    private LocalizedMessage _gameStatusMessage = new(TextKey.GameStatusNotChecked);
    private LocalizedMessage _modStatusMessage = new(TextKey.ModStatusNotChecked);
    private readonly Dictionary<string, LocalizedMessage> _moduleStatusMessages = new(
        StringComparer.OrdinalIgnoreCase)
    {
        [BuiltInModuleIds.RobotLoot] = new(TextKey.ModuleStatusNotChecked),
        [BuiltInModuleIds.BeehiveAutomation] = new(TextKey.ModuleStatusNotChecked),
        [BuiltInModuleIds.FreezerAutomation] = new(TextKey.ModuleStatusNotChecked),
    };
    private readonly Dictionary<string, ModuleBackupStatus> _moduleBackupStatuses = new(
        StringComparer.OrdinalIgnoreCase)
    {
        [BuiltInModuleIds.RobotLoot] = EmptyBackupStatus(BuiltInModuleIds.RobotLoot),
        [BuiltInModuleIds.BeehiveAutomation] = EmptyBackupStatus(BuiltInModuleIds.BeehiveAutomation),
        [BuiltInModuleIds.FreezerAutomation] = EmptyBackupStatus(BuiltInModuleIds.FreezerAutomation),
    };
    private SteamInstallation? _selectedInstallation;
    private string? _activeOperationId;
    private bool _applyingLanguage;

    private static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScrapMechanicModManager");
    private static string BackupRoot => Path.Combine(AppDataRoot, "backups");
    private static string SettingsPath => Path.Combine(AppDataRoot, "settings.json");
    private static string OperationHistoryPath => Path.Combine(
        AppDataRoot,
        "logs", "operations.jsonl");

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
        _modulesLabel = this.FindControl<TextBlock>("ModulesLabelText")!;
        _robotLootModule = this.FindControl<CheckBox>("RobotLootModuleCheckBox")!;
        _beehiveAutomationModule =
            this.FindControl<CheckBox>("BeehiveAutomationModuleCheckBox")!;
        _freezerAutomationModule =
            this.FindControl<CheckBox>("FreezerAutomationModuleCheckBox")!;
        _robotLootStatus = this.FindControl<TextBlock>("RobotLootModuleStatusText")!;
        _beehiveAutomationStatus =
            this.FindControl<TextBlock>("BeehiveAutomationModuleStatusText")!;
        _freezerAutomationStatus =
            this.FindControl<TextBlock>("FreezerAutomationModuleStatusText")!;
        _robotLootBackupStatus =
            this.FindControl<TextBlock>("RobotLootBackupStatusText")!;
        _beehiveAutomationBackupStatus =
            this.FindControl<TextBlock>("BeehiveAutomationBackupStatusText")!;
        _freezerAutomationBackupStatus =
            this.FindControl<TextBlock>("FreezerAutomationBackupStatusText")!;
        _gameStatus = this.FindControl<TextBlock>("GameStatusText")!;
        _modStatus = this.FindControl<TextBlock>("ModStatusText")!;
        _progress = this.FindControl<ProgressBar>("ProgressBar")!;
        _log = this.FindControl<TextBox>("LogTextBox")!;

        _settings = _settingsStore.Load();
        _localizer.Language = _settings.Language;
        ApplySelectedModuleSettings();
        ApplyLocalizedText();

        string informationalVersion = typeof(MainWindow).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.2.0-preview.9";
        string appVersion = informationalVersion.Split('+', 2)[0];
        string? releaseTag = ReleaseChannel.GetReleaseTag(appVersion);
        _releaseClient = new GitHubReleaseClient(
            _httpClient,
            RepositoryOwner,
            RepositoryName,
            releaseTag);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ScrapMechanicModManager-Linux", appVersion));

        Opened += async (_, _) => await RunBusyAsync(InitializeAsync);
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
        await RunBusyAsync(RestoreSelectedModulesAsync);
    }

    private async void OnModuleSelectionChanged(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(() => SaveCurrentSettingsAsync());
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

    private async Task InitializeAsync()
    {
        await LoadOperationHistoryAsync();
        await RefreshBackupStatusesAsync();
        await AutoDetectAsync();
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
        foreach (string modId in BuiltInModuleIds.All)
        {
            SetModuleStatus(modId, TextKey.ModuleStatusNotChecked);
        }
        await RefreshBackupStatusesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        (SteamInstallation installation, ResolvedModuleRelease release, string productVersion) =
            await ResolveAndValidateLatestModulesAsync();

        foreach (string modId in BuiltInModuleIds.All)
        {
            SetModuleStatus(modId, TextKey.ModuleStatusUnavailable);
        }

        bool allCurrent = release.Modules.Count > 0;
        foreach (ResolvedModule module in release.Modules)
        {
            ModuleInstallState state = await _moduleStatusEvaluator.EvaluateAsync(
                installation.GameRoot,
                BackupRoot,
                module.Manifest,
                _lifetimeCancellation.Token);
            TextKey statusKey = state switch
            {
                ModuleInstallState.UpToDate => TextKey.ModuleStatusUpToDate,
                ModuleInstallState.UpdateAvailable => TextKey.ModuleStatusUpdateAvailable,
                _ => TextKey.ModuleStatusNotInstalled,
            };
            SetModuleStatus(module.ModId, statusKey, module.Manifest.Version);
            allCurrent &= state == ModuleInstallState.UpToDate;
        }

        SetGameStatus(TextKey.GameStatusReady, productVersion, installation.BuildId);
        SetModStatus(
            allCurrent ? TextKey.ModStatusUpToDate : TextKey.ModStatusUpdateAvailable,
            release.TagName);
        await RefreshBackupStatusesAsync();
        Log(TextKey.LogLatestRelease, release.TagName, installation.BuildId);
    }

    private async Task InstallLatestAsync()
    {
        IReadOnlyList<string> selectedModuleIds = GetSelectedModuleIds();
        if (selectedModuleIds.Count == 0)
        {
            throw new UserFacingException(TextKey.ErrorNoModulesSelected);
        }
        EnsureGameIsNotRunning();
        (SteamInstallation installation, ResolvedModuleRelease release, _) =
            await ResolveAndValidateLatestModulesAsync(selectedModuleIds);

        IReadOnlyList<ResolvedModule> selectedModules = ModuleSelection.FilterAvailable(
            release.Modules,
            selectedModuleIds);
        var temporaryZips = new List<string>();
        var installRequests = new List<ModuleInstallRequest>();
        try
        {
            foreach (ResolvedModule module in selectedModules)
            {
                string temporaryZip = Path.Combine(
                    Path.GetTempPath(),
                    $"smmm-{Guid.NewGuid():N}-{module.Manifest.PayloadAsset}");
                temporaryZips.Add(temporaryZip);
                LogDetailed(
                    TextKey.LogModulePayloadDownload,
                    OperationSeverity.Information,
                    [module.ModId],
                    null,
                    null,
                    GetModuleDisplayName(module.ModId),
                    module.PayloadDownloadUrl);
                using HttpResponseMessage response = await _httpClient.GetAsync(
                    module.PayloadDownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    _lifetimeCancellation.Token);
                response.EnsureSuccessStatusCode();
                await using (Stream source = await response.Content.ReadAsStreamAsync(
                    _lifetimeCancellation.Token))
                await using (FileStream destination = File.Create(temporaryZip))
                {
                    await source.CopyToAsync(destination, _lifetimeCancellation.Token);
                }
                installRequests.Add(new ModuleInstallRequest(
                    temporaryZip,
                    module.Manifest));
            }

            EnsureGameIsNotRunning();
            InstallResult result = await _moduleInstaller.InstallAsync(
                installation.GameRoot,
                installRequests,
                BackupRoot,
                _lifetimeCancellation.Token);
            await SaveCurrentSettingsAsync(installation.GameRoot);
            foreach (ResolvedModule module in selectedModules)
            {
                SetModuleStatus(
                    module.ModId,
                    TextKey.ModuleStatusInstalled,
                    module.Manifest.Version);
            }
            SetModStatus(TextKey.ModStatusInstalled, release.TagName);
            LogDetailed(
                TextKey.LogSelectedModulesInstalled,
                OperationSeverity.Information,
                selectedModuleIds,
                result.BackupDirectory,
                null,
                string.Join(", ", selectedModules.Select(module =>
                    GetModuleDisplayName(module.ModId))),
                result.InstalledFileCount);
            LogDetailed(
                TextKey.LogBackupDirectory,
                OperationSeverity.Information,
                selectedModuleIds,
                result.BackupDirectory,
                null,
                result.BackupDirectory);
            await RefreshBackupStatusesAsync();
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
            foreach (string temporaryZip in temporaryZips)
            {
                if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
            }
        }
    }

    private async Task RestoreSelectedModulesAsync()
    {
        IReadOnlyList<string> selectedModuleIds = GetSelectedModuleIds();
        if (selectedModuleIds.Count == 0)
        {
            throw new UserFacingException(TextKey.ErrorNoModulesSelected);
        }
        EnsureGameIsNotRunning();
        SteamInstallation installation = ResolveSelectedInstallation(RequireGameRoot());
        var snapshots = new List<(string ModId, string Directory)>();
        foreach (string modId in selectedModuleIds)
        {
            string? snapshot = _moduleInstaller.FindLatestSnapshotForModule(
                BackupRoot,
                modId);
            if (snapshot is null)
            {
                throw new UserFacingException(
                    TextKey.ErrorNoSelectedModuleBackup,
                    GetModuleDisplayName(modId));
            }
            snapshots.Add((modId, snapshot));
        }

        string moduleList = string.Join(
            Environment.NewLine,
            snapshots.Select(snapshot => "• " + GetModuleDisplayName(snapshot.ModId)));
        bool confirmed = await ShowConfirmationAsync(
            TextKey.DialogRestoreSelectedModulesTitle,
            TextKey.DialogRestoreSelectedModulesMessage,
            TextKey.DialogButtonRestore,
            moduleList);
        if (!confirmed)
        {
            return;
        }

        try
        {
            bool cacheBundleInvalidated = false;
            foreach ((string modId, string snapshotDirectory) in snapshots)
            {
                cacheBundleInvalidated |= await _moduleInstaller.RestoreModuleAsync(
                    installation.GameRoot,
                    snapshotDirectory,
                    modId,
                    _lifetimeCancellation.Token);
                SetModuleStatus(modId, TextKey.ModuleStatusRestored);
                LogDetailed(
                    TextKey.LogModuleRestored,
                    OperationSeverity.Information,
                    [modId],
                    snapshotDirectory,
                    null,
                    GetModuleDisplayName(modId),
                    snapshotDirectory);
            }
            SetModStatus(TextKey.ModStatusBackupRestored);
            await RefreshBackupStatusesAsync();
            if (cacheBundleInvalidated)
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
    }

    private string ReadProductVersionForUser(string executable)
    {
        try
        {
            return _versionReader.ReadProductVersion(executable);
        }
        catch (FileNotFoundException)
        {
            throw new UserFacingException(TextKey.ErrorGameExecutableMissing, executable);
        }
        catch (InvalidDataException)
        {
            throw new UserFacingException(TextKey.ErrorGameVersionUnavailable, executable);
        }
    }

    private async Task<(
        SteamInstallation Installation,
        ResolvedModuleRelease Release,
        string ProductVersion)> ResolveAndValidateLatestModulesAsync(
            IReadOnlyCollection<string>? requiredModuleIds = null)
    {
        SteamInstallation installation = ResolveSelectedInstallation(RequireGameRoot());
        ResolvedModuleRelease release = await _releaseClient.GetLatestModuleReleaseAsync(
            _lifetimeCancellation.Token);
        IReadOnlyList<ResolvedModule> validationModules = release.Modules;
        if (requiredModuleIds is { Count: > 0 })
        {
            var availableIds = new HashSet<string>(
                release.Modules.Select(module => module.ModId),
                StringComparer.OrdinalIgnoreCase);
            string[] unavailableIds = requiredModuleIds
                .Where(modId => !availableIds.Contains(modId))
                .ToArray();
            if (unavailableIds.Length > 0)
            {
                throw new UserFacingException(
                    TextKey.ErrorSelectedModulesUnavailable,
                    string.Join(", ", unavailableIds.Select(GetModuleDisplayName)));
            }

            validationModules = ModuleSelection.FilterAvailable(
                release.Modules,
                requiredModuleIds);
        }

        string[] commonSupportedBuildIds = GetCommonSupportedBuildIds(validationModules);
        string executable = Path.Combine(
            installation.GameRoot,
            "Release",
            "ScrapMechanic.exe");
        string productVersion = ReadProductVersionForUser(executable);
        GameInstallValidationResult validation = _gameValidator.Validate(
            installation.GameRoot,
            productVersion,
            installation.BuildId,
            commonSupportedBuildIds);
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

    private static string[] GetCommonSupportedBuildIds(
        IReadOnlyList<ResolvedModule> modules)
    {
        if (modules.Count == 0) return [];

        var commonBuildIds = new HashSet<string>(
            modules[0].Manifest.SupportedBuildIds,
            StringComparer.Ordinal);
        foreach (ResolvedModule module in modules.Skip(1))
        {
            commonBuildIds.IntersectWith(module.Manifest.SupportedBuildIds);
        }
        return commonBuildIds.ToArray();
    }

    private SteamInstallation ResolveSelectedInstallation(string gameRoot)
    {
        string normalizedGameRoot = SteamPathIdentity.Normalize(gameRoot);
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
            .FirstOrDefault(installation => SteamPathIdentity.AreEquivalent(
                installation.GameRoot,
                normalizedGameRoot));
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
        string version = ReadProductVersionForUser(executable);
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
        string? previousOperationId = _activeOperationId;
        _activeOperationId = Guid.NewGuid().ToString("N");
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            LogDetailed(
                TextKey.LogOperationCanceled,
                OperationSeverity.Warning,
                GetSelectedModuleIds(),
                null,
                null);
        }
        catch (Exception error)
        {
            string message = GetUserFacingError(error);
            LogDetailed(
                TextKey.LogError,
                OperationSeverity.Error,
                GetSelectedModuleIds(),
                null,
                error,
                message);
            await ShowMessageAsync(TextKey.DialogErrorTitle, message);
        }
        finally
        {
            _activeOperationId = previousOperationId;
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
        return _localizer.Get(TextKey.ErrorOperationFailed, error.GetType().Name);
    }

    private void SetBusy(bool busy)
    {
        _progress.IsVisible = busy;
        _browse.IsEnabled = !busy;
        _check.IsEnabled = !busy;
        _install.IsEnabled = !busy;
        _restore.IsEnabled = !busy;
        _launch.IsEnabled = !busy;
        _robotLootModule.IsEnabled = !busy;
        _beehiveAutomationModule.IsEnabled = !busy;
        _freezerAutomationModule.IsEnabled = !busy;
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
        _restore.Content = _localizer.Get(TextKey.ButtonRestoreSelectedModules);
        _launch.Content = _localizer.Get(TextKey.ButtonLaunchGame);
        _devMode.Content = _localizer.Get(TextKey.CheckBoxDevMode);
        _modulesLabel.Text = _localizer.Get(TextKey.ModulesLabel);
        _robotLootModule.Content = _localizer.Get(TextKey.ModuleRobotLoot);
        _beehiveAutomationModule.Content = _localizer.Get(TextKey.ModuleBeehiveAutomation);
        _freezerAutomationModule.Content = _localizer.Get(TextKey.ModuleFreezerAutomation);
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
        RenderModuleStatuses();
        RenderBackupStatuses();
        RenderLog();
    }

    private void ApplySelectedModuleSettings()
    {
        _robotLootModule.IsChecked = _settings.SelectedModuleIds.Contains(
            BuiltInModuleIds.RobotLoot,
            StringComparer.OrdinalIgnoreCase);
        _beehiveAutomationModule.IsChecked = _settings.SelectedModuleIds.Contains(
            BuiltInModuleIds.BeehiveAutomation,
            StringComparer.OrdinalIgnoreCase);
        _freezerAutomationModule.IsChecked = _settings.SelectedModuleIds.Contains(
            BuiltInModuleIds.FreezerAutomation,
            StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> GetSelectedModuleIds()
    {
        var selected = new List<string>(3);
        if (_robotLootModule.IsChecked == true)
        {
            selected.Add(BuiltInModuleIds.RobotLoot);
        }
        if (_beehiveAutomationModule.IsChecked == true)
        {
            selected.Add(BuiltInModuleIds.BeehiveAutomation);
        }
        if (_freezerAutomationModule.IsChecked == true)
        {
            selected.Add(BuiltInModuleIds.FreezerAutomation);
        }
        return selected;
    }

    private string GetModuleDisplayName(string modId) => modId switch
    {
        BuiltInModuleIds.RobotLoot => _localizer.Get(TextKey.ModuleRobotLoot),
        BuiltInModuleIds.BeehiveAutomation =>
            _localizer.Get(TextKey.ModuleBeehiveAutomation),
        BuiltInModuleIds.FreezerAutomation =>
            _localizer.Get(TextKey.ModuleFreezerAutomation),
        _ => modId,
    };

    private void SetModuleStatus(
        string modId,
        TextKey key,
        params object?[] arguments)
    {
        if (!_moduleStatusMessages.ContainsKey(modId)) return;

        _moduleStatusMessages[modId] = new LocalizedMessage(key, arguments);
        RenderModuleStatuses();
    }

    private void RenderModuleStatuses()
    {
        _robotLootStatus.Text = _moduleStatusMessages[BuiltInModuleIds.RobotLoot]
            .Render(_localizer);
        _beehiveAutomationStatus.Text =
            _moduleStatusMessages[BuiltInModuleIds.BeehiveAutomation]
                .Render(_localizer);
        _freezerAutomationStatus.Text =
            _moduleStatusMessages[BuiltInModuleIds.FreezerAutomation]
                .Render(_localizer);
    }

    private async Task RefreshBackupStatusesAsync()
    {
        Dictionary<string, ModuleBackupStatus> statuses = await Task.Run(
            () => BuiltInModuleIds.All.ToDictionary(
                modId => modId,
                modId => _backupCatalog.GetModuleStatus(BackupRoot, modId),
                StringComparer.OrdinalIgnoreCase),
            _lifetimeCancellation.Token);
        foreach ((string modId, ModuleBackupStatus status) in statuses)
        {
            _moduleBackupStatuses[modId] = status;
        }
        RenderBackupStatuses();
    }

    private void RenderBackupStatuses()
    {
        RenderBackupStatus(
            _robotLootBackupStatus,
            _moduleBackupStatuses[BuiltInModuleIds.RobotLoot]);
        RenderBackupStatus(
            _beehiveAutomationBackupStatus,
            _moduleBackupStatuses[BuiltInModuleIds.BeehiveAutomation]);
        RenderBackupStatus(
            _freezerAutomationBackupStatus,
            _moduleBackupStatuses[BuiltInModuleIds.FreezerAutomation]);
    }

    private void RenderBackupStatus(TextBlock textBlock, ModuleBackupStatus status)
    {
        TextKey key = status.State switch
        {
            BackupSnapshotState.Available when status.CreatedAtUtc is not null =>
                TextKey.ModuleBackupAvailable,
            BackupSnapshotState.Corrupt => TextKey.ModuleBackupCorrupt,
            BackupSnapshotState.Legacy => TextKey.ModuleBackupLegacy,
            _ => TextKey.ModuleBackupMissing,
        };
        if (key == TextKey.ModuleBackupAvailable)
        {
            string localTimestamp = _localizer.FormatShortLocalDateTime(
                status.CreatedAtUtc!.Value);
            textBlock.Text = _localizer.Get(key, localTimestamp);
            return;
        }
        textBlock.Text = _localizer.Get(key);
    }

    private static ModuleBackupStatus EmptyBackupStatus(string modId) => new(
        modId,
        BackupSnapshotState.None,
        null,
        null,
        null);

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

    private async Task LoadOperationHistoryAsync()
    {
        (bool loaded, IReadOnlyList<OperationRecord> records, string? error) =
            await Task.Run(
                () =>
                {
                    bool success = _operationJournal.TryReadRecent(
                        out IReadOnlyList<OperationRecord> recent,
                        out string? readError);
                    return (success, recent, readError);
                },
                _lifetimeCancellation.Token);
        _operationHistory.Clear();
        _operationHistory.AddRange(records);
        TrimOperationHistory();
        if (!loaded)
        {
            AddOperationRecord(
                CreateOperationRecord(
                    TextKey.LogHistoryReadWarning,
                    OperationSeverity.Warning,
                    [],
                    null,
                    null,
                    [error ?? "Unknown operation history read error"]),
                persist: false);
            return;
        }
        RenderLog();
    }

    private void Log(TextKey key, params object?[] arguments) =>
        LogDetailed(
            key,
            OperationSeverity.Information,
            [],
            null,
            null,
            arguments);

    private void LogDetailed(
        TextKey key,
        OperationSeverity severity,
        IReadOnlyList<string> moduleIds,
        string? backupDirectory,
        Exception? technicalError,
        params object?[] arguments)
    {
        AddOperationRecord(
            CreateOperationRecord(
                key,
                severity,
                moduleIds,
                backupDirectory,
                technicalError,
                arguments),
            persist: true);
    }

    private OperationRecord CreateOperationRecord(
        TextKey key,
        OperationSeverity severity,
        IReadOnlyList<string> moduleIds,
        string? backupDirectory,
        Exception? technicalError,
        IReadOnlyList<object?> arguments) => new()
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Severity = severity,
            MessageKey = key.ToString(),
            Arguments = arguments
                .Select(argument => Convert.ToString(
                    argument,
                    CultureInfo.InvariantCulture) ?? string.Empty)
                .ToArray(),
            ModuleIds = moduleIds
                .Where(modId => !string.IsNullOrWhiteSpace(modId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            OperationId = _activeOperationId ?? Guid.NewGuid().ToString("N"),
            BackupDirectory = string.IsNullOrWhiteSpace(backupDirectory)
                ? null
                : backupDirectory,
            TechnicalErrorType = technicalError?.GetType().FullName,
            TechnicalDetail = technicalError?.ToString(),
        };

    private void AddOperationRecord(OperationRecord record, bool persist)
    {
        _operationHistory.Add(record);
        TrimOperationHistory();
        RenderLog();
        if (!persist || _operationJournal.TryAppend(record, out string? error))
        {
            return;
        }

        AddOperationRecord(
            CreateOperationRecord(
                TextKey.LogHistoryWriteWarning,
                OperationSeverity.Warning,
                [],
                null,
                null,
                [error ?? "Unknown operation history write error"]),
            persist: false);
    }

    private void TrimOperationHistory()
    {
        int overflow = _operationHistory.Count
            - OperationJournalOptions.Default.MaxUiEntries;
        if (overflow > 0)
        {
            _operationHistory.RemoveRange(0, overflow);
        }
    }

    private void RenderLog()
    {
        var text = new StringBuilder();
        foreach (OperationRecord record in _operationHistory)
        {
            LocalizedMessage message = LocalizedMessage.FromPersisted(
                record.MessageKey,
                record.Arguments,
                record.FallbackText);
            text.Append('[')
                .Append(record.TimestampUtc.ToLocalTime().ToString(
                    "HH:mm:ss",
                    CultureInfo.InvariantCulture))
                .Append("] ")
                .AppendLine(message.Render(_localizer));
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
        _settings = new ManagerSettings(
            currentRoot,
            _localizer.Language,
            GetSelectedModuleIds());
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
