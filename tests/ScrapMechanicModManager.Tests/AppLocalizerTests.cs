using System.Globalization;
using ScrapMechanicModManager.Core.Localization;

namespace ScrapMechanicModManager.Tests;

public sealed class AppLocalizerTests
{
    [Fact]
    public void Hungarian_is_the_default_language()
    {
        var defaultLocalizer = new AppLocalizer();
        var hungarianLocalizer = new AppLocalizer(AppLanguage.Hungarian);
        var englishLocalizer = new AppLocalizer(AppLanguage.English);

        Assert.Equal(AppLanguage.Hungarian, defaultLocalizer.Language);
        Assert.Equal(
            hungarianLocalizer.Get(TextKey.ButtonBrowse),
            defaultLocalizer.Get(TextKey.ButtonBrowse));
        Assert.NotEqual(
            englishLocalizer.Get(TextKey.AppSubtitle),
            defaultLocalizer.Get(TextKey.AppSubtitle));
    }

    [Fact]
    public void Language_can_switch_to_English_for_typed_key_lookup()
    {
        var localizer = new AppLocalizer();

        localizer.Language = AppLanguage.English;

        Assert.Equal("Browse...", localizer.Get(TextKey.ButtonBrowse));
    }

    [Theory]
    [InlineData(null, AppLanguage.Hungarian)]
    [InlineData("", AppLanguage.Hungarian)]
    [InlineData("   ", AppLanguage.Hungarian)]
    [InlineData("zz", AppLanguage.Hungarian)]
    [InlineData("hu", AppLanguage.Hungarian)]
    [InlineData("Hungarian", AppLanguage.Hungarian)]
    [InlineData("en", AppLanguage.English)]
    [InlineData("EN", AppLanguage.English)]
    [InlineData("English", AppLanguage.English)]
    public void Language_parsing_is_safe(string? value, AppLanguage expected)
    {
        Assert.Equal(expected, AppLocalizer.ParseLanguage(value));
    }

    [Fact]
    public void Formatting_uses_invariant_culture_for_arguments()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("hu-HU");
            CultureInfo.CurrentUICulture = new CultureInfo("hu-HU");
            var localizer = new AppLocalizer(AppLanguage.English);

            string formatted = localizer.Get(
                TextKey.LogAutoDetectedSteamInstall,
                1.5);

            Assert.Contains("1.5", formatted);
            Assert.DoesNotContain("1,5", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Unexpected_failure_keeps_a_localized_message_and_a_technical_code()
    {
        var hungarian = new AppLocalizer(AppLanguage.Hungarian);
        var english = new AppLocalizer(AppLanguage.English);

        Assert.Contains(
            "InvalidDataException",
            hungarian.Get(TextKey.ErrorOperationFailed, "InvalidDataException"));
        Assert.Contains(
            "InvalidDataException",
            english.Get(TextKey.ErrorOperationFailed, "InvalidDataException"));
    }

    [Fact]
    public void Every_text_key_has_nonempty_Hungarian_and_English_translation()
    {
        foreach (TextKey key in Enum.GetValues<TextKey>())
        {
            foreach (AppLanguage language in new[]
                     {
                         AppLanguage.Hungarian,
                         AppLanguage.English,
                     })
            {
                string text = new AppLocalizer(language).Get(key);
                Assert.False(
                    string.IsNullOrWhiteSpace(text),
                    $"Missing {language} translation for {key}.");
            }
        }
    }

    [Fact]
    public void Catalog_defines_keys_needed_by_current_launchers()
    {
        TextKey[] requiredKeys =
        [
            TextKey.AppTitle,
            TextKey.AppHeader,
            TextKey.AppSubtitle,
            TextKey.AppSubtitleLinux,
            TextKey.GameRootLabel,
            TextKey.GameRootWatermarkLinux,
            TextKey.ButtonBrowse,
            TextKey.ButtonCheck,
            TextKey.ButtonInstallUpdate,
            TextKey.ButtonRestore,
            TextKey.ButtonLaunchGame,
            TextKey.CheckBoxDevMode,
            TextKey.LanguageLabel,
            TextKey.LanguageHungarian,
            TextKey.LanguageEnglish,
            TextKey.LinuxPreviewFooter,
            TextKey.DialogButtonOk,
            TextKey.DialogButtonCancel,
            TextKey.DialogButtonRestore,
            TextKey.DialogButtonYes,
            TextKey.DialogButtonNo,
            TextKey.GameStatusNotChecked,
            TextKey.ModStatusNotChecked,
            TextKey.GameStatusNotFoundAutomatically,
            TextKey.GameStatusPathProvidedNeedsCheck,
            TextKey.GameStatusReady,
            TextKey.GameStatusValidationErrors,
            TextKey.ModStatusUpToDate,
            TextKey.ModStatusUpdateAvailable,
            TextKey.ModStatusInstalled,
            TextKey.ModStatusBackupRestored,
            TextKey.LogSavedGameRootLoaded,
            TextKey.LogSavedGameRootInvalid,
            TextKey.LogAutoDetectedSteamInstall,
            TextKey.LogAutoDetectedSteamProtonInstall,
            TextKey.LogAutoDetectFailedUseBrowse,
            TextKey.LogLatestRelease,
            TextKey.LogPayloadDownload,
            TextKey.LogInstalledFiles,
            TextKey.LogBackupDirectory,
            TextKey.LogScriptCacheInvalidated,
            TextKey.LogBackupRestored,
            TextKey.LogSteamExeDevModeUnavailable,
            TextKey.LogLaunchRequested,
            TextKey.LogOperationCanceled,
            TextKey.LogError,
            TextKey.LogElevatedRestartCanceled,
            TextKey.LogLanguageChanged,
            TextKey.DialogSelectGameRootTitle,
            TextKey.DialogRestoreBackupTitle,
            TextKey.DialogRestoreBackupMessage,
            TextKey.DialogAdministratorTitle,
            TextKey.DialogAdministratorRestartMessage,
            TextKey.DialogErrorTitle,
            TextKey.ErrorNoBackupSnapshot,
            TextKey.ErrorSteamInstallNotReady,
            TextKey.ErrorInvalidAppManifestForSelectedFolder,
            TextKey.ErrorUnsafeManifestTarget,
            TextKey.ErrorManifestTargetEscapesGameRoot,
            TextKey.ErrorMissingGameRoot,
            TextKey.ErrorGameRunning,
            TextKey.ErrorSteamGameDirectoryNotWritable,
            TextKey.ErrorLatestReleaseUnavailable,
            TextKey.ErrorPayloadDownloadFailed,
            TextKey.ErrorPermissionDenied,
            TextKey.ErrorOperationFailed,
        ];

        Assert.Equal(requiredKeys.Length, requiredKeys.Distinct().Count());
        Assert.True(requiredKeys.Length >= 60);
        foreach (TextKey key in requiredKeys)
        {
            Assert.True(Enum.IsDefined(key), $"Missing key {key}.");
        }
    }

    [Fact]
    public void LocalizedMessage_stores_key_and_arguments_and_renders_through_localizer()
    {
        object?[] arguments = ["C:/backup"];
        var message = new LocalizedMessage(TextKey.LogBackupDirectory, arguments);
        arguments[0] = "mutated";

        Assert.Equal(TextKey.LogBackupDirectory, message.Key);
        object? argument = Assert.Single(message.Arguments);
        Assert.Equal("C:/backup", argument);

        var localizer = new AppLocalizer(AppLanguage.English);
        Assert.Equal("Backup: C:/backup", message.Render(localizer));

        localizer.Language = AppLanguage.Hungarian;
        Assert.Equal(
            localizer.Get(TextKey.LogBackupDirectory, "C:/backup"),
            message.Render(localizer));
    }
}
