using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Text;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Localization;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Settings;
using ScrapMechanicModManager.Core.Steam;
using ScrapMechanicModManager.Core.Updates;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager;

public sealed class MainForm : Form
{
    private const string RepositoryOwner = "fuzzyhead8";
    private const string RepositoryName = "scrap-mechanic-plugins";

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
    private readonly HashService _hashService = new();
    private readonly ModInstaller _installer = new();
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Icon? _applicationIcon;
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

    public MainForm()
    {
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (_applicationIcon is not null)
        {
            Icon = _applicationIcon;
        }

        _settings = _settingsStore.Load();
        _localizer.Language = _settings.Language;

        _releaseClient = new GitHubReleaseClient(
            _httpClient,
            RepositoryOwner,
            RepositoryName);
        string appVersion = typeof(MainForm).Assembly.GetName().Version?.ToString(3)
            ?? "0.2.0-preview.2";
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ScrapMechanicModManager", appVersion));

        InitializeUi();
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

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 8),
        };
        actions.Controls.AddRange([_check, _install, _restore, _launch, _devMode]);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(22),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(pathLayout, 0, 2);
        root.Controls.Add(statusPanel, 0, 3);
        root.Controls.Add(actions, 0, 4);
        root.Controls.Add(_progress, 0, 5);
        root.Controls.Add(_log, 0, 6);
        Controls.Add(root);
    }

    private void WireEvents()
    {
        Shown += async (_, _) => await RunBusyAsync(AutoDetectAsync);
        FormClosing += (_, _) => _lifetimeCancellation.Cancel();
        _browse.Click += (_, _) => BrowseForGameRoot();
        _check.Click += async (_, _) => await RunBusyAsync(CheckForUpdatesAsync);
        _install.Click += async (_, _) => await RunBusyAsync(InstallLatestAsync);
        _restore.Click += async (_, _) => await RunBusyAsync(RestoreLatestAsync);
        _launch.Click += (_, _) => LaunchGame();
        _languageSelector.SelectedIndexChanged += async (_, _) =>
            await RunBusyAsync(OnLanguageChangedAsync);
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
        if (!await EnsureElevatedForWriteAsync()) return;
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
        finally
        {
            if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
        }
    }

    private async Task RestoreLatestAsync()
    {
        if (!await EnsureElevatedForWriteAsync()) return;
        EnsureGameIsNotRunning();
        SteamInstallation installation = ResolveSelectedInstallation(
            RequireGameRoot());
        string? latestSnapshot = Directory.Exists(BackupRoot)
            ? Directory.GetDirectories(BackupRoot)
                .Where(path => File.Exists(Path.Combine(path, ".snapshot.json")))
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
        if (latestSnapshot is null)
        {
            throw new UserFacingException(TextKey.ErrorNoBackupSnapshot);
        }

        if (!ShowConfirmation(
                TextKey.DialogRestoreBackupTitle,
                TextKey.DialogRestoreBackupMessage,
                TextKey.DialogButtonRestore,
                latestSnapshot))
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

    private async Task<(SteamInstallation Installation, ResolvedRelease Release, string ProductVersion)>
        ResolveAndValidateLatestAsync()
    {
        SteamInstallation installation = ResolveSelectedInstallation(RequireGameRoot());
        ResolvedRelease release = await _releaseClient.GetLatestReleaseAsync(
            _lifetimeCancellation.Token);
        string executable = Path.Combine(installation.GameRoot, "Release", "ScrapMechanic.exe");
        string productVersion = ReadProductVersionForUser(executable);
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
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFacingException(TextKey.ErrorManifestTargetEscapesGameRoot, relativePath);
        }
        return target;
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
            Log(TextKey.LogElevatedRestartCanceled, error.NativeErrorCode);
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
            ShowError(message);
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
        return _localizer.Get(TextKey.ErrorOperationFailed, error.GetType().Name);
    }

    private void SetBusy(bool busy)
    {
        _progress.Visible = busy;
        _browse.Enabled = !busy;
        _check.Enabled = !busy;
        _install.Enabled = !busy;
        _restore.Enabled = !busy;
        _launch.Enabled = !busy;
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
        _restore.Text = _localizer.Get(TextKey.ButtonRestore);
        _launch.Text = _localizer.Get(TextKey.ButtonLaunchGame);
        _devMode.Text = _localizer.Get(TextKey.CheckBoxDevMode);
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
        _settings = new ManagerSettings(currentRoot, _localizer.Language);
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

    private sealed class UserFacingException(TextKey key, params object?[] arguments) : Exception
    {
        public LocalizedMessage UserMessage { get; } = new(key, arguments);
    }
}
