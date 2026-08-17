# Scrap Mechanic Plugins

Scrap Mechanic 1.0 Survival modok és a közös modverziót kezelő Windows launcher repositoryja.

## Működő baseline

A `robots_01.zip` a felhasználó által élő játékban tesztelt robot-loot csomag. A launcher fejlesztése nem módosítja a ZIP drop-logikáját.

Tartalma a játékban ide kerül:

```text
Survival/Scripts/game/loot/lootsources/robots_01
```

## Scrap Mechanic Mod Manager

A .NET 8 WinForms launcher:

- automatikusan felismeri a Steam Library-ket;
- kézi útvonalválasztást is enged;
- ellenőrzi a Steam AppID-t, build ID-t, ProductVersiont és az 1.0-s könyvtárstruktúrát;
- publikus GitHub Releases csatornáról tölti le a manifestet és a payloadot;
- SHA-256-tal ellenőrzi a ZIP-et és minden telepítendő fájlt;
- időbélyeges backupot készít felülírás előtt;
- backup után invalidálja a `Cache/Bundle/core_data.cbo` script-cache-t, így nem kell `-dev` az új Lua fájlok betöltéséhez;
- saját, többfelbontású Scrap Mechanic-hangulatú alkalmazásikont használ;
- támogatja a restore-t és a Steam játékindítást;
- futó játék vagy ismeretlen build esetén nem telepít.

## Fejlesztői parancsok

```powershell
dotnet restore ScrapMechanicModManager.sln
dotnet test ScrapMechanicModManager.sln
dotnet build ScrapMechanicModManager.sln -c Release
dotnet publish src/ScrapMechanicModManager/ScrapMechanicModManager.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o artifacts/launcher
```

Release assetek létrehozása:

```powershell
./scripts/New-ReleasePayload.ps1 `
  -Version 0.1.3 `
  -OutputDirectory artifacts/release
```

## Release folyamat

1. A módosított fájlokat játékban tesztelni kell.
2. Szükség esetén frissíteni kell a `distribution/supported-builds.txt` fájlt.
3. Verziótag létrehozása, például `v0.1.3`.
4. A `.github/workflows/release.yml` elkészíti a single-file launchert, `manifest.json` fájlt és a payload ZIP-et.
5. A kliensek a GitHub latest release API-jából kapják a frissítést.

> [!NOTE]
> A Lua fájlok módosítása után a `core_data.cbo` cache invalidálása kötelező; ezt a launcher install és restore közben automatikusan, backup után végzi.

## Helyi ellenőrzött környezet

```text
Game root: D:/SteamLibrary/steamapps/common/Scrap Mechanic
Steam AppID: 387990
ProductVersion: 1.0.5.876
Steam buildid: 24529696
```

## Biztonsági szabályok

- Telepített játékfájlt backup nélkül nem írunk felül.
- Ismeretlen buildre fail-closed módon nem telepítünk.
- A `weight` kiválasztási súly; a `quantity` mennyiség.
- Játékfrissítés után vanilla/mod diff és új runtime teszt szükséges.
- Statikus teszt nem helyettesíti a játék `-dev` konzol ellenőrzését.

## Obsidian követés

- Projekt: `1_Projects/Scrap_Mechanic_Plugins/_index.md`
- Task index: `1_Projects/Scrap_Mechanic_Plugins/tasks/_index.md`
- Kanban: `Scrap Mechanic Plugins Kanban.md`
