# Scrap Mechanic Mod Manager Implementation Plan

> **REQUIRED SUB-SKILL:** Use the executing-plans skill to implement this plan task-by-task.

**Goal:** Biztonságos, önfrissítő Windows launcher létrehozása a közösen használt Scrap Mechanic 1.0 Survival Lua fájlokhoz, GoldGrid-formátumú Obsidian projektkövetéssel.

**Architecture:** Egy tesztelhető `net8.0` core library végzi a Steam/game felismerést, kompatibilitást, GitHub release feldolgozást és tranzakciós telepítést. Egy vékony `net8.0-windows` WinForms alkalmazás adja a felületet; publikus GitHub Releases biztosítja a manifestet és payloadot.

**Tech Stack:** C# 12, .NET 8, WinForms, xUnit, PowerShell/GitHub Actions, Obsidian MCP.

---

### Task 1: Solution és kompatibilitási modellek

**Files:**
- Create: `ScrapMechanicModManager.sln`
- Create: `src/ScrapMechanicModManager.Core/ScrapMechanicModManager.Core.csproj`
- Create: `tests/ScrapMechanicModManager.Tests/ScrapMechanicModManager.Tests.csproj`
- Test: `tests/ScrapMechanicModManager.Tests/GameInstallValidatorTests.cs`

1. Írj tesztet, amely elutasítja a hiányzó 1.0 könyvtárstruktúrát.
2. Futtasd és ellenőrizd a RED állapotot.
3. Implementáld a minimális modelleket és validátort.
4. Futtasd és ellenőrizd a GREEN állapotot.

### Task 2: Steam Library és AppManifest felismerés

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Steam/SteamLibraryLocator.cs`
- Create: `src/ScrapMechanicModManager.Core/Steam/SteamAppManifest.cs`
- Test: `tests/ScrapMechanicModManager.Tests/SteamLibraryLocatorTests.cs`

1. Teszteld ideiglenes `libraryfolders.vdf` és `appmanifest_387990.acf` fájlokkal a több library-s felismerést.
2. RED után implementálj dependency nélküli, célzott VDF-parsert.
3. Ellenőrizd az AppID, `installdir`, `buildid` és `StateFlags` mezőket.

### Task 3: Release manifest és integritás

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Updates/ModManifest.cs`
- Create: `src/ScrapMechanicModManager.Core/Updates/GitHubReleaseClient.cs`
- Create: `src/ScrapMechanicModManager.Core/Security/HashService.cs`
- Test: `tests/ScrapMechanicModManager.Tests/ManifestTests.cs`

1. Teszteld a manifest schema, asset-név, build-lista és hash validálását.
2. Teszteld a GitHub latest release válasz assetfeloldását fake HTTP handlerrel.
3. Implementáld a minimális klienst és SHA-256 ellenőrzést.

### Task 4: Backup, install és restore

**Files:**
- Create: `src/ScrapMechanicModManager.Core/Installation/ModInstaller.cs`
- Create: `src/ScrapMechanicModManager.Core/Installation/BackupStore.cs`
- Create: `src/ScrapMechanicModManager.Core/Installation/ZipPayloadValidator.cs`
- Test: `tests/ScrapMechanicModManager.Tests/ModInstallerTests.cs`

1. Teszteld ideiglenes game rooton a backup-before-write szabályt.
2. Teszteld a ZIP path traversal, hibás hash és részleges payload tiltását.
3. Teszteld a restore-t és a hiba utáni változatlan célállapotot.
4. Implementálj staging + atomi cserét.

### Task 5: WinForms launcher

**Files:**
- Create: `src/ScrapMechanicModManager/ScrapMechanicModManager.csproj`
- Create: `src/ScrapMechanicModManager/Program.cs`
- Create: `src/ScrapMechanicModManager/MainForm.cs`
- Create: `src/ScrapMechanicModManager/app.manifest`

1. Építs vékony UI-t a tesztelt core szolgáltatások fölé.
2. Add hozzá az útvonal tallózást, státuszt, telepítést, restore-t, launchot és `-dev` opciót.
3. Hibákat emberi nyelvű dialógusban és naplóban jeleníts meg.

### Task 6: Release csomagolás

**Files:**
- Create: `distribution/manifest.template.json`
- Create: `scripts/New-ReleasePayload.ps1`
- Create: `.github/workflows/release.yml`
- Modify: `README.md`

1. Készíts tesztelhető manifest/payload generátort a változatlan `robots_01.zip` alapján.
2. A workflow publikáljon self-contained `win-x64` launchert és release asseteket tag pushra.
3. Dokumentáld, hogy GitHub hitelesítés nélkül lokálisan buildelhető, de release nem tölthető fel.

### Task 7: MCP és Obsidian projektkövetés

**Files:**
- Modify: `.mcp.json`
- Create in vault: `1_Projects/Scrap_Mechanic_Plugins/**`
- Create in vault: `Scrap Mechanic Plugins Kanban.md`

1. Másold a generikus `web-tools` scriptet a projektbe, és állítsd a MCP útvonalát a helyi `.pi/scripts/web-tools-mcp.mjs` fájlra.
2. Hozd létre a GoldGrid-formátumú indexeket, konvenciót, protokollt és task note-okat.
3. Hozd létre a Kanban boardot egyetlen 🔴 aktuális taskkal.
4. Obsidian MCP-vel olvasd vissza és ellenőrizd a frontmattert/wikilinkeket.

### Task 8: Végső ellenőrzés

1. Futtasd: `dotnet test ScrapMechanicModManager.sln`.
2. Futtasd: `dotnet build ScrapMechanicModManager.sln -c Release`.
3. Futtass self-contained publish smoke buildet.
4. Ellenőrizd a ZIP hashét és tartalomlistáját.
5. Ellenőrizd az Obsidian note-okat és a Kanban settings blokkot.
6. Repo-szintű kereséssel zárd ki az örökölt projekthivatkozásokat.
