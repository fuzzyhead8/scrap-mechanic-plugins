# Scrap Mechanic Plugins

Repository for Scrap Mechanic 1.0 Survival mods and the Windows/Linux launchers that manage the shared mod version.

## Working baseline

`robots_01.zip` is the robot-loot package verified by the user in a live game. Launcher development does not change its drop logic.

The package installs into:

```text
Survival/Scripts/game/loot/lootsources/robots_01
```

## Scrap Mechanic Mod Manager

The .NET 8 launchers:

- discover Steam libraries automatically and allow manual folder selection;
- validate Steam AppID, build ID, ProductVersion, and the Scrap Mechanic 1.0 directory structure;
- download the manifest and payload from public GitHub Releases;
- verify the ZIP and every installed file with SHA-256;
- create timestamped backups before overwriting any game file;
- invalidate `Cache/Bundle/core_data.cbo` only after backup, so updated Lua files load without requiring `-dev`;
- use an original multi-resolution application icon inspired by Scrap Mechanic;
- support restore and Steam game launch;
- refuse installation while the game is running or when the build is unknown;
- start in Hungarian by default and provide a persistent `Magyar / English` selector with immediate UI refresh.

## Windows launcher

The tested WinForms launcher is published as a self-contained, single-file `win-x64` executable. It requests elevation only when an operation needs to write into the Steam game directory.

## Linux preview

A separate Avalonia GUI targets `linux-x64` Steam Proton installations while the tested WinForms launcher remains intact. It discovers native and Flatpak Steam roots, uses the same validation/install/restore Core, and never invokes `sudo` automatically.

Required Debian/Ubuntu libraries:

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1
```

Linux support remains a preview until install, launch, gameplay, cache invalidation, and restore pass on a real Steam Proton system. Portable instructions are included in `distribution/linux/README-Linux.txt`.

## Development commands

```powershell
dotnet restore ScrapMechanicModManager.sln
dotnet test ScrapMechanicModManager.sln
dotnet build ScrapMechanicModManager.sln -c Release

dotnet publish src/ScrapMechanicModManager/ScrapMechanicModManager.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o artifacts/launcher

dotnet publish src/ScrapMechanicModManager.Desktop/ScrapMechanicModManager.Desktop.csproj `
  -c Release -r linux-x64 --self-contained true `
  -p:PublishSingleFile=true -o artifacts/linux-publish
```

Create release assets:

```powershell
./scripts/New-ReleasePayload.ps1 `
  -Version 0.2.0-preview.1 `
  -OutputDirectory artifacts/release
```

## Release process

1. Verify modified gameplay files in a live game.
2. Update `distribution/supported-builds.txt` when required.
3. Create a version tag such as `v0.2.0-preview.1`.
4. `.github/workflows/release.yml` builds the Windows EXE, Linux `tar.gz`, `manifest.json`, and payload ZIP.
5. Stable clients query the GitHub latest-release API. Prereleases do not replace the stable latest channel.

> [!NOTE]
> Lua changes require invalidating the `core_data.cbo` cache. Install and restore perform this automatically and only after creating a backup.

## Locally verified environment

```text
Game root: D:/SteamLibrary/steamapps/common/Scrap Mechanic
Steam AppID: 387990
ProductVersion: 1.0.5.876
Steam buildid: 24529696
```

## Safety rules

- Never overwrite installed game files without a backup.
- Fail closed on unknown game builds.
- `weight` is a selection weight; `quantity` is an amount.
- Compare vanilla and mod files and repeat runtime tests after every game update.
- Static verification does not replace checking the in-game `-dev` console.

## Local project tracking

The private Obsidian vault tracks the project, tasks, and Kanban board. Vault content is intentionally not part of this public repository.
