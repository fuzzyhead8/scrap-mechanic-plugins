---
name: caveman-compress
description: Compress a memory/notes file to fewer tokens while keeping every fact. Use ONLY on explicit request to compress a specific file; always keep a readable backup first.
---

# Caveman Compress

## Goal

Shrink a memory/notes file's token count without losing any fact, path, number, or decision.

## Hard rule — backup first

Before editing, copy the original to a `.bak` next to it (e.g. `MEMORY.md` -> `MEMORY.md.bak`).
Never compress in place without the backup. Confirm the backup exists before writing.

## What to preserve (non-negotiable)

- Every fact, decision, verdict, and its date.
- All exact numbers, file paths, identifiers, commands, `[[wiki-links]]`.
- Causality (why / how-to-apply lines).

## What to cut

- Filler, hedging, restated context, duplicate phrasings.
- Prose connectors -> fragments and bullets.
- Long narrative -> `claim — evidence — decision` shape.

## Process

1. Confirm the target file and write the `.bak`.
2. Rewrite each block as terse fragments, one fact per line.
3. Diff the fact-set, not the wording: every number/path/decision in the original must still be present.
4. Report before/after size (chars or tokens).

## Only on explicit request

Don't auto-compress. This runs when the user explicitly asks to compress a named file.
