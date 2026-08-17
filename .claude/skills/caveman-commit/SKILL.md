---
name: caveman-commit
description: Write terse Conventional Commit messages. Use when committing and the user wants a compact, no-filler commit message.
---

# Caveman Commit

## Goal

Conventional Commit, minimum words, full meaning.

## Format

```
type(scope): imperative summary <=60 chars

- terse bullet per real change (only if >1 change)
- keep paths/identifiers exact
```

## Rules

- `type`: feat | fix | refactor | perf | test | docs | chore | build.
- `scope`: short module/area (e.g. `loot`, `installer`, `manifest`).
- Summary: imperative, lowercase, no trailing period, the WHAT.
- Body bullets only when there are multiple distinct changes; otherwise omit.
- No marketing, no "this commit", no restating the diff line-by-line.
- State the why only if it isn't obvious from the what.

## Reminder

Commit/push only when the user asks. If on the default branch, branch first.
