---
name: caveman-stats
description: Estimate tokens saved by caveman/terse mode in the current session. Use when the user asks how much caveman mode is saving.
---

# Caveman Stats

## Goal

Give a quick, honest estimate of token savings from terse mode this session.

## Method

1. Count caveman-mode assistant replies this session and their rough token size.
2. Estimate a verbose-baseline size for the same content (typical 2.5-4x for prose vs fragments).
3. Saved ≈ baseline − actual. Use ~0.75 words/token to convert if counting words.

## Output (terse)

```
caveman replies: N
actual ~A tokens | verbose est ~B tokens
saved ~ (B-A) tokens (~P%)
```

## Honesty

- It's an estimate — say so. Don't fabricate precise counts.
- If too few terse replies to judge, say `not enough caveman turns yet` and stop.
