# Scrap Mechanic Mod Manager — Design

## Goal

Distribute a Windows launcher/updater once and let it discover or request each player's Scrap Mechanic path, validate the 1.0 game structure, safely install or update shared Survival Lua files, and optionally launch the game.

`robots_01.zip` is the working baseline verified by the user in a live game. Launcher development does not change its drop logic.

## Architecture

The original solution is a .NET 8 self-contained, single-file Windows x64 application:

- `ScrapMechanicModManager.Core` — Steam discovery, compatibility, release API, hashes, backup, installation, and restore;
- `ScrapMechanicModManager` — WinForms UI;
- `ScrapMechanicModManager.Tests` — xUnit regression tests;
- GitHub Releases — public update channel.

The launcher obtains `manifest.json` and the payload ZIP from the GitHub latest-release API. The manifest records the mod version, supported Steam build IDs, ZIP SHA-256, source-to-target file map, and individual file hashes.

The later Linux/Avalonia architecture is documented separately in `2026-08-17-linux-avalonia-launcher-design.md`.

## Installation flow

1. Discover Steam roots from the Windows registry and `libraryfolders.vdf`.
2. Validate `appmanifest_387990.acf` and `installdir`.
3. Allow manual folder selection as a fallback.
4. Validate:
   - AppID `387990`;
   - `Release/ScrapMechanic.exe` exists;
   - ProductVersion major version is `1`;
   - the four `robots_01` target paths have the expected structure;
   - Steam `buildid` appears in the release manifest's supported list.
5. Download the payload into a temporary directory.
6. Verify ZIP and file SHA-256 values and reject path traversal.
7. Refuse installation while `ScrapMechanic.exe` is running.
8. Create a timestamped backup under `%LocalAppData%/ScrapMechanicModManager/backups/`.
9. Perform targeted atomic file replacement from staging.
10. Save installed state and optionally launch through Steam.

## Safety and recovery

- Never overwrite a target before its backup exists.
- The first run captures the current files, which can be vanilla or a previous manual mod.
- Every update creates a separate snapshot.
- Restore uses the selected or latest snapshot.
- Unknown game builds produce diagnostics and cannot be modified.
- Network and hash failures leave target files unchanged.
- Script-cache invalidation was added later and is documented in the current README and installer tests.

## User interface

- Scrap Mechanic path field and Browse action;
- game version/build and mod-version status;
- Check, Install/update, Restore, and Launch actions;
- optional `-dev` launch;
- compact copyable log.

The later bilingual UI uses a persistent Hungarian/English selector and is documented in `2026-08-17-bilingual-ui-design.md`.

## Local project tracking

The private Obsidian vault follows the project's task, protocol, and Kanban conventions. It is intentionally not part of the public repository.
