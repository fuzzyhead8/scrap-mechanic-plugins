# Scrap Mechanic Mod Manager — Design

## Cél

Egyetlen alkalommal kiosztandó Windows launcher/frissítő, amely a játékosoknál automatikusan megtalálja vagy bekéri a Scrap Mechanic útvonalát, ellenőrzi az 1.0-s játékstruktúrát, biztonságosan telepíti/frissíti a közös Survival Lua fájlokat, majd igény szerint elindítja a játékot.

A `robots_01.zip` a felhasználó által játékban tesztelt, működő baseline. A launcher fejlesztése nem változtatja meg a ZIP drop-logikáját.

## Architektúra

A megoldás .NET 8 alapú, Windows x64 self-contained single-file alkalmazás:

- `ScrapMechanicModManager.Core` — Steam felismerés, kompatibilitás, release API, hash, backup, telepítés és restore;
- `ScrapMechanicModManager` — WinForms felület;
- `ScrapMechanicModManager.Tests` — xUnit regressziós tesztek;
- GitHub Releases — publikus frissítési csatorna.

A launcher a GitHub `latest release` API-jából keresi a `manifest.json` és a payload ZIP asseteket. A manifest rögzíti a modverziót, támogatott Steam build ID-ket, a ZIP SHA-256 értékét, a forrás–cél fájltérképet és az egyes fájlok hashét.

## Telepítés folyamata

1. Steam rootok felismerése registryből és `libraryfolders.vdf` alapján.
2. `appmanifest_387990.acf` és `installdir` ellenőrzése.
3. Kézi mappaválasztás fallbackként.
4. Validáció:
   - AppID `387990`;
   - `Release/ScrapMechanic.exe` létezik;
   - ProductVersion főverziója `1`;
   - a négy `robots_01` célfájl könyvtárstruktúrája létezik;
   - a Steam `buildid` szerepel a release manifest támogatott listáján.
5. Payload letöltése ideiglenes könyvtárba.
6. ZIP és fájlok SHA-256 ellenőrzése, path traversal tiltása.
7. Futó `ScrapMechanic.exe` esetén telepítés tiltása.
8. Időbélyeges backup `%LocalAppData%/ScrapMechanicModManager/backups/` alatt.
9. Stagingből célzott, atomi fájlcsere.
10. Telepített állapot mentése és opcionális Steam-indítás.

## Biztonság és helyreállítás

- Felülírás backup nélkül nem történhet.
- Első futáskor az aktuális fájlok `pre-manager` mentést kapnak; ez lehet vanilla vagy korábbi kézi mod.
- Minden frissítés külön snapshotot készít.
- Restore a kiválasztott/latest snapshotból dolgozik.
- Ismeretlen játékbuildnél a launcher nem telepít, csak diagnosztikát mutat.
- Hálózati vagy hash hiba után a célfájlok változatlanok maradnak.

## Felület

- Scrap Mechanic útvonal mező + Tallózás;
- játékverzió/build és modverzió státusz;
- `Ellenőrzés`, `Telepítés / frissítés`, `Visszaállítás`, `Játék indítása`;
- opcionális `-dev` indítás;
- tömör, másolható napló.

## Obsidian követés

A vault a GoldGrid mintáját követi:

- `1_Projects/Scrap_Mechanic_Plugins/_index.md`;
- `Setup & Config/_index.md`;
- `Setup & Config/Kanban Priority System.md`;
- `Setup & Config/Launcher & Update Protocol.md`;
- `tasks/_index.md` és dátumozott task note-ok;
- vault gyökérben `Scrap Mechanic Plugins Kanban.md`.

Prioritások: 🔴 current, 🟠 high/blocked/bug, 🟡 research, 🔵 planned, ⚪ later, ✅ done. Kártyatagok: `#epic/scrap-mechanic-plugins`, `#task`, `#bug`.
