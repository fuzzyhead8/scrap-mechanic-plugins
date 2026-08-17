---
name: cavecrew
description: Delegate independent investigation, small focused edits, or review tasks to terse subagents to save main-thread context. Use when a self-contained subtask can run in isolation and you only need the conclusion back.
---

# Cavecrew

## Goal

Spend subagent tokens instead of main-thread context. Spawn a terse agent for a
self-contained subtask, get back only the conclusion.

## When to use

- Independent investigation (search/audit across many files — you need the finding, not the dumps).
- A small, well-scoped edit you can fully specify up front.
- A focused review of a diff or file.

## When NOT to use

- The task needs ongoing back-and-forth with the user.
- You already have the context loaded — just do it inline (a cold subagent re-derives context = waste).
- The user didn't ask for delegation and the task is small. Don't over-delegate.

## How

1. Pick the agent type your runtime offers for read-only search vs edits vs design.
2. Give a tight prompt: exact files/paths, the one question to answer, and "report only the conclusion, caveman style."
3. Relay only what matters back to the user — the agent's full output is not shown to them.

## Style passed to the agent

- Terse, technical, no filler.
- Keep paths, identifiers, commands exact.
- Lead with the answer / next action.
