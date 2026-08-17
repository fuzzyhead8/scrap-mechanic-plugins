using System.Globalization;

namespace ScrapMechanicModManager.Core.Localization;

public sealed class AppLocalizer
{
    public const AppLanguage DefaultLanguage = AppLanguage.Hungarian;

    private static readonly IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<TextKey, string>> Catalog =
        new Dictionary<AppLanguage, IReadOnlyDictionary<TextKey, string>>
        {
            [AppLanguage.Hungarian] = CreateTranslations(
                (TextKey.AppTitle, "Scrap Mechanic Mod Manager"),
                (TextKey.AppHeader, "Scrap Mechanic Mod Manager"),
                (TextKey.AppSubtitle, "Közös Survival Lua fájlok biztonságos telepítése és frissítése"),
                (TextKey.AppSubtitleLinux, "Közös Survival Lua fájlok biztonságos telepítése és frissítése Linuxon, Steam Proton alatt"),
                (TextKey.GameRootLabel, "Scrap Mechanic mappa:"),
                (TextKey.GameRootWatermarkLinux, "~/.local/share/Steam/steamapps/common/Scrap Mechanic"),
                (TextKey.ButtonBrowse, "Tallózás..."),
                (TextKey.ButtonCheck, "Ellenőrzés"),
                (TextKey.ButtonInstallUpdate, "Telepítés / frissítés"),
                (TextKey.ButtonRestore, "Visszaállítás"),
                (TextKey.ButtonLaunchGame, "Játék indítása"),
                (TextKey.CheckBoxDevMode, "Indítás -dev módban"),
                (TextKey.LanguageLabel, "Nyelv:"),
                (TextKey.LanguageHungarian, "Magyar"),
                (TextKey.LanguageEnglish, "English"),
                (TextKey.LinuxPreviewFooter, "Linux előnézet · X11/XWayland · Steam Proton"),
                (TextKey.DialogButtonOk, "OK"),
                (TextKey.DialogButtonCancel, "Mégse"),
                (TextKey.DialogButtonRestore, "Visszaállítás"),
                (TextKey.DialogButtonYes, "Igen"),
                (TextKey.DialogButtonNo, "Nem"),
                (TextKey.GameStatusNotChecked, "Játék: nincs ellenőrizve"),
                (TextKey.ModStatusNotChecked, "Mod: nincs ellenőrizve"),
                (TextKey.GameStatusNotFoundAutomatically, "Játék: nem található automatikusan"),
                (TextKey.GameStatusPathProvidedNeedsCheck, "Játék: útvonal megadva, ellenőrzés szükséges"),
                (TextKey.GameStatusReady, "Játék: Scrap Mechanic {0} · Steam build {1}"),
                (TextKey.GameStatusValidationErrors, "Játék: {0}"),
                (TextKey.GameStatusInvalid, "Játék: a telepítés nem érvényes"),
                (TextKey.ModStatusUpToDate, "Mod: naprakész ({0})"),
                (TextKey.ModStatusUpdateAvailable, "Mod: telepítés/frissítés elérhető ({0})"),
                (TextKey.ModStatusInstalled, "Mod: telepítve ({0})"),
                (TextKey.ModStatusBackupRestored, "Mod: backup visszaállítva"),
                (TextKey.LogSavedGameRootLoaded, "Mentett játékútvonal betöltve."),
                (TextKey.LogSavedGameRootInvalid, "A mentett játékútvonal már nem érvényes: {0}"),
                (TextKey.LogAutoDetectedSteamInstall, "Steam telepítés automatikusan megtalálva: {0}"),
                (TextKey.LogAutoDetectedSteamProtonInstall, "Steam Proton telepítés automatikusan megtalálva: {0}"),
                (TextKey.LogAutoDetectFailedUseBrowse, "A Scrap Mechanic nem található automatikusan. Használd a Tallózás gombot."),
                (TextKey.LogLatestRelease, "Latest release: {0}; támogatott build: {1}."),
                (TextKey.LogPayloadDownload, "Payload letöltése: {0}"),
                (TextKey.LogInstalledFiles, "Telepítve: {0} fájl."),
                (TextKey.LogBackupDirectory, "Backup: {0}"),
                (TextKey.LogScriptCacheInvalidated, "A core_data.cbo script-cache backupolva és invalidálva."),
                (TextKey.LogBackupRestored, "Backup visszaállítva: {0}"),
                (TextKey.LogSteamExeDevModeUnavailable, "A steam.exe nem található; a steam:// indítás nem tudja átadni a -dev kapcsolót."),
                (TextKey.LogLaunchRequested, "Játékindítás kérése: {0}"),
                (TextKey.LogOperationCanceled, "A művelet megszakítva."),
                (TextKey.LogError, "HIBA: {0}"),
                (TextKey.LogElevatedRestartCanceled, "Az emelt jogosultságú újraindítás megszakadt: {0}"),
                (TextKey.LogLanguageChanged, "Nyelv átállítva: {0}."),
                (TextKey.DialogSelectGameRootTitle, "Válaszd ki a Scrap Mechanic gyökérmappáját"),
                (TextKey.DialogRestoreBackupTitle, "Backup visszaállítása"),
                (TextKey.DialogRestoreBackupMessage, "Visszaállítod ezt a mentést?\n\n{0}"),
                (TextKey.DialogAdministratorTitle, "Rendszergazdai jogosultság"),
                (TextKey.DialogAdministratorRestartMessage, "A Steam játékmappa módosításához rendszergazdai jogosultság kellhet. Újraindítsam a Mod Managert rendszergazdaként? A műveletet utána újra meg kell nyomnod."),
                (TextKey.DialogErrorTitle, "Scrap Mechanic Mod Manager"),
                (TextKey.ErrorNoBackupSnapshot, "Nincs visszaállítható backup snapshot."),
                (TextKey.ErrorSteamInstallNotReady, "A Steam telepítés nincs kész állapotban (StateFlags={0})."),
                (TextKey.ErrorInvalidAppManifestForSelectedFolder, "A kiválasztott mappához nem található érvényes appmanifest_387990.acf. A Scrap Mechanic Steam telepítési gyökérmappáját add meg."),
                (TextKey.ErrorUnsafeManifestTarget, "Nem biztonságos manifest target: {0}"),
                (TextKey.ErrorManifestTargetEscapesGameRoot, "A manifest target kilép a game rootból: {0}"),
                (TextKey.ErrorMissingGameRoot, "Add meg a Scrap Mechanic útvonalát."),
                (TextKey.ErrorGameRunning, "A Scrap Mechanic fut. Zárd be a játékot telepítés vagy restore előtt."),
                (TextKey.ErrorSteamGameDirectoryNotWritable, "A Steam játékmappa nem írható. Ellenőrizd a könyvtár tulajdonosát és jogosultságait; ne futtasd a launchert sudo-val."),
                (TextKey.ErrorLatestReleaseUnavailable, "A legfrissebb kiadás nem érhető el. Ellenőrizd a hálózati kapcsolatot és próbáld újra."),
                (TextKey.ErrorPayloadDownloadFailed, "A payload letöltése sikertelen. Ellenőrizd a hálózati kapcsolatot és próbáld újra."),
                (TextKey.ErrorPermissionDenied, "Nincs jogosultság a művelet végrehajtásához."),
                (TextKey.ErrorOperationFailed, "A művelet nem sikerült."),
                (TextKey.ErrorGameValidationFailed, "A Scrap Mechanic telepítés ellenőrzése sikertelen.")),
            [AppLanguage.English] = CreateTranslations(
                (TextKey.AppTitle, "Scrap Mechanic Mod Manager"),
                (TextKey.AppHeader, "Scrap Mechanic Mod Manager"),
                (TextKey.AppSubtitle, "Safe installation and updates for shared Survival Lua files"),
                (TextKey.AppSubtitleLinux, "Safe installation and updates for shared Survival Lua files on Linux with Steam Proton"),
                (TextKey.GameRootLabel, "Scrap Mechanic folder:"),
                (TextKey.GameRootWatermarkLinux, "~/.local/share/Steam/steamapps/common/Scrap Mechanic"),
                (TextKey.ButtonBrowse, "Browse..."),
                (TextKey.ButtonCheck, "Check"),
                (TextKey.ButtonInstallUpdate, "Install / update"),
                (TextKey.ButtonRestore, "Restore"),
                (TextKey.ButtonLaunchGame, "Launch game"),
                (TextKey.CheckBoxDevMode, "Launch in -dev mode"),
                (TextKey.LanguageLabel, "Language:"),
                (TextKey.LanguageHungarian, "Magyar"),
                (TextKey.LanguageEnglish, "English"),
                (TextKey.LinuxPreviewFooter, "Linux preview · X11/XWayland · Steam Proton"),
                (TextKey.DialogButtonOk, "OK"),
                (TextKey.DialogButtonCancel, "Cancel"),
                (TextKey.DialogButtonRestore, "Restore"),
                (TextKey.DialogButtonYes, "Yes"),
                (TextKey.DialogButtonNo, "No"),
                (TextKey.GameStatusNotChecked, "Game: not checked"),
                (TextKey.ModStatusNotChecked, "Mod: not checked"),
                (TextKey.GameStatusNotFoundAutomatically, "Game: not found automatically"),
                (TextKey.GameStatusPathProvidedNeedsCheck, "Game: path provided, check required"),
                (TextKey.GameStatusReady, "Game: Scrap Mechanic {0} · Steam build {1}"),
                (TextKey.GameStatusValidationErrors, "Game: {0}"),
                (TextKey.GameStatusInvalid, "Game: the installation is invalid"),
                (TextKey.ModStatusUpToDate, "Mod: up to date ({0})"),
                (TextKey.ModStatusUpdateAvailable, "Mod: install/update available ({0})"),
                (TextKey.ModStatusInstalled, "Mod: installed ({0})"),
                (TextKey.ModStatusBackupRestored, "Mod: backup restored"),
                (TextKey.LogSavedGameRootLoaded, "Saved game path loaded."),
                (TextKey.LogSavedGameRootInvalid, "The saved game path is no longer valid: {0}"),
                (TextKey.LogAutoDetectedSteamInstall, "Steam installation found automatically: {0}"),
                (TextKey.LogAutoDetectedSteamProtonInstall, "Steam Proton installation found automatically: {0}"),
                (TextKey.LogAutoDetectFailedUseBrowse, "Scrap Mechanic could not be found automatically. Use the Browse button."),
                (TextKey.LogLatestRelease, "Latest release: {0}; supported build: {1}."),
                (TextKey.LogPayloadDownload, "Downloading payload: {0}"),
                (TextKey.LogInstalledFiles, "Installed: {0} file(s)."),
                (TextKey.LogBackupDirectory, "Backup: {0}"),
                (TextKey.LogScriptCacheInvalidated, "The core_data.cbo script cache was backed up and invalidated."),
                (TextKey.LogBackupRestored, "Backup restored: {0}"),
                (TextKey.LogSteamExeDevModeUnavailable, "steam.exe was not found; steam:// launch cannot pass the -dev switch."),
                (TextKey.LogLaunchRequested, "Game launch requested: {0}"),
                (TextKey.LogOperationCanceled, "The operation was canceled."),
                (TextKey.LogError, "ERROR: {0}"),
                (TextKey.LogElevatedRestartCanceled, "Elevated restart was canceled: {0}"),
                (TextKey.LogLanguageChanged, "Language changed to {0}."),
                (TextKey.DialogSelectGameRootTitle, "Select the Scrap Mechanic root folder"),
                (TextKey.DialogRestoreBackupTitle, "Restore backup"),
                (TextKey.DialogRestoreBackupMessage, "Restore this backup?\n\n{0}"),
                (TextKey.DialogAdministratorTitle, "Administrator rights"),
                (TextKey.DialogAdministratorRestartMessage, "Changing the Steam game folder may require administrator rights. Restart Mod Manager as administrator? You will need to press the operation again afterwards."),
                (TextKey.DialogErrorTitle, "Scrap Mechanic Mod Manager"),
                (TextKey.ErrorNoBackupSnapshot, "No restorable backup snapshot was found."),
                (TextKey.ErrorSteamInstallNotReady, "The Steam installation is not ready (StateFlags={0})."),
                (TextKey.ErrorInvalidAppManifestForSelectedFolder, "No valid appmanifest_387990.acf was found for the selected folder. Select the Scrap Mechanic Steam installation root folder."),
                (TextKey.ErrorUnsafeManifestTarget, "Unsafe manifest target: {0}"),
                (TextKey.ErrorManifestTargetEscapesGameRoot, "The manifest target escapes the game root: {0}"),
                (TextKey.ErrorMissingGameRoot, "Enter the Scrap Mechanic path."),
                (TextKey.ErrorGameRunning, "Scrap Mechanic is running. Close the game before installing or restoring."),
                (TextKey.ErrorSteamGameDirectoryNotWritable, "The Steam game folder is not writable. Check the directory owner and permissions; do not run the launcher with sudo."),
                (TextKey.ErrorLatestReleaseUnavailable, "The latest release is unavailable. Check the network connection and try again."),
                (TextKey.ErrorPayloadDownloadFailed, "Payload download failed. Check the network connection and try again."),
                (TextKey.ErrorPermissionDenied, "Permission denied for this operation."),
                (TextKey.ErrorOperationFailed, "The operation failed."),
                (TextKey.ErrorGameValidationFailed, "Scrap Mechanic installation validation failed.")),
        };

    private AppLanguage _language = DefaultLanguage;

    static AppLocalizer()
    {
        ValidateCatalog();
    }

    public AppLocalizer()
        : this(DefaultLanguage)
    {
    }

    public AppLocalizer(AppLanguage language)
    {
        Language = language;
    }

    public AppLanguage Language
    {
        get => _language;
        set => _language = IsDefinedLanguage(value) ? value : DefaultLanguage;
    }

    public string this[TextKey key] => Get(key);

    public string Get(TextKey key, params object?[] arguments)
    {
        if (!Catalog[Language].TryGetValue(key, out string? format)
            || string.IsNullOrWhiteSpace(format))
        {
            throw new KeyNotFoundException($"Missing {Language} translation for {key}.");
        }

        return arguments is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, format, arguments)
            : format;
    }

    public static AppLanguage ParseLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultLanguage;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "hu" or "hun" or "hungarian" => AppLanguage.Hungarian,
            "en" or "eng" or "english" => AppLanguage.English,
            _ => DefaultLanguage,
        };
    }

    private static IReadOnlyDictionary<TextKey, string> CreateTranslations(
        params (TextKey Key, string Text)[] translations)
    {
        Dictionary<TextKey, string> map = translations.ToDictionary(
            translation => translation.Key,
            translation => translation.Text);
        if (map.Count != translations.Length)
        {
            throw new InvalidOperationException("Duplicate localization keys are not allowed.");
        }
        return map;
    }

    private static void ValidateCatalog()
    {
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
        {
            if (!Catalog.TryGetValue(language, out IReadOnlyDictionary<TextKey, string>? translations))
            {
                throw new InvalidOperationException($"Missing catalog for {language}.");
            }

            foreach (TextKey key in Enum.GetValues<TextKey>())
            {
                if (!translations.TryGetValue(key, out string? text)
                    || string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException(
                        $"Missing {language} translation for {key}.");
                }
            }
        }
    }

    private static bool IsDefinedLanguage(AppLanguage language)
    {
        return Enum.IsDefined(typeof(AppLanguage), language);
    }
}
