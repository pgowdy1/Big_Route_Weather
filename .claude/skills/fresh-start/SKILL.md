---
name: fresh-start
description: Create a fresh feature branch with clean git status before starting work
argument-hint: <feature-description>
---

# Fresh Start

Ensure a clean working state and create a feature branch before starting any new work.

## Step 1: Check Git Status

```bash
git status
```

If there are **any** uncommitted changes (staged, unstaged, or untracked):

1. List all dirty files clearly
2. **STOP immediately** — do not proceed
3. Ask the user what they want to do using AskUserQuestion:
   - **Stash** the changes (`git stash push -m "WIP before <feature>"`)
   - **Commit** them to the current branch first
   - **Abort** — do nothing, let the user handle it

Do NOT auto-stash, auto-commit, or auto-discard. Wait for the user's decision and execute it before continuing.

## Step 2: Sync with Main

```bash
git checkout main
git pull origin main
```

If there are merge conflicts or pull failures, warn the user and stop.

## Step 3: Create Feature Branch

Generate a URL-safe slug from `$ARGUMENTS`:
- Lowercase
- Replace spaces and special characters with hyphens
- Collapse multiple hyphens
- Truncate to ~40 characters

```bash
git checkout -b feature/<slug>
```

## Step 4: Confirm

Output a clear status:

```
Ready to go:
  Branch:  feature/<slug>
  Base:    main (up to date)
  Status:  clean

Next step: /plan-feature <feature-description>
```
