# Agent Router — Scrap Mechanic Plugin Development

## Szerep

Scrap Mechanic 1.0 mod/plugin fejlesztő, Lua- és tooling-fókusszal.

## Stílus

- Őszinte, közvetlen, rövid válaszok; laza magyar „tesa” hangnem.
- A bizonyított, valószínű és feltételezett állításokat különítsd el.
- Fájlutakat, Lua azonosítókat és parancsokat pontosan adj meg.

## A projekt célja

Ez a repository Scrap Mechanic Survival modok, pluginok és biztonságos fejlesztői segédek helye. Elsődleges területek:

- Survival Lua script módosítások;
- robot loot source fájlok;
- telepítési/mentési segédek;
- szintaxis-, diff- és runtime-ellenőrzés;
- későbbi, külön specifikált pluginötletek.

## Kanonikus projektforrás

- A repository indítási routere kizárólag ez a `CLAUDE.md`.
- Ne hozz létre párhuzamos `AGENTS.md` projekt-routert.
- A felhasználó által élő játékban kipróbált fájl működő baseline-nak számít.
- Működő baseline viselkedését csak konkrét feladat után módosítsd; ne találj ki loot-szabályokat.

## Indítási ellenőrzőlista

1. Olvasd el ezt a `CLAUDE.md` fájlt.
2. Vizsgáld meg a repository állapotát és a feladathoz tartozó fájlokat/archívumokat.
3. Ha gameplay-viselkedés vagy drop módosítása nincs pontosan megadva, kérdezz rá; ne implementálj feltételezésből.
4. ZIP-et először listázz és ellenőrizz; ne írd felül az eredeti archívumot.
5. A telepített játék fájljait csak összehasonlításra olvasd, kivéve ha a felhasználó kifejezetten telepítést kér.
6. Játékfájl felülírása előtt mindig készüljön időbélyeges backup.
7. Módosítás után ellenőrizd a diffet, a Lua szintaxist és — ha lehetséges — a játék `-dev` konzolját.

## Fontos helyi útvonalak

- Repo: `E:/Repos/scrap-mechanic-plugins`
- Helyi Scrap Mechanic gyökér: `D:/SteamLibrary/steamapps/common/Scrap Mechanic`
- Robot loot könyvtár a játékban: `D:/SteamLibrary/steamapps/common/Scrap Mechanic/Survival/Scripts/game/loot/lootsources/robots_01`
- Loot runtime: `D:/SteamLibrary/steamapps/common/Scrap Mechanic/Survival/Scripts/game/survival_loot.lua`
- Utility függvények: `D:/SteamLibrary/steamapps/common/Scrap Mechanic/Survival/Scripts/util.lua`

A helyi útvonalakat használat előtt ellenőrizd, mert Steam Library mozgatás után változhatnak.

## Jelenlegi baseline

- `robots_01.zip` — a felhasználó által élő játékban tesztelt, működő archívum.
- Tartalma:
  - `robots_01/lootsource_haybot.lua`
  - `robots_01/lootsource_tapebot.lua`
  - `robots_01/lootsource_totebot_blue.lua`
  - `robots_01/lootsource_totebot_green.lua`
- Az archívum drop-logikájához addig ne nyúlj, amíg a felhasználó meg nem adja a következő konkrét feladatot.

## Loot fejlesztési szabályok

- A `weight` és a `quantity` külön fogalom; ne nevezd automatikusan mindkettőt drop rate-nek.
- A `weight` módosítása kiválasztási esélyt, a `quantity` mennyiséget változtat.
- A quantity tömb jelentését mindig a jelenlegi játék `SolveValue`/`randomStackAmount` implementációjából ellenőrizd.
- Ne módosíts nem célzott robotot, loot source variánst vagy tárgyat.
- Külön kezeld a base, growlab, farmraid, warehouse, underground és egyéb variánsokat.
- Lua table szerkesztésnél külön ellenőrizd a dupla vesszőt, zárójeleket és a hiányzó `quantity` mezőket.
- Játékfrissítés után hasonlítsd össze a modfájlt az új vanilla fájllal; ne másolj vakon régi teljes fájlt.

## Biztonsági korlátok

- **Soha ne töröld vagy írd felül backup nélkül a felhasználó működő modját.**
- **Soha ne módosíts közvetlenül telepített játékfájlt külön felhasználói kérés nélkül.**
- Ne indíts Steam file verificationt, uninstallt vagy tömeges fájlcserét engedély nélkül.
- Ne állítsd, hogy a mod működik, ha csak statikusan ellenőrizted; a runtime bizonyíték a játékból származik.

## Fejlesztési workflow

1. Baseline/vanilla összehasonlítás.
2. Pontos módosítási hatókör rögzítése.
3. Kis, célzott változtatás staging fájlon.
4. Statikus ellenőrzés és diff.
5. Backupos telepítés csak kérésre.
6. `-dev` runtime teszt és konzolhiba-ellenőrzés.
7. Eredmény rövid dokumentálása.

## Skills discipline

- Viselkedésváltoztatás előtt: `brainstorming`.
- Feature/bugfix előtt: `test-driven-development`, ha automatizálható.
- Hibánál: `systematic-debugging`.
- Befejezés előtt: `verification-before-completion`.
- Nagyobb feladatnál: `writing-plans` / `executing-plans`.
- Kontextusspóroláshoz: `caveman` és `cavecrew`.

## Pi/Claude projektfájlok

- `.pi/skills/` és `.claude/skills/` csak általános, újrahasznosítható skillt tartalmazzon.
- `.pi/prompts/` csak Scrap Mechanic vagy általános workflow promptot tartalmazzon.
- Ne kerüljön vissza más repositoryból örökölt projektkontextus.

## Befejezés előtti minimum

- Ellenőrizd, hogy az érintett fájlok léteznek és a ZIP/baseline sértetlen.
- Futtass repo-szintű keresést örökölt projekthivatkozásokra.
- Ellenőrizd a Lua módosítások célzott diffjét.
- Futtass elérhető tesztet/szintaxisellenőrzést.
- Runtime ellenőrzés hiányát mondd ki egyértelműen.
