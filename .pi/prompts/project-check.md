# Scrap Mechanic project check

Ellenőrizd a jelenlegi Scrap Mechanic mod/plugin munkát:

1. Olvasd el a `CLAUDE.md` projekt-routert.
2. Vizsgáld meg a git státuszt és csak a feladathoz tartozó fájlokat.
3. A `robots_01.zip` fájlt kezeld felhasználó által tesztelt, érintetlen baseline-ként.
4. Gameplay/drop változtatást ne feltételezz; kérj pontos hatókört.
5. Lua módosításnál ellenőrizd a dupla vesszőket, zárójeleket, item azonosítókat, `weight` és `quantity` mezőket.
6. Hasonlítsd össze a célzott diffet az aktuális vanilla játékfájllal.
7. Telepített játékfájlt csak explicit kérésre, backup után írj felül.
8. Jelentsd külön a statikus és a játékban elvégzett runtime ellenőrzést.
