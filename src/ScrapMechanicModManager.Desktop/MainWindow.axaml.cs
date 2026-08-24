using System.Diagnostics;
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
    private readonly Button _openModsFolder;
    private readonly Button _refreshModules;
    private readonly StackPanel _modulesPanel;
    private readonly Dictionary<string, ModuleRowControls> _moduleRows = new(
        StringComparer.OrdinalIgnoreCase);
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
    private readonly OnlineModuleCatalogClient _onlineCatalogClient;
    private readonly LocalModulePackageSource _localModuleSource;
    private readonly ModulePayloadAcquirer _payloadAcquirer;
    private readonly AppLocalizer _localizer = new();
    private readonly ManagerSettingsStore _settingsStore = new(SettingsPath);
    private readonly JsonLinesOperationJournal _operationJournal = new(OperationHistoryPath);
    private readonly List<OperationRecord> _operationHistory = [];
    private ManagerSettings _settings = ManagerSettings.Default;
    private LocalizedMessage _gameStatusMessage = new(TextKey.GameStatusNotChecked);
    private LocalizedMessage _modStatusMessage = new(TextKey.ModStatusNotChecked);
    private readonly Dictionary<string, LocalizedMessage> _moduleStatusMessages = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModuleBackupStatus> _moduleBackupStatuses = new(
        StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ModuleCandidate> _moduleCandidates = [];
    private ModuleRegistry _moduleRegistry = ModuleRegistry.Create([]);
    private SteamInstallation? _selectedInstallation;
    private string? _activeOperationId;
    private bool _applyingLanguage;

    private static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScrapMechanicModManager");
    private static string BackupRoot => Path.Combine(AppDataRoot, "backups");
    private static string ModsRoot => Path.Combine(AppDataRoot, "mods");
    private static string CatalogCachePath => Path.Combine(
        AppDataRoot,
        "cache",
        "catalog-v1.json");
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
        _openModsFolder = this.FindControl<Button>("OpenModsFolderButton")!;
        _refreshModules = this.FindControl<Button>("RefreshModulesButton")!;
        _modulesPanel = this.FindControl<StackPanel>("ModulesPanel")!;
        _gameStatus = this.FindControl<TextBlock>("GameStatusText")!;
        _modStatus = this.FindControl<TextBlock>("ModStatusText")!;
        _progress = this.FindControl<ProgressBar>("ProgressBar")!;
        _log = this.FindControl<TextBox>("LogTextBox")!;

        _settings = _settingsStore.Load();
        _localizer.Language = _settings.Language;

        string informationalVersion = typeof(MainWindow).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.2.0-preview.11";
        string appVersion = informationalVersion.Split('+', 2)[0];
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ScrapMechanicModManager-Linux", appVersion));
        _onlineCatalogClient = new OnlineModuleCatalogClient(
            _httpClient,
            OnlineModuleCatalogClient.DefaultCatalogUri,
            CatalogCachePath);
        _localModuleSource = new LocalModulePackageSource(ModsRoot);
        _payloadAcquirer = new ModulePayloadAcquirer(
            _httpClient,
            Path.GetTempPath());
        _moduleCandidates = CreateFallbackCandidates();
        _moduleRegistry = ModuleRegistry.Create(
            _moduleCandidates,
            _settings.ModuleSourcePreferences);

        RebuildModuleRows();
        ApplySelectedModuleSettings();
        ApplyLocalizedText();

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

    private async void OnRefreshModulesClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(RefreshModuleRegistryAsync);
    }

    private async void OnOpenModsFolderClick(object? sender, RoutedEventArgs e)
    {
        await RunBusyAsync(() =>
        {
            OpenModsFolder();
            return Task.CompletedTask;
        });
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
        await RefreshModuleRegistryAsync();
        await RefreshBackupStatusesAsync();
        await AutoDetectAsync();
    }

    private static void OpenModsFolder()
    {
        Directory.CreateDirectory(ModsRoot);
        Process.Start(new ProcessStartInfo(ModsRoot)
        {
            UseShellExecute = true,
        });
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
        foreach (ModuleRegistryEntry entry in _moduleRegistry.Entries)
        {
            SetModuleStatus(entry.ModId, TextKey.ModuleStatusNotChecked);
        }
        await RefreshBackupStatusesAsync();
    }

    private async Task RefreshModuleRegistryAsync()
    {
        var candidates = new List<ModuleCandidate>();
        try
        {
            OnlineModuleCatalogLoadResult online =
                await _onlineCatalogClient.LoadAsync(_lifetimeCancellation.Token);
            candidates.AddRange(online.Snapshot.Candidates);
            foreach (ModuleSourceIssue issue in online.Snapshot.Issues)
            {
                LogDetailed(
                    TextKey.LogModuleSourceIssue,
                    OperationSeverity.Warning,
                    [],
                    null,
                    null,
                    issue.Source,
                    string.Join("; ", issue.Errors));
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException
            or IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            LogDetailed(
                TextKey.LogOnlineCatalogFallback,
                OperationSeverity.Warning,
                [],
                null,
                error,
                error.Message);
            candidates.AddRange(CreateFallbackCandidates());
        }

        ModuleSourceSnapshot local = await Task.Run(
            _localModuleSource.Load,
            _lifetimeCancellation.Token);
        candidates.AddRange(local.Candidates);
        foreach (ModuleSourceIssue issue in local.Issues)
        {
            LogDetailed(
                TextKey.LogModuleSourceIssue,
                OperationSeverity.Warning,
                [],
                null,
                null,
                Path.GetFileName(issue.Source),
                string.Join("; ", issue.Errors));
        }

        _moduleCandidates = candidates;
        _moduleRegistry = ModuleRegistry.Create(
            _moduleCandidates,
            _settings.ModuleSourcePreferences);
        RebuildModuleRows();
        ApplySelectedModuleSettings();
        await RefreshBackupStatusesAsync();
    }

    private void RebuildModuleRows()
    {
        _modulesPanel.Children.Clear();
        _moduleRows.Clear();

        foreach (ModuleRegistryEntry entry in _moduleRegistry.Entries)
        {
            _moduleStatusMessages.TryAdd(
                entry.ModId,
                new LocalizedMessage(TextKey.ModuleStatusNotChecked));
            _moduleBackupStatuses.TryAdd(
                entry.ModId,
                EmptyBackupStatus(entry.ModId));

            var selection = new CheckBox
            {
                IsChecked = _settings.SelectedModuleIds.Contains(
                    entry.ModId,
                    StringComparer.OrdinalIgnoreCase),
                IsEnabled = entry.CanInstall || HasRestorableBackup(entry.ModId),
                Content = entry.SelectedSource == ModuleSourceKind.Local
                    ? $"{GetModuleDisplayName(entry.ModId)} · " +
                      _localizer.Get(TextKey.ModuleLocalUnverified)
                    : GetModuleDisplayName(entry.ModId),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            ComboBox? sourceSelector = null;
            var namePanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            namePanel.Children.Add(selection);
            if (entry.HasSourceChoice)
            {
                sourceSelector = CreateSourceSelector(entry);
                namePanel.Children.Add(sourceSelector);
            }

            var status = new TextBlock
            {
                Opacity = 0.68,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
            var backup = new TextBlock
            {
                Opacity = 0.68,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            };
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("3*,2*,3*"),
                ColumnSpacing = 12,
                MinHeight = 28,
            };
            Grid.SetColumn(namePanel, 0);
            Grid.SetColumn(status, 1);
            Grid.SetColumn(backup, 2);
            row.Children.Add(namePanel);
            row.Children.Add(status);
            row.Children.Add(backup);
            _modulesPanel.Children.Add(row);
            _moduleRows[entry.ModId] = new ModuleRowControls(
                selection,
                status,
                backup,
                sourceSelector);

            string modId = entry.ModId;
            selection.Click += async (_, _) =>
                await RunBusyAsync(() => SaveCurrentSettingsAsync());
            if (sourceSelector is not null)
            {
                sourceSelector.SelectionChanged += async (_, _) =>
                {
                    if (sourceSelector.SelectedItem is ModuleSourceChoiceItem selected)
                    {
                        await RunBusyAsync(() => ChangeModuleSourceAsync(
                            modId,
                            selected.Source));
                    }
                };
            }
        }

        RenderModuleStatuses();
        RenderBackupStatuses();
    }

    private ComboBox CreateSourceSelector(ModuleRegistryEntry entry)
    {
        ModuleSourceChoiceItem[] choices = entry.Candidates
            .Select(candidate => candidate.SourceKind)
            .Distinct()
            .Select(source => new ModuleSourceChoiceItem(
                source,
                _localizer.Get(source == ModuleSourceKind.Online
                    ? TextKey.ModuleSourceOnline
                    : TextKey.ModuleSourceLocal)))
            .ToArray();
        var selector = new ComboBox
        {
            ItemsSource = choices,
            MinWidth = 92,
            SelectedIndex = Array.FindIndex(
                choices,
                choice => choice.Source == entry.SelectedSource),
        };
        return selector;
    }

    private async Task ChangeModuleSourceAsync(
        string modId,
        ModuleSourceKind source)
    {
        var preferences = new Dictionary<string, ModuleSourceKind>(
            _settings.ModuleSourcePreferences,
            StringComparer.OrdinalIgnoreCase)
        {
            [modId] = source,
        };
        _settings = new ManagerSettings(
            string.IsNullOrWhiteSpace(_gameRoot.Text)
                ? _settings.GameRoot
                : _gameRoot.Text.Trim(),
            _localizer.Language,
            GetSelectedModuleIds(),
            preferences);
        _moduleRegistry = ModuleRegistry.Create(_moduleCandidates, preferences);
        RebuildModuleRows();
        await SaveCurrentSettingsAsync();
    }

    private static IReadOnlyList<ModuleCandidate> CreateFallbackCandidates() =>
    [
        CreateFallbackCandidate(
            BuiltInModuleIds.RobotLoot,
            "Robot Loot",
            defaultSelected: true),
        CreateFallbackCandidate(
            BuiltInModuleIds.BeehiveAutomation,
            "Beehive Automation"),
        CreateFallbackCandidate(
            BuiltInModuleIds.FreezerAutomation,
            "Freezer Automation"),
    ];

    private static ModuleCandidate CreateFallbackCandidate(
        string modId,
        string name,
        bool defaultSelected = false) => new(
        new ModulePackageDefinition
        {
            SchemaVersion = 1,
            ModId = modId,
            Version = "0.0.0",
            DisplayName = new LocalizedModuleText(name, name),
            Description = new LocalizedModuleText(string.Empty, string.Empty),
            MinimumManagerVersion = "0.2.0",
            SupportedBuildIds = [],
            Files = [],
        },
        ModuleSourceKind.Online,
        new string('0', 64),
        PackageDownloadUrl: null,
        LocalPackagePath: null,
        defaultSelected,
        ["Online catalog is not available."]);

    private async Task CheckForUpdatesAsync()
    {
        await RefreshModuleRegistryAsync();
        string[] availableIds = _moduleRegistry.Entries
            .Where(entry => entry.CanInstall)
            .Select(entry => entry.ModId)
            .ToArray();
        if (availableIds.Length == 0)
        {
            throw new UserFacingException(TextKey.ErrorModuleCatalogUnavailable);
        }

        (SteamInstallation installation, IReadOnlyList<ModuleCandidate> candidates,
            string productVersion) = await ResolveAndValidateCandidatesAsync(
                availableIds,
                requireCommonBuild: false);
        foreach (ModuleRegistryEntry entry in _moduleRegistry.Entries)
        {
            SetModuleStatus(entry.ModId, TextKey.ModuleStatusUnavailable);
        }

        bool allCurrent = candidates.Count > 0;
        foreach (ModuleCandidate candidate in candidates)
        {
            if (!candidate.Definition.SupportedBuildIds.Contains(
                    installation.BuildId,
                    StringComparer.Ordinal))
            {
                allCurrent = false;
                continue;
            }

            ModManifest manifest = candidate.CreateInstallManifest();
            ModuleInstallState state = await _moduleStatusEvaluator.EvaluateAsync(
                installation.GameRoot,
                BackupRoot,
                manifest,
                _lifetimeCancellation.Token);
            TextKey statusKey = state switch
            {
                ModuleInstallState.UpToDate => TextKey.ModuleStatusUpToDate,
                ModuleInstallState.UpdateAvailable => TextKey.ModuleStatusUpdateAvailable,
                _ => TextKey.ModuleStatusNotInstalled,
            };
            SetModuleStatus(candidate.ModId, statusKey, candidate.Definition.Version);
            allCurrent &= state == ModuleInstallState.UpToDate;
        }

        SetGameStatus(TextKey.GameStatusReady, productVersion, installation.BuildId);
        SetModStatus(
            allCurrent ? TextKey.ModStatusUpToDate : TextKey.ModStatusUpdateAvailable,
            "catalog-v1");
        await RefreshBackupStatusesAsync();
        Log(TextKey.LogLatestRelease, "catalog-v1", installation.BuildId);
    }

    private async Task InstallLatestAsync()
    {
        IReadOnlyList<string> selectedModuleIds = GetSelectedModuleIds();
        if (selectedModuleIds.Count == 0)
        {
            throw new UserFacingException(TextKey.ErrorNoModulesSelected);
        }

        EnsureGameIsNotRunning();
        await RefreshModuleRegistryAsync();
        (SteamInstallation installation, IReadOnlyList<ModuleCandidate> selectedModules, _) =
            await ResolveAndValidateCandidatesAsync(selectedModuleIds);

        foreach (ModuleCandidate module in selectedModules)
        {
            object source = module.SourceKind == ModuleSourceKind.Online
                ? module.PackageDownloadUrl ?? new Uri("https://github.com")
                : module.LocalPackagePath ?? string.Empty;
            LogDetailed(
                TextKey.LogModulePayloadDownload,
                OperationSeverity.Information,
                [module.ModId],
                null,
                null,
                GetModuleDisplayName(module.ModId),
                source);
        }

        try
        {
            EnsureGameIsNotRunning();
            InstallResult result = await _moduleInstaller.InstallCandidatesAsync(
                installation.GameRoot,
                selectedModules,
                _payloadAcquirer,
                BackupRoot,
                _lifetimeCancellation.Token);
            await SaveCurrentSettingsAsync(installation.GameRoot);
            foreach (ModuleCandidate module in selectedModules)
            {
                SetModuleStatus(
                    module.ModId,
                    TextKey.ModuleStatusInstalled,
                    module.Definition.Version);
            }
            SetModStatus(TextKey.ModStatusInstalled, "catalog-v1");
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
        IReadOnlyList<ModuleCandidate> Candidates,
        string ProductVersion)> ResolveAndValidateCandidatesAsync(
            IReadOnlyCollection<string> requiredModuleIds,
            bool requireCommonBuild = true)
    {
        SteamInstallation installation = ResolveSelectedInstallation(RequireGameRoot());
        var candidates = new List<ModuleCandidate>(requiredModuleIds.Count);
        var unavailable = new List<string>();
        foreach (string modId in requiredModuleIds)
        {
            if (!_moduleRegistry.TryGetEntry(modId, out ModuleRegistryEntry? entry)
                || entry is null
                || !entry.CanInstall)
            {
                unavailable.Add(GetModuleDisplayName(modId));
                continue;
            }
            candidates.Add(entry.SelectedCandidate);
        }
        if (unavailable.Count > 0)
        {
            throw new UserFacingException(
                TextKey.ErrorSelectedModulesUnavailable,
                string.Join(", ", unavailable));
        }

        if (requireCommonBuild)
        {
            IReadOnlyList<ModuleTargetConflict> conflicts =
                _moduleRegistry.FindTargetConflicts(requiredModuleIds);
            if (conflicts.Count > 0)
            {
                throw new UserFacingException(
                    TextKey.ErrorModuleTargetConflict,
                    string.Join(", ", conflicts.Select(conflict => conflict.Target)));
            }
        }

        string[] supportedBuildIds = requireCommonBuild
            ? GetCommonSupportedBuildIds(candidates)
            : [installation.BuildId];
        if (requireCommonBuild && supportedBuildIds.Length == 0)
        {
            throw new UserFacingException(TextKey.ErrorModulesNoCommonBuild);
        }

        string executable = Path.Combine(
            installation.GameRoot,
            "Release",
            "ScrapMechanic.exe");
        string productVersion = ReadProductVersionForUser(executable);
        GameInstallValidationResult validation = _gameValidator.Validate(
            installation.GameRoot,
            productVersion,
            installation.BuildId,
            supportedBuildIds);
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
        return (installation, candidates, productVersion);
    }

    private static string[] GetCommonSupportedBuildIds(
        IReadOnlyList<ModuleCandidate> modules)
    {
        if (modules.Count == 0) return [];

        var commonBuildIds = new HashSet<string>(
            modules[0].Definition.SupportedBuildIds,
            StringComparer.Ordinal);
        foreach (ModuleCandidate module in modules.Skip(1))
        {
            commonBuildIds.IntersectWith(module.Definition.SupportedBuildIds);
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
        _refreshModules.IsEnabled = !busy;
        _openModsFolder.IsEnabled = !busy;
        _launch.IsEnabled = !busy;
        foreach ((string modId, ModuleRowControls row) in _moduleRows)
        {
            bool canSelect = (_moduleRegistry.TryGetEntry(
                    modId,
                    out ModuleRegistryEntry? entry)
                    && entry is not null
                    && entry.CanInstall)
                || HasRestorableBackup(modId);
            row.Selection.IsEnabled = !busy && canSelect;
            if (row.SourceSelector is not null)
            {
                row.SourceSelector.IsEnabled = !busy;
            }
        }
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
        _refreshModules.Content = _localizer.Get(TextKey.ButtonRefreshModules);
        _openModsFolder.Content = _localizer.Get(TextKey.ButtonOpenModsFolder);
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

        RebuildModuleRows();
        ApplySelectedModuleSettings();
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
        foreach ((string modId, ModuleRowControls row) in _moduleRows)
        {
            row.Selection.IsChecked = _settings.SelectedModuleIds.Contains(
                modId,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private IReadOnlyList<string> GetSelectedModuleIds() => _moduleRows
        .Where(pair => pair.Value.Selection.IsChecked == true)
        .Select(pair => pair.Key)
        .ToArray();

    private string GetModuleDisplayName(string modId)
    {
        if (_moduleRegistry.TryGetEntry(modId, out ModuleRegistryEntry? entry)
            && entry is not null)
        {
            string localized = entry.SelectedCandidate.Definition.DisplayName.Get(
                _localizer.Language);
            if (!string.IsNullOrWhiteSpace(localized)) return localized;
        }
        return modId;
    }

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
        foreach ((string modId, ModuleRowControls row) in _moduleRows)
        {
            if (_moduleStatusMessages.TryGetValue(
                    modId,
                    out LocalizedMessage? message))
            {
                row.Status.Text = message.Render(_localizer);
            }
        }
    }

    private async Task RefreshBackupStatusesAsync()
    {
        string[] moduleIds = _moduleRegistry.Entries
            .Select(entry => entry.ModId)
            .ToArray();
        Dictionary<string, ModuleBackupStatus> statuses = await Task.Run(
            () => moduleIds.ToDictionary(
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

    private bool HasRestorableBackup(string modId) =>
        _moduleBackupStatuses.TryGetValue(modId, out ModuleBackupStatus? status)
        && status.State == BackupSnapshotState.Available;

    private void RenderBackupStatuses()
    {
        foreach ((string modId, ModuleRowControls row) in _moduleRows)
        {
            if (_moduleBackupStatuses.TryGetValue(
                    modId,
                    out ModuleBackupStatus? status))
            {
                RenderBackupStatus(row.Backup, status);
            }
        }
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
            GetSelectedModuleIds(),
            _settings.ModuleSourcePreferences);
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

    private sealed record ModuleRowControls(
        CheckBox Selection,
        TextBlock Status,
        TextBlock Backup,
        ComboBox? SourceSelector);

    private sealed record ModuleSourceChoiceItem(
        ModuleSourceKind Source,
        string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class UserFacingException(TextKey key, params object?[] arguments) : Exception
    {
        public LocalizedMessage UserMessage { get; } = new(key, arguments);
    }
}
