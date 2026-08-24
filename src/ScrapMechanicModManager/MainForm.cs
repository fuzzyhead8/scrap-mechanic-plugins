using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using ScrapMechanicModManager.Core.History;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Localization;
using ScrapMechanicModManager.Core.Settings;
using ScrapMechanicModManager.Core.Steam;
using ScrapMechanicModManager.Core.Updates;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager;

public sealed class MainForm : Form
{
    private readonly Label _title = new()
    {
        Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
        AutoSize = true,
    };
    private readonly Label _subtitle = new() { ForeColor = Color.DimGray, AutoSize = true };
    private readonly Label _gameRootLabel = new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Padding = new Padding(0, 7, 8, 0),
    };
    private readonly Label _languageLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 4, 0) };
    private readonly ComboBox _languageSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 112,
    };
    private readonly TextBox _gameRoot = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = new() { AutoSize = true };
    private readonly Button _check = new() { AutoSize = true };
    private readonly Button _install = new() { AutoSize = true };
    private readonly Button _restore = new() { AutoSize = true };
    private readonly Button _launch = new() { AutoSize = true };
    private readonly CheckBox _devMode = new() { AutoSize = true };
    private readonly Label _modulesLabel = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
    };
    private readonly Button _openModsFolder = new() { AutoSize = true };
    private readonly Button _refreshModules = new() { AutoSize = true };
    private readonly TableLayoutPanel _modulesPanel = new()
    {
        Dock = DockStyle.Top,
        ColumnCount = 3,
        RowCount = 1,
        AutoSize = true,
        GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        Padding = new Padding(0, 4, 0, 4),
    };
    private readonly Dictionary<string, ModuleRowControls> _moduleRows = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly Label _gameStatus = new() { AutoSize = true };
    private readonly Label _modStatus = new() { AutoSize = true };
    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Fill,
        Style = ProgressBarStyle.Marquee,
        Visible = false,
    };
    private readonly RichTextBox _log = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = Color.FromArgb(28, 31, 36),
        ForeColor = Color.Gainsboro,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Consolas", 9F),
    };

    private readonly SteamRootDiscovery _steamRootDiscovery = new();
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
    private readonly Icon? _applicationIcon;
    private readonly OnlineModuleCatalogClient _onlineCatalogClient;
    private readonly LocalModulePackageSource _localModuleSource;
    private readonly ModulePayloadAcquirer _payloadAcquirer;
    private readonly string _appVersion;
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

    public MainForm()
    {
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (_applicationIcon is not null)
        {
            Icon = _applicationIcon;
        }

        _settings = _settingsStore.Load();
        _localizer.Language = _settings.Language;

        string informationalVersion = typeof(MainForm).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.2.0-preview.13";
        _appVersion = informationalVersion.Split('+', 2)[0];
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ScrapMechanicModManager", _appVersion));
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
            _settings.ModuleSourcePreferences,
            _appVersion);

        InitializeUi();
        RebuildModuleRows();
        ApplySelectedModuleSettings();
        LoadOperationHistory();
        RefreshBackupStatuses();
        ApplyLocalizedText();
        WireEvents();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            _httpClient.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _applicationIcon?.Dispose();
        }
    }

    private void InitializeUi()
    {
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        ClientSize = new Size(900, 620);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(242, 244, 247);

        var headerText = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
        };
        headerText.Controls.AddRange([_title, _subtitle]);

        var languagePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        languagePanel.Controls.AddRange([_languageLabel, _languageSelector]);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(headerText, 0, 0);
        header.Controls.Add(languagePanel, 1, 0);

        var pathLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
        };
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathLayout.Controls.Add(_gameRootLabel, 0, 0);
        pathLayout.Controls.Add(_gameRoot, 1, 0);
        pathLayout.Controls.Add(_browse, 2, 0);

        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 8),
        };
        statusPanel.Controls.Add(_gameStatus);
        statusPanel.Controls.Add(_modStatus);

        _modulesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        _modulesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        _modulesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        var modulesHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = Padding.Empty,
        };
        modulesHost.Controls.Add(_modulesPanel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 8),
        };
        actions.Controls.AddRange([
            _check,
            _install,
            _restore,
            _refreshModules,
            _openModsFolder,
            _launch,
            _devMode,
        ]);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(22),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(pathLayout, 0, 2);
        root.Controls.Add(statusPanel, 0, 3);
        root.Controls.Add(modulesHost, 0, 4);
        root.Controls.Add(actions, 0, 5);
        root.Controls.Add(_progress, 0, 6);
        root.Controls.Add(_log, 0, 7);
        Controls.Add(root);
    }

    private static Label CreateModuleDetailLabel() => new()
    {
        AutoSize = false,
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        ForeColor = Color.DimGray,
        Height = 20,
        Margin = Padding.Empty,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private void WireEvents()
    {
        Shown += async (_, _) => await RunBusyAsync(async () =>
        {
            await RefreshModuleRegistryAsync();
            await AutoDetectAsync();
        });
        FormClosing += (_, _) => _lifetimeCancellation.Cancel();
        _browse.Click += (_, _) => BrowseForGameRoot();
        _check.Click += async (_, _) => await RunBusyAsync(CheckForUpdatesAsync);
        _install.Click += async (_, _) => await RunBusyAsync(InstallLatestAsync);
        _restore.Click += async (_, _) => await RunBusyAsync(RestoreSelectedModulesAsync);
        _refreshModules.Click += async (_, _) =>
            await RunBusyAsync(RefreshModuleRegistryAsync);
        _openModsFolder.Click += (_, _) => OpenModsFolder();
        _launch.Click += (_, _) => LaunchGame();
        _languageSelector.SelectedIndexChanged += async (_, _) =>
            await RunBusyAsync(OnLanguageChangedAsync);
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
            _gameRoot.Text = _settings.GameRoot;
            _selectedInstallation = ResolveSelectedInstallation(_settings.GameRoot);
            ShowLocalGameStatus();
            Log(TextKey.LogSavedGameRootLoaded);
            return;
        }
        if (!string.IsNullOrWhiteSpace(_settings.GameRoot))
        {
            Log(TextKey.LogSavedGameRootInvalid, _settings.GameRoot);
        }

        foreach (string steamRoot in _steamRootDiscovery.FindCandidateRoots())
        {
            SteamInstallation? installation = _steamLibraryLocator
                .FindInstallations(steamRoot)
                .FirstOrDefault();
            if (installation is null) continue;

            _selectedInstallation = installation;
            _gameRoot.Text = installation.GameRoot;
            ShowLocalGameStatus();
            await SaveCurrentSettingsAsync(installation.GameRoot);
            Log(TextKey.LogAutoDetectedSteamInstall, installation.GameRoot);
            return;
        }

        SetGameStatus(TextKey.GameStatusNotFoundAutomatically);
        Log(TextKey.LogAutoDetectFailedUseBrowse);
    }

    private void BrowseForGameRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = _localizer.Get(TextKey.DialogSelectGameRootTitle),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_gameRoot.Text) ? _gameRoot.Text : string.Empty,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _gameRoot.Text = dialog.SelectedPath;
        _selectedInstallation = null;
        SetGameStatus(TextKey.GameStatusPathProvidedNeedsCheck);
        SetModStatus(TextKey.ModStatusNotChecked);
        foreach (ModuleRegistryEntry entry in _moduleRegistry.Entries)
        {
            SetModuleStatus(entry.ModId, TextKey.ModuleStatusNotChecked);
        }
        RefreshBackupStatuses();
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
            _settings.ModuleSourcePreferences,
            _appVersion);
        RebuildModuleRows();
        ApplySelectedModuleSettings();
        RefreshBackupStatuses();
    }

    private void RebuildModuleRows()
    {
        _modulesPanel.SuspendLayout();
        try
        {
            foreach (Control control in _modulesPanel.Controls
                         .Cast<Control>()
                         .Where(control => !ReferenceEquals(control, _modulesLabel))
                         .ToArray())
            {
                control.Dispose();
            }
            _modulesPanel.Controls.Clear();
            _modulesPanel.RowStyles.Clear();
            _modulesPanel.RowCount = 1;
            _modulesPanel.Controls.Add(_modulesLabel, 0, 0);
            _modulesPanel.SetColumnSpan(_modulesLabel, 3);
            _moduleRows.Clear();

            int rowIndex = 1;
            foreach (ModuleRegistryEntry entry in _moduleRegistry.Entries)
            {
                _moduleStatusMessages.TryAdd(
                    entry.ModId,
                    new LocalizedMessage(TextKey.ModuleStatusNotChecked));
                _moduleBackupStatuses.TryAdd(
                    entry.ModId,
                    EmptyBackupStatus(entry.ModId));

                var selector = new CheckBox
                {
                    AutoSize = true,
                    Checked = _settings.SelectedModuleIds.Contains(
                        entry.ModId,
                        StringComparer.OrdinalIgnoreCase),
                    Enabled = entry.CanInstall || HasRestorableBackup(entry.ModId),
                    Text = entry.SelectedSource == ModuleSourceKind.Local
                        ? $"{GetModuleDisplayName(entry.ModId)} · " +
                          _localizer.Get(TextKey.ModuleLocalUnverified)
                        : GetModuleDisplayName(entry.ModId),
                    Margin = new Padding(0, 1, 8, 0),
                };
                ComboBox? sourceSelector = null;
                Control nameCell = selector;
                if (entry.HasSourceChoice)
                {
                    sourceSelector = CreateSourceSelector(entry);
                    var namePanel = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        AutoSize = true,
                        WrapContents = false,
                        Margin = Padding.Empty,
                    };
                    namePanel.Controls.Add(selector);
                    namePanel.Controls.Add(sourceSelector);
                    nameCell = namePanel;
                }

                Label status = CreateModuleDetailLabel();
                Label backup = CreateModuleDetailLabel();
                _modulesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                _modulesPanel.Controls.Add(nameCell, 0, rowIndex);
                _modulesPanel.Controls.Add(status, 1, rowIndex);
                _modulesPanel.Controls.Add(backup, 2, rowIndex);
                _moduleRows[entry.ModId] = new ModuleRowControls(
                    selector,
                    status,
                    backup,
                    sourceSelector);

                string modId = entry.ModId;
                selector.CheckedChanged += async (_, _) =>
                    await RunBusyAsync(() => SaveCurrentSettingsAsync());
                if (sourceSelector is not null)
                {
                    sourceSelector.SelectionChangeCommitted += async (_, _) =>
                    {
                        if (sourceSelector.SelectedItem is ModuleSourceChoiceItem selected)
                        {
                            await RunBusyAsync(() => ChangeModuleSourceAsync(
                                modId,
                                selected.Source));
                        }
                    };
                }
                rowIndex++;
            }
            _modulesPanel.RowCount = rowIndex;
        }
        finally
        {
            _modulesPanel.ResumeLayout(performLayout: true);
        }

        RenderModuleStatuses();
        RenderBackupStatuses();
    }

    private ComboBox CreateSourceSelector(ModuleRegistryEntry entry)
    {
        var selector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 94,
            Margin = Padding.Empty,
        };
        foreach (ModuleSourceKind source in entry.Candidates
                     .Select(candidate => candidate.SourceKind)
                     .Distinct())
        {
            selector.Items.Add(new ModuleSourceChoiceItem(
                source,
                _localizer.Get(source == ModuleSourceKind.Online
                    ? TextKey.ModuleSourceOnline
                    : TextKey.ModuleSourceLocal)));
        }
        selector.SelectedIndex = selector.Items
            .Cast<ModuleSourceChoiceItem>()
            .Select((item, index) => (item, index))
            .First(pair => pair.item.Source == entry.SelectedSource)
            .index;
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
        _moduleRegistry = ModuleRegistry.Create(
            _moduleCandidates,
            preferences,
            _appVersion);
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
        RefreshBackupStatuses();
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
        if (!await EnsureElevatedForWriteAsync()) return;

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
        RefreshBackupStatuses();
        if (result.CacheBundleInvalidated)
        {
            Log(TextKey.LogScriptCacheInvalidated);
        }
    }

    private async Task RestoreSelectedModulesAsync()
    {
        IReadOnlyList<string> selectedModuleIds = GetSelectedModuleIds();
        if (selectedModuleIds.Count == 0)
        {
            throw new UserFacingException(TextKey.ErrorNoModulesSelected);
        }
        if (!await EnsureElevatedForWriteAsync()) return;
        EnsureGameIsNotRunning();
        SteamInstallation installation = ResolveSelectedInstallation(
            RequireGameRoot());
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
        if (!ShowConfirmation(
                TextKey.DialogRestoreSelectedModulesTitle,
                TextKey.DialogRestoreSelectedModulesMessage,
                TextKey.DialogButtonRestore,
                moduleList))
        {
            return;
        }

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
        RefreshBackupStatuses();
        if (cacheBundleInvalidated)
        {
            Log(TextKey.LogScriptCacheInvalidated);
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

        string executable = Path.Combine(installation.GameRoot, "Release", "ScrapMechanic.exe");
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
            StringComparer.OrdinalIgnoreCase);
        DirectoryInfo? library = Directory.GetParent(normalizedGameRoot)?.Parent?.Parent;
        if (library is not null) roots.Add(library.FullName);

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
        if (_selectedInstallation is null) return;
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
        string? steamExe = _steamRootDiscovery.FindCandidateRoots()
            .Select(root => Path.Combine(root, "steam.exe"))
            .FirstOrDefault(File.Exists);
        if (steamExe is not null)
        {
            string arguments = "-applaunch 387990" + (_devMode.Checked ? " -dev" : string.Empty);
            Process.Start(new ProcessStartInfo(steamExe, arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(steamExe),
            });
        }
        else
        {
            if (_devMode.Checked)
            {
                Log(TextKey.LogSteamExeDevModeUnavailable);
            }
            Process.Start(new ProcessStartInfo("steam://rungameid/387990")
            {
                UseShellExecute = true,
            });
        }
        Log(TextKey.LogLaunchRequested, installation.GameRoot);
    }

    private string RequireGameRoot()
    {
        string value = _gameRoot.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UserFacingException(TextKey.ErrorMissingGameRoot);
        }
        return value;
    }

    private async Task<bool> EnsureElevatedForWriteAsync()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator)) return true;

        await SaveCurrentSettingsAsync(RequireGameRoot());
        if (!ShowConfirmation(
                TextKey.DialogAdministratorTitle,
                TextKey.DialogAdministratorRestartMessage,
                TextKey.DialogButtonYes))
        {
            return false;
        }

        try
        {
            string executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(executablePath)
            {
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            BeginInvoke(Close);
            return false;
        }
        catch (Win32Exception error)
        {
            LogDetailed(
                TextKey.LogElevatedRestartCanceled,
                OperationSeverity.Warning,
                GetSelectedModuleIds(),
                null,
                error,
                error.NativeErrorCode);
            return false;
        }
    }

    private static void EnsureGameIsNotRunning()
    {
        if (Process.GetProcessesByName("ScrapMechanic").Length > 0)
        {
            throw new UserFacingException(TextKey.ErrorGameRunning);
        }
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
            ShowError(message);
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
        _progress.Visible = busy;
        _browse.Enabled = !busy;
        _check.Enabled = !busy;
        _install.Enabled = !busy;
        _restore.Enabled = !busy;
        _refreshModules.Enabled = !busy;
        _openModsFolder.Enabled = !busy;
        _launch.Enabled = !busy;
        foreach ((string modId, ModuleRowControls row) in _moduleRows)
        {
            bool canSelect = (_moduleRegistry.TryGetEntry(
                    modId,
                    out ModuleRegistryEntry? entry)
                    && entry is not null
                    && entry.CanInstall)
                || HasRestorableBackup(modId);
            row.Selection.Enabled = !busy && canSelect;
            if (row.SourceSelector is not null)
            {
                row.SourceSelector.Enabled = !busy;
            }
        }
        _languageSelector.Enabled = !busy;
        UseWaitCursor = busy;
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
        Text = _localizer.Get(TextKey.AppTitle);
        _title.Text = _localizer.Get(TextKey.AppHeader);
        _subtitle.Text = _localizer.Get(TextKey.AppSubtitle);
        _gameRootLabel.Text = _localizer.Get(TextKey.GameRootLabel);
        _browse.Text = _localizer.Get(TextKey.ButtonBrowse);
        _check.Text = _localizer.Get(TextKey.ButtonCheck);
        _install.Text = _localizer.Get(TextKey.ButtonInstallUpdate);
        _restore.Text = _localizer.Get(TextKey.ButtonRestoreSelectedModules);
        _refreshModules.Text = _localizer.Get(TextKey.ButtonRefreshModules);
        _openModsFolder.Text = _localizer.Get(TextKey.ButtonOpenModsFolder);
        _launch.Text = _localizer.Get(TextKey.ButtonLaunchGame);
        _devMode.Text = _localizer.Get(TextKey.CheckBoxDevMode);
        _modulesLabel.Text = _localizer.Get(TextKey.ModulesLabel);
        _languageLabel.Text = _localizer.Get(TextKey.LanguageLabel);

        _applyingLanguage = true;
        try
        {
            _languageSelector.Items.Clear();
            _languageSelector.Items.Add(_localizer.Get(TextKey.LanguageHungarian));
            _languageSelector.Items.Add(_localizer.Get(TextKey.LanguageEnglish));
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
            row.Selection.Checked = _settings.SelectedModuleIds.Contains(
                modId,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private IReadOnlyList<string> GetSelectedModuleIds() => _moduleRows
        .Where(pair => pair.Value.Selection.Checked)
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
        _moduleStatusMessages[modId] = new LocalizedMessage(key, arguments);
        RenderModuleStatuses();
    }

    private void RenderModuleStatuses()
    {
        foreach ((string modId, ModuleRowControls row) in _moduleRows)
        {
            LocalizedMessage message = _moduleStatusMessages.TryGetValue(
                modId,
                out LocalizedMessage? stored)
                ? stored
                : new LocalizedMessage(TextKey.ModuleStatusNotChecked);
            row.Status.Text = message.Render(_localizer);
        }
    }

    private void RefreshBackupStatuses()
    {
        foreach (ModuleRegistryEntry entry in _moduleRegistry.Entries)
        {
            _moduleBackupStatuses[entry.ModId] = _backupCatalog.GetModuleStatus(
                BackupRoot,
                entry.ModId);
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
            ModuleBackupStatus status = _moduleBackupStatuses.TryGetValue(
                modId,
                out ModuleBackupStatus? stored)
                ? stored
                : EmptyBackupStatus(modId);
            RenderBackupStatus(row.BackupStatus, status);
        }
    }

    private void RenderBackupStatus(Label label, ModuleBackupStatus status)
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
            label.Text = _localizer.Get(key, localTimestamp);
            return;
        }
        label.Text = _localizer.Get(key);
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

    private void LoadOperationHistory()
    {
        bool loaded = _operationJournal.TryReadRecent(
            out IReadOnlyList<OperationRecord> records,
            out string? error);
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
        }
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
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
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

    private bool ShowConfirmation(
        TextKey titleKey,
        TextKey messageKey,
        TextKey confirmButtonKey,
        params object?[] arguments)
    {
        using var dialog = CreateDialog(titleKey, messageKey, arguments);
        var confirm = new Button
        {
            Text = _localizer.Get(confirmButtonKey),
            AutoSize = true,
            DialogResult = DialogResult.OK,
        };
        var cancel = new Button
        {
            Text = _localizer.Get(TextKey.DialogButtonCancel),
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
        };
        AddDialogButtons(dialog, cancel, confirm);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private void ShowError(string message)
    {
        using Form dialog = CreateDialog(TextKey.DialogErrorTitle, message);
        var ok = new Button
        {
            Text = _localizer.Get(TextKey.DialogButtonOk),
            AutoSize = true,
            DialogResult = DialogResult.OK,
        };
        AddDialogButtons(dialog, ok);
        dialog.AcceptButton = ok;
        dialog.CancelButton = ok;
        dialog.ShowDialog(this);
    }

    private Form CreateDialog(TextKey titleKey, TextKey messageKey, params object?[] arguments) =>
        CreateDialog(titleKey, new LocalizedMessage(messageKey, arguments));

    private Form CreateDialog(TextKey titleKey, LocalizedMessage message) =>
        CreateDialog(titleKey, message.Render(_localizer));

    private Form CreateDialog(TextKey titleKey, string message)
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18),
        };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(500, 0),
        }, 0, 0);
        var dialog = new Form
        {
            Text = _localizer.Get(titleKey),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(540, 190),
        };
        dialog.Controls.Add(content);
        return dialog;
    }

    private static void AddDialogButtons(Form dialog, params Button[] buttons)
    {
        var content = (TableLayoutPanel)dialog.Controls[0];
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };
        panel.Controls.AddRange(buttons);
        content.Controls.Add(panel, 0, 1);
    }

    private sealed record ModuleRowControls(
        CheckBox Selection,
        Label Status,
        Label BackupStatus,
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
