# Bilingual UI and English Public Repository Implementation Plan

> **REQUIRED SUB-SKILL:** Use the executing-plans skill to implement this plan task-by-task.

**Goal:** Add persistent HU/EN localization to both graphical launchers and convert current public technical content to English.

**Architecture:** A typed localization catalog and atomic settings store live in the shared Core project. WinForms and Avalonia retain localized status/log models and reapply text immediately when the saved language changes. Public prose and technical source messages become English, except intentional Hungarian translations.

**Tech Stack:** .NET 8, C#, WinForms, Avalonia 11.3.20, System.Text.Json, xUnit, PowerShell, GitHub Actions.

---

### Task 1: Localization contract

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Localization/AppLanguage.cs`
- Create: `src/ScrapMechanicModManager.Core/Localization/TextKey.cs`
- Create: `src/ScrapMechanicModManager.Core/Localization/AppLocalizer.cs`
- Create: `src/ScrapMechanicModManager.Core/Localization/LocalizedMessage.cs`
- Test: `tests/ScrapMechanicModManager.Tests/AppLocalizerTests.cs`

**Steps:**
1. Write tests requiring Hungarian by default, English switching, formatting, full key coverage, and safe unknown-language parsing.
2. Run the focused tests and verify failure because the localization API does not exist.
3. Implement the minimal typed bilingual catalog.
4. Run focused tests and verify they pass.
5. Refactor duplicate translation-map construction without changing behavior.

### Task 2: Shared persistent settings

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Settings/ManagerSettings.cs`
- Create: `src/ScrapMechanicModManager.Core/Settings/ManagerSettingsStore.cs`
- Test: `tests/ScrapMechanicModManager.Tests/ManagerSettingsStoreTests.cs`

**Steps:**
1. Write tests for missing-file HU default, EN round trip, `gameRoot` preservation, malformed JSON fallback, and no temporary-file residue.
2. Run focused tests and verify failure because the store does not exist.
3. Implement asynchronous load/save with atomic replacement and no elevation.
4. Run focused tests and verify they pass.

### Task 3: WinForms localization

**Files:**
- Modify: `src/ScrapMechanicModManager/MainForm.cs`
- Test: `tests/ScrapMechanicModManager.Tests/WinFormsLocalizationContractTests.cs`

**Steps:**
1. Write source-contract tests requiring the language selector, shared localizer/settings store, and absence of hard-coded Hungarian UI strings outside the catalog.
2. Run focused tests and verify the expected contract failures.
3. Add the top-right selector and named static controls.
4. Replace UI/status/log/dialog strings with keys and structured messages.
5. Load language before first visible refresh and save selector changes immediately.
6. Run focused and existing launcher tests.

### Task 4: Avalonia localization

**Files:**
- Modify: `src/ScrapMechanicModManager.Desktop/MainWindow.axaml`
- Modify: `src/ScrapMechanicModManager.Desktop/MainWindow.axaml.cs`
- Test: `tests/ScrapMechanicModManager.Tests/LinuxDesktopProjectTests.cs`

**Steps:**
1. Extend source-contract tests for the named top-right language selector and shared localization services.
2. Run focused tests and verify failure.
3. Remove static user-facing text from XAML, then apply localized text from code.
4. Replace statuses, logs, dialogs, and errors with structured localized messages.
5. Persist language and rerender immediately.
6. Run focused tests and build the Avalonia project.

### Task 5: English technical source and public documentation

**Files:**
- Modify: `src/ScrapMechanicModManager.Core/Installation/ModInstaller.cs`
- Modify: `src/ScrapMechanicModManager.Core/Validation/GameInstallValidator.cs`
- Modify: `src/ScrapMechanicModManager.Core/Validation/ExecutableVersionReader.cs`
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/plans/2026-08-17-scrap-mechanic-mod-manager-design.md`
- Modify: `docs/plans/2026-08-17-scrap-mechanic-mod-manager-implementation.md`
- Test: `tests/ScrapMechanicModManager.Tests/PublicLanguageContractTests.cs`

**Steps:**
1. Write an audit test that rejects Hungarian accented characters in tracked public technical files, excluding intentional translation values and ignored local configuration.
2. Run it and verify failure against the current files.
3. Convert Core technical exceptions, comments, README, project router, and historical tracked plans to English.
4. Rerun the audit and existing message-sensitive tests; update expected English technical messages where required.

### Task 6: Full verification and release readiness

**Files:**
- Modify: `E:/Repos/obsidian-vault/1_Projects/Scrap_Mechanic_Plugins/tasks/2026-08-17-task-11-bilingual-ui-english-github.md`
- Modify: `E:/Repos/obsidian-vault/Scrap Mechanic Plugins Kanban.md`

**Steps:**
1. Run `dotnet test ScrapMechanicModManager.sln -c Release`.
2. Run warning-free solution build and both self-contained publishes.
3. Run Windows and Linux packaging tests and `npm test`.
4. Run `dotnet list package --vulnerable --include-transitive` for all projects.
5. Run `git diff --check`, tracked-local-config checks, Hungarian public-language audit, and `robots_01.zip` SHA-256 verification.
6. Smoke the Windows GUI language selector locally where available; explicitly report any unavailable Linux GUI/Proton runtime evidence.
7. Update the Obsidian task and Kanban with proven results only.
8. Request code review, address findings with TDD, and commit the verified implementation.
