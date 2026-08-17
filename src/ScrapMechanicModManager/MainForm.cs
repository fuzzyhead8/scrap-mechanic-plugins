using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Text.Json;
using ScrapMechanicModManager.Core.Installation;
using ScrapMechanicModManager.Core.Security;
using ScrapMechanicModManager.Core.Steam;
using ScrapMechanicModManager.Core.Updates;
using ScrapMechanicModManager.Core.Validation;

namespace ScrapMechanicModManager;

public sealed class MainForm : Form
{
    private const string RepositoryOwner = "fuzzyhead8";
    private const string RepositoryName = "scrap-mechanic-plugins";

    private readonly TextBox _gameRoot = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = new() { Text = "Tallózás…", AutoSize = true };
    private readonly Button _check = new() { Text = "Ellenőrzés", AutoSize = true };
    private readonly Button _install = new() { Text = "Telepítés / frissítés", AutoSize = true };
    private readonly Button _restore = new() { Text = "Visszaállítás", AutoSize = true };
    private readonly Button _launch = new() { Text = "Játék indítása", AutoSize = true };
    private readonly CheckBox _devMode = new() { Text = "Indítás -dev módban", AutoSize = true };
    private readonly Label _gameStatus = new() { AutoSize = true, Text = "Játék: nincs ellenőrizve" };
    private readonly Label _modStatus = new() { AutoSize = true, Text = "Mod: nincs ellenőrizve" };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };
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
    private SteamInstallation? _selectedInstallation;

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

        _releaseClient = new GitHubReleaseClient(
            _httpClient,
            RepositoryOwner,
            RepositoryName);
        string appVersion = typeof(MainForm).Assembly.GetName().Version?.ToString(3)
            ?? "0.1.3";
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ScrapMechanicModManager", appVersion));

        InitializeUi();
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
        Text = "Scrap Mechanic Mod Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        ClientSize = new Size(900, 620);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(242, 244, 247);

        var title = new Label
        {
            Text = "Scrap Mechanic Mod Manager",
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            AutoSize = true,
        };
        var subtitle = new Label
        {
            Text = "Közös Survival Lua fájlok biztonságos telepítése és frissítése",
            ForeColor = Color.DimGray,
            AutoSize = true,
        };

        var pathLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
        };
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathLayout.Controls.Add(new Label
        {
            Text = "Scrap Mechanic mappa:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 8, 0),
        }, 0, 0);
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
            RowCount = 8,
            Padding = new Padding(22),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(pathLayout, 0, 3);
        root.Controls.Add(statusPanel, 0, 4);
        root.Controls.Add(actions, 0, 5);
        root.Controls.Add(_progress, 0, 6);
        root.Controls.Add(_log, 0, 7);
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
    }

    private async Task AutoDetectAsync()
    {
        ManagerSettings? settings = await LoadSettingsAsync();
        if (!string.IsNullOrWhiteSpace(settings?.GameRoot)
            && Directory.Exists(settings.GameRoot))
        {
            _gameRoot.Text = settings.GameRoot;
            _selectedInstallation = ResolveSelectedInstallation(settings.GameRoot);
            ShowLocalGameStatus();
            Log("Mentett játékútvonal betöltve.");
            return;
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
            await SaveSettingsAsync(installation.GameRoot);
            Log($"Steam telepítés automatikusan megtalálva: {installation.GameRoot}");
            return;
        }

        _gameStatus.Text = "Játék: nem található automatikusan";
        Log("A Scrap Mechanic nem található automatikusan. Használd a Tallózás gombot.");
    }

    private void BrowseForGameRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Válaszd ki a Scrap Mechanic gyökérmappáját",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_gameRoot.Text) ? _gameRoot.Text : string.Empty,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _gameRoot.Text = dialog.SelectedPath;
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
            throw new InvalidOperationException("Nincs visszaállítható backup snapshot.");
        }

        DialogResult answer = MessageBox.Show(
            this,
            $"Visszaállítod ezt a mentést?\n\n{latestSnapshot}",
            "Backup visszaállítása",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

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
        string executable = Path.Combine(installation.GameRoot, "Release", "ScrapMechanic.exe");
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
            StringComparer.OrdinalIgnoreCase);
        DirectoryInfo? library = Directory.GetParent(normalizedGameRoot)?.Parent?.Parent;
        if (library is not null) roots.Add(library.FullName);

        SteamInstallation? match = roots
            .SelectMany(root => _steamLibraryLocator.FindInstallations(root))
            .FirstOrDefault(installation => string.Equals(
                Path.GetFullPath(installation.GameRoot),
                normalizedGameRoot,
                StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException(
            "A kiválasztott mappához nem található érvényes appmanifest_387990.acf. " +
            "A Scrap Mechanic Steam telepítési gyökérmappáját add meg.");
    }

    private void ShowLocalGameStatus()
    {
        if (_selectedInstallation is null) return;
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
                Log("A steam.exe nem található; a steam:// indítás nem tudja átadni a -dev kapcsolót.");
            }
            Process.Start(new ProcessStartInfo("steam://rungameid/387990")
            {
                UseShellExecute = true,
            });
        }
        Log($"Játékindítás kérése: {installation.GameRoot}");
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
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"A manifest target kilép a game rootból: {relativePath}");
        }
        return target;
    }

    private string RequireGameRoot()
    {
        string value = _gameRoot.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Add meg a Scrap Mechanic útvonalát.");
        }
        return value;
    }

    private async Task<bool> EnsureElevatedForWriteAsync()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator)) return true;

        await SaveSettingsAsync(RequireGameRoot());
        DialogResult answer = MessageBox.Show(
            this,
            "A Steam játékmappa módosításához rendszergazdai jogosultság kellhet. " +
            "Újraindítsam a Mod Managert rendszergazdaként? A műveletet utána újra meg kell nyomnod.",
            "Rendszergazdai jogosultság",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (answer != DialogResult.Yes) return false;

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
            Log("Az emelt jogosultságú újraindítás megszakadt: " + error.Message);
            return false;
        }
    }

    private static void EnsureGameIsNotRunning()
    {
        if (Process.GetProcessesByName("ScrapMechanic").Length > 0)
        {
            throw new InvalidOperationException(
                "A Scrap Mechanic fut. Zárd be a játékot telepítés vagy restore előtt.");
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
            Log("A művelet megszakítva.");
        }
        catch (Exception error)
        {
            Log("HIBA: " + error.Message);
            MessageBox.Show(
                this,
                error.Message,
                "Scrap Mechanic Mod Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _progress.Visible = busy;
        _browse.Enabled = !busy;
        _check.Enabled = !busy;
        _install.Enabled = !busy;
        _restore.Enabled = !busy;
        _launch.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void Log(string message)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static async Task<ManagerSettings?> LoadSettingsAsync()
    {
        if (!File.Exists(SettingsPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<ManagerSettings>(
                await File.ReadAllTextAsync(SettingsPath));
        }
        catch
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
