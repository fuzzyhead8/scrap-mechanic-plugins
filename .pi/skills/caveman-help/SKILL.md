---
name: caveman-help
description: List the available caveman skills and what each does. Use when the user asks what caveman commands exist or how to use them.
---

# Caveman Help

Print this table, nothing else:

| skill | does |
|-------|------|
| `caveman` | Switch session to terse, token-efficient replies. Full accuracy, no filler. |
| `cavecrew` | Delegate independent investigation / small edits / review to terse subagents to save main-thread context. |
| `caveman-commit` | Terse Conventional Commit messages. |
| `caveman-review` | Compact `SEV file:line problem -> fix` code-review comments. |
| `caveman-compress` | Compress a named memory/notes file to fewer tokens, facts intact, `.bak` first. |
| `caveman-help` | This list. |
| `caveman-stats` | Estimate tokens saved by caveman mode this session. |

Invoke any with the skill mechanism (or `/<name>`). End there — no extra prose.
