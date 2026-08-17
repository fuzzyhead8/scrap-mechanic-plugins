# Linux Avalonia Launcher Implementation Plan

> Execute with TDD. Keep the Windows WinForms launcher releasable after every task. Do not modify `robots_01.zip`.

**Goal:** Publish a self-contained graphical `linux-x64` launcher that safely manages Steam Proton Scrap Mechanic installations through the existing Core pipeline.

**Architecture:** Add deterministic Linux platform services to Core and a separate Avalonia desktop frontend. Keep WinForms intact. Extend release CI with Ubuntu tests and a Linux archive.

**Technology:** .NET 8, Avalonia 11.3.20, xUnit, GitHub Actions, PowerShell/bash packaging. Avalonia 11 is pinned because Avalonia 12.1.1 analyzers require a newer Roslyn compiler than the repository's .NET 8 SDK.

---

## Task 1: Linux Steam root discovery

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Steam/ISteamRootDiscovery.cs`
- Create: `src/ScrapMechanicModManager.Core/Steam/LinuxSteamRootDiscovery.cs`
- Modify: `src/ScrapMechanicModManager.Core/Steam/SteamRootDiscovery.cs`
- Create: `tests/ScrapMechanicModManager.Tests/LinuxSteamRootDiscoveryTests.cs`

1. Write failing tests for native, legacy, Flatpak, missing, duplicate, and case-sensitive roots.
2. Add the discovery interface and Linux implementation with injectable home path and directory predicate.
3. Make the existing Windows discovery implement the interface without changing its behavior.
4. Run the focused tests and then the Steam test group.

## Task 2: Linux process and Steam launch services

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Platform/IGamePlatformService.cs`
- Create: `src/ScrapMechanicModManager.Core/Platform/LinuxGamePlatformService.cs`
- Create: `tests/ScrapMechanicModManager.Tests/LinuxGamePlatformServiceTests.cs`

1. Write failing tests for native Steam, Flatpak Steam, `-dev`, process-name matching, and `/proc` command-line matching.
2. Implement pure command construction and injectable process/filesystem adapters.
3. Ensure no `sudo`, shell interpolation, or automatic elevation is possible.
4. Run focused tests.

## Task 3: Cross-platform validation and path behavior

**Files:**
- Modify: `src/ScrapMechanicModManager.Core/Steam/SteamLibraryLocator.cs`
- Modify: `src/ScrapMechanicModManager.Core/Validation/ExecutableVersionReader.cs` only if Linux CI proves a failure
- Modify: relevant tests

1. Add Linux-style VDF paths and case-sensitive library fixtures.
2. Verify `FileVersionInfo` against a Windows PE fixture on Ubuntu CI.
3. Make the minimum correction required by failing tests.
4. Run all Core tests on Windows and Ubuntu.

## Task 4: Avalonia desktop frontend

**Files:**
- Create: `src/ScrapMechanicModManager.Desktop/ScrapMechanicModManager.Desktop.csproj`
- Create: `src/ScrapMechanicModManager.Desktop/Program.cs`
- Create: `src/ScrapMechanicModManager.Desktop/App.axaml`
- Create: `src/ScrapMechanicModManager.Desktop/App.axaml.cs`
- Create: `src/ScrapMechanicModManager.Desktop/MainWindow.axaml`
- Create: `src/ScrapMechanicModManager.Desktop/MainWindow.axaml.cs`
- Copy: `src/ScrapMechanicModManager.Desktop/Assets/ScrapMechanicModManager.png`
- Modify: `ScrapMechanicModManager.sln`

1. Add a failing project contract test for the Avalonia project, Core reference, Linux runtime, and icon.
2. Create the Avalonia project pinned to version `11.3.20`.
3. Implement the approved GUI and wire it to Core and Linux platform services.
4. Preserve cancellation, timeouts, game-running guard, hash verification, backup, restore, and cache invalidation behavior.
5. Build and launch a desktop smoke process where the host supports it.

## Task 5: Linux packaging and release workflow

**Files:**
- Create: `scripts/New-LinuxReleasePackage.ps1`
- Create: `distribution/linux/scrap-mechanic-mod-manager`
- Create: `distribution/linux/scrap-mechanic-mod-manager.desktop`
- Modify: `.github/workflows/release.yml`
- Modify: `README.md`
- Create/modify: release contract tests

1. Write failing tests for archive name, executable, icon, launcher script, desktop entry, and unchanged payload hash.
2. Publish self-contained `linux-x64` output and package it as `ScrapMechanicModManager-linux-x64.tar.gz`.
3. Add Ubuntu test/publish jobs and include the archive in GitHub Release assets.
4. Document required Linux libraries, supported Steam roots, extraction, permissions, and the runtime-test limitation.

## Task 6: Verification and rollout status

1. Run `dotnet test ScrapMechanicModManager.sln -c Release`.
2. Run clean Windows Release build and `win-x64` publish.
3. Run `linux-x64` Avalonia publish.
4. Validate archive contents, hashes, and `git diff --check`.
5. Confirm `robots_01.zip` remains 1718 bytes with SHA-256 `D429E6C0A812346F375DC863573A731F95BB0354834CD4BE552D90EC32217767`.
6. Update Obsidian Task 10 with automated evidence and leave real Proton runtime acceptance unchecked.
7. Request code review before release.
