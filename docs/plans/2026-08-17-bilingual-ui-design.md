# Bilingual UI and English Public Repository Design

## Goal

Provide Hungarian and English user interfaces in both launchers. Hungarian is the default on first launch. Users can switch language immediately from a top-right selector, and the choice persists across launches. Public technical content in the current repository tree is English; the local Obsidian vault remains Hungarian.

## Chosen approach

Localization lives in `ScrapMechanicModManager.Core` so WinForms and Avalonia use the same contract and translations. A typed `TextKey` enum prevents stringly typed lookups, while `AppLocalizer` owns the active `AppLanguage`, formatting, completeness validation, and fallback behavior. Hungarian and English values are compiled into the applications so self-contained single-file releases do not depend on external translation files.

This is preferred over `.resx` because both UI stacks need explicit immediate refresh and structured log re-rendering anyway. It is preferred over external JSON because missing or modified language files must not break a security-sensitive installer.

## Settings

A shared `ManagerSettingsStore` reads and writes the existing `settings.json` file under `ScrapMechanicModManager` application data. The schema adds `language` while retaining `gameRoot`, so upgrades preserve the previously selected installation.

Missing, empty, malformed, or unknown language values fall back to Hungarian. Saving uses a temporary file and replacement to avoid leaving partial JSON. Settings persistence never triggers elevation.

## UI behavior

Both launchers show the same top-right selector with the autonyms `Magyar` and `English`. Initialization loads settings before applying visible text. Changing the selector:

1. updates `AppLocalizer.Language`;
2. saves the new language together with the current game path;
3. reapplies all static control text;
4. rerenders current game/mod statuses;
5. rerenders retained structured log entries;
6. records a localized language-change log entry.

Dynamic status and log entries are stored as `LocalizedMessage` values containing a key and formatting arguments rather than final strings. This allows existing visible content to switch language immediately without mixed-language history.

Dialog titles, confirmation buttons, validation summaries, permission failures, network failures, cancellation messages, and generic errors use localization keys. Unexpected exception details are not copied directly into the user-facing log because operating-system and framework messages can be in an arbitrary language. Known operation context and exception categories are mapped to safe localized messages.

## Public repository language

The current tracked source tree is audited for Hungarian technical prose. Identifiers and comments remain or become English. `README.md`, `CLAUDE.md`, tracked design/implementation documents, workflow/release text, and technical exception messages become English. Intentional Hungarian translation values are allowed and excluded from the English-only audit. Git history is not rewritten. Local `.claude`, `.pi`, `.mcp`, `.mcp.json`, and the Obsidian vault remain outside this conversion.

## Testing

TDD covers:

- Hungarian first-launch default;
- English selection and persistence;
- malformed and unknown settings fallback;
- preservation of `gameRoot` while changing language;
- complete HU/EN key coverage and formatting;
- structured status/log rerendering;
- WinForms and Avalonia language-selector contracts;
- absence of hard-coded user-facing Hungarian text outside the translation catalog;
- absence of Hungarian technical prose in public tracked files outside documented exclusions;
- the existing Windows and Ubuntu build, packaging, integrity, backup, restore, and cache-invalidation suite.

Manual smoke testing verifies immediate selector behavior in both GUI stacks. A real Linux/Proton gameplay smoke remains a separate Task 10 runtime gate.
