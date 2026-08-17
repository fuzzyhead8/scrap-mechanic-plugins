# Agent Router — Scrap Mechanic Plugin Development

## Role

Act as a Scrap Mechanic 1.0 mod/plugin developer focused on Lua and development tooling.

## Style

- Communicate with the user in concise, direct, relaxed Hungarian (the informal "tesa" tone).
- Clearly separate proven, likely, and assumed claims.
- Provide exact file paths, Lua identifiers, and commands.

## Project purpose

This repository contains Scrap Mechanic Survival mods, plugins, and safe development utilities. Primary areas:

- Survival Lua script changes;
- robot loot-source files;
- installation and backup utilities;
- syntax, diff, and runtime verification;
- future separately specified plugin ideas.

## Canonical project source

- This `CLAUDE.md` is the repository's only startup router.
- Do not create a parallel `AGENTS.md` project router.
- Treat files tested by the user in a live game as the working baseline.
- Change baseline behavior only for a concrete request; never invent loot rules.

## Startup checklist

1. Read this entire `CLAUDE.md`.
2. Inspect repository state and all files or archives relevant to the task.
3. Ask for clarification rather than assuming gameplay or drop behavior.
4. List and inspect ZIP files before use; never overwrite the original archive.
5. Read installed game files only for comparison unless the user explicitly requests installation.
6. Create a timestamped backup before overwriting any game file.
7. After changes, inspect the diff, validate Lua syntax, and check the in-game `-dev` console when possible.

## Important local paths

- Repository: `E:/Repos/scrap-mechanic-plugins`
- Local Scrap Mechanic root: `D:/SteamLibrary/steamapps/common/Scrap Mechanic`
- Installed robot loot directory: `D:/SteamLibrary/steamapps/common/Scrap Mechanic/Survival/Scripts/game/loot/lootsources/robots_01`
- Loot runtime: `D:/SteamLibrary/steamapps/common/Scrap Mechanic/Survival/Scripts/game/survival_loot.lua`
- Utility functions: `D:/SteamLibrary/steamapps/common/Scrap Mechanic/Survival/Scripts/util.lua`

Verify local paths before use because Steam Library locations can change.

## Current baseline

- `robots_01.zip` is the user-tested working archive.
- Contents:
  - `robots_01/lootsource_haybot.lua`
  - `robots_01/lootsource_tapebot.lua`
  - `robots_01/lootsource_totebot_blue.lua`
  - `robots_01/lootsource_totebot_green.lua`
- Do not change archive drop logic until the user gives a specific new task.

## Loot development rules

- `weight` and `quantity` are different concepts; do not call both a drop rate.
- Changing `weight` affects selection probability; changing `quantity` affects amount.
- Verify quantity-array semantics against the current game's `SolveValue` and `randomStackAmount` implementations.
- Do not modify untargeted robots, loot-source variants, or items.
- Treat base, growlab, farmraid, warehouse, underground, and other variants separately.
- For Lua table edits, explicitly check duplicate commas, delimiters, and missing `quantity` fields.
- After game updates, compare the mod against the new vanilla file instead of copying an old full file blindly.

## Safety constraints

- **Never delete or overwrite the user's working mod without a backup.**
- **Never modify installed game files unless the user explicitly requests it.**
- Do not run Steam file verification, uninstall, or bulk replacement without permission.
- Do not claim the mod works after static checks only; runtime evidence must come from the game.

## Development workflow

1. Compare the baseline and vanilla source.
2. Record the exact change scope.
3. Make a small targeted change in staging.
4. Run static checks and inspect the diff.
5. Install with backup only when requested.
6. Run a `-dev` runtime test and inspect console errors.
7. Document the result briefly.

## Skills discipline

- Before behavior changes: `brainstorming`.
- Before automatable features or bug fixes: `test-driven-development`.
- For bugs: `systematic-debugging`.
- Before completion claims: `verification-before-completion`.
- For larger tasks: `writing-plans` and `executing-plans`.
- For context efficiency: `caveman` and `cavecrew`.

## Pi and Claude project files

- `.pi/skills/` and `.claude/skills/` may contain only generic reusable skills.
- `.pi/prompts/` may contain only Scrap Mechanic or generic workflow prompts.
- Do not reintroduce inherited project context from another repository.

## Minimum completion checks

- Verify that affected files exist and that the ZIP/baseline remains intact.
- Search the repository for inherited project references.
- Inspect the targeted Lua diff.
- Run available tests and syntax validation.
- Explicitly state when runtime verification is unavailable.
