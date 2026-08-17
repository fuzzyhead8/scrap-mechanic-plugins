---
name: caveman-review
description: Compact code-review comments. Use when reviewing a diff/file and the user wants terse, high-signal findings instead of prose.
---

# Caveman Review

## Goal

Maximum signal per token. Findings, not essays.

## Output format

One line per finding, ordered by severity:

```
SEV  file:line  problem -> fix
```

- `SEV` = 🔴 blocker | 🟠 should-fix | 🟡 nit.
- `problem` = what's wrong, concrete.
- `fix` = the change, concrete. Skip if obvious.

## Rules

- Only real findings. No "looks good" filler, no praise padding.
- Reference exact `file:line`. Quote the offending token, not paragraphs.
- One concern per line. No bundling.
- If clean: say `clean` plus the one risk you'd still watch, then stop.
- Correctness > brevity — never soften a 🔴 to save words.

## Scope discipline

Review what changed. Don't expand into unrelated refactors unless it's a 🔴.
