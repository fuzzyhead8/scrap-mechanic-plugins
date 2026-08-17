# Linux Avalonia Launcher Design

**Date:** 2026-08-17
**Status:** Approved

## Goal

Add a graphical Linux launcher for Steam Proton installations of Scrap Mechanic while preserving the tested Windows WinForms launcher and the existing robot-loot payload.

## Non-goals

- Replacing or rewriting the working WinForms launcher in this task.
- Changing any loot rule in `robots_01.zip`.
- Claiming Linux runtime support before a real Steam Proton smoke test.
- Running the launcher as root or automatically invoking `sudo`.
- Supporting native Wayland before Avalonia's Wayland backend is stable enough for this project.

## Architecture

The solution keeps the current Windows frontend and adds an Avalonia desktop frontend:

- `ScrapMechanicModManager.Core` remains the shared `net8.0` library for manifest validation, hashing, Steam library parsing, game validation, backup, installation, cache invalidation, rollback, and restore.
- `ScrapMechanicModManager` remains the stable Windows-only WinForms frontend.
- `ScrapMechanicModManager.Desktop` is a new Avalonia frontend. Its first supported runtime is `linux-x64`.

Platform-specific discovery, process detection, and Steam launch behavior are isolated behind testable services. The Linux frontend calls the same Core installation and validation APIs as Windows.

## Linux Steam and Proton discovery

The launcher checks existing directories only and never creates candidate Steam roots while discovering them.

Supported default roots:

1. `~/.local/share/Steam`
2. `~/.steam/steam`
3. `~/.var/app/com.valvesoftware.Steam/.local/share/Steam` for Flatpak Steam

Each root is passed to the existing `SteamLibraryLocator`, which parses `steamapps/libraryfolders.vdf` and `appmanifest_387990.acf`. Linux path comparisons are case-sensitive.

The installed game remains a Windows build under Proton, so the expected game root still contains `Release/ScrapMechanic.exe` and the Scrap Mechanic 1.0 Survival structure.

## Linux platform behavior

- Running-game detection checks Linux process names and `/proc/*/cmdline` for `ScrapMechanic.exe`.
- Native Steam launch uses `steam -applaunch 387990`.
- Flatpak Steam launch uses `flatpak run com.valvesoftware.Steam -applaunch 387990`.
- Development mode appends `-dev`.
- Installation and restore stop on insufficient permissions and show a clear error. They never elevate automatically.
- Settings and backups use the user's local application-data directory.

## Avalonia UI

The first Linux UI provides feature parity with the existing launcher:

- automatic Steam/Proton discovery;
- manual game-root selection;
- local game/build/mod status;
- release check;
- install/update;
- restore latest backup;
- launch through Steam with optional `-dev`;
- progress state and visible logs.

The existing 512 px PNG icon is used for Linux desktop integration. Task 11 will add the shared HU/EN localization layer; this task may initially mirror the current Hungarian user-facing strings while all newly added code and public documentation remain English.

## Distribution

GitHub Actions runs tests on Windows and Ubuntu. Releases keep the existing Windows executable and add a self-contained `linux-x64` archive:

`ScrapMechanicModManager-linux-x64.tar.gz`

The archive contains the Avalonia executable, dependencies, icon, launcher script, and desktop entry. A `.deb` package can follow after the archive passes a real Proton smoke test.

Avalonia's documented Linux dependencies are:

`libx11-6 libice6 libsm6 libfontconfig1`

The stable initial display path is X11/XWayland.

## Safety and compatibility

All existing gates remain mandatory on Linux:

- AppID `387990`;
- ProductVersion major `1`;
- supported Steam build ID;
- expected Scrap Mechanic 1.0 paths;
- game not running;
- SHA-256 verification;
- staging and backup before writes;
- rollback on failure;
- targeted `Cache/Bundle/core_data.cbo` backup and invalidation only.

## Verification

Automated evidence:

- deterministic Linux Steam-root tests;
- Linux and Flatpak launch-command tests;
- process-detection tests around pure matching logic;
- existing installer/restore regression suite on Windows and Ubuntu;
- solution build on Windows;
- Avalonia `linux-x64` publish on Ubuntu;
- release archive contract tests.

Runtime evidence still required before declaring Linux support complete:

- launch the GUI on a real supported Linux desktop;
- detect an actual Proton installation;
- check ProductVersion and build ID;
- install, invalidate cache, launch, verify bot drops, and restore;
- confirm normal Steam and Flatpak behavior where available.
