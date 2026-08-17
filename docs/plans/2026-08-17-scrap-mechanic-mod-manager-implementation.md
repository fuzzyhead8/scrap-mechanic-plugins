# Scrap Mechanic Mod Manager Implementation Plan

> **REQUIRED SUB-SKILL:** Use the executing-plans skill to implement this plan task-by-task.

**Goal:** Build a safe self-updating Windows launcher for shared Scrap Mechanic 1.0 Survival Lua files, with local Obsidian project tracking.

**Architecture:** A testable `net8.0` Core library handles Steam/game discovery, compatibility, GitHub release processing, and transactional installation. A thin `net8.0-windows` WinForms application provides the UI, while public GitHub Releases provide the manifest and payload.

**Tech Stack:** C# 12, .NET 8, WinForms, xUnit, PowerShell, GitHub Actions, Obsidian MCP.

---

### Task 1: Solution and compatibility models

**Files:**
- Create: `ScrapMechanicModManager.sln`
- Create: `src/ScrapMechanicModManager.Core/ScrapMechanicModManager.Core.csproj`
- Create: `tests/ScrapMechanicModManager.Tests/ScrapMechanicModManager.Tests.csproj`
- Test: `tests/ScrapMechanicModManager.Tests/GameInstallValidatorTests.cs`

1. Write a test that rejects a missing Scrap Mechanic 1.0 directory structure.
2. Run it and verify RED.
3. Implement the minimal models and validator.
4. Run it and verify GREEN.

### Task 2: Steam Library and AppManifest discovery

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Steam/SteamLibraryLocator.cs`
- Create: `src/ScrapMechanicModManager.Core/Steam/SteamAppManifest.cs`
- Test: `tests/ScrapMechanicModManager.Tests/SteamLibraryLocatorTests.cs`

1. Test multi-library discovery with temporary `libraryfolders.vdf` and `appmanifest_387990.acf` files.
2. After RED, implement a focused dependency-free VDF parser.
3. Validate AppID, `installdir`, `buildid`, and `StateFlags`.

### Task 3: Release manifest and integrity

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Updates/ModManifest.cs`
- Create: `src/ScrapMechanicModManager.Core/Updates/GitHubReleaseClient.cs`
- Create: `src/ScrapMechanicModManager.Core/Security/HashService.cs`
- Test: `tests/ScrapMechanicModManager.Tests/ManifestTests.cs`

1. Test manifest schema, asset name, build list, and hash validation.
2. Test GitHub latest-release asset resolution with a fake HTTP handler.
3. Implement the minimal client and SHA-256 verification.

### Task 4: Backup, installation, and restore

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Installation/ModInstaller.cs`
- Create: `src/ScrapMechanicModManager.Core/Installation/BackupStore.cs`
- Create: `src/ScrapMechanicModManager.Core/Installation/ZipPayloadValidator.cs`
- Test: `tests/ScrapMechanicModManager.Tests/ModInstallerTests.cs`

1. Test backup-before-write with a temporary game root.
2. Test rejection of ZIP path traversal, incorrect hashes, and partial payloads.
3. Test restore and unchanged targets after failure.
4. Implement staging and atomic replacement.

### Task 5: WinForms launcher

**Files:**
- Create: `src/ScrapMechanicModManager/ScrapMechanicModManager.csproj`
- Create: `src/ScrapMechanicModManager/Program.cs`
- Create: `src/ScrapMechanicModManager/MainForm.cs`
- Create: `src/ScrapMechanicModManager/app.manifest`

1. Build a thin UI over tested Core services.
2. Add path browsing, status, installation, restore, launch, and optional `-dev` mode.
3. Present failures through readable dialogs and logs.

### Task 6: Release packaging

**Files:**
- Create: `distribution/manifest.template.json`
- Create: `scripts/New-ReleasePayload.ps1`
- Create: `.github/workflows/release.yml`
- Modify: `README.md`

1. Build a testable manifest/payload generator around the unchanged `robots_01.zip`.
2. Publish a self-contained `win-x64` launcher and release assets on tag pushes.
3. Document that local builds require no GitHub authentication, while publishing releases does.

### Task 7: MCP and local Obsidian tracking

**Files:**
- Modify locally: `.mcp.json`
- Create in private vault: `1_Projects/Scrap_Mechanic_Plugins/**`
- Create in private vault: `Scrap Mechanic Plugins Kanban.md`

1. Configure the generic local `web-tools` MCP script.
2. Create project indexes, conventions, protocols, and task notes.
3. Create a Kanban board with one current task.
4. Read back and verify frontmatter and wikilinks through Obsidian MCP.

Local agent/MCP configuration and the private vault were later removed from public tracking.

### Task 8: Final verification

1. Run `dotnet test ScrapMechanicModManager.sln`.
2. Run `dotnet build ScrapMechanicModManager.sln -c Release`.
3. Run a self-contained publish smoke build.
4. Verify ZIP hash and contents.
5. Verify local Obsidian notes and Kanban settings.
6. Search the repository for inherited project references.
