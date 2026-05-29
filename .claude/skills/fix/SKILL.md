---
name: fix
description: Streamlined bug fix workflow — investigates, fixes, and verifies without the overhead of a full plan or agent team
argument-hint: <bug description>
---

# Fix

You are fixing a bug. This skill is for quick, targeted fixes that don't need a full plan document or multi-agent ceremony. It uses a debugger agent to investigate, then either fixes directly or delegates to specialist agents.

## When to Use This

- Bug reports and defect fixes
- Small behavioral issues (wrong output, broken UI, failed validation)
- Regressions after a recent change
- Issues where you need to investigate before fixing

**Don't use this for:** New features (use `/plan-feature` + `/new-feature`), large refactors, or multi-component changes (use `/build-with-agent-team`).

## Arguments

- **Bug description**: `$ARGUMENTS` — A plain-language description of the bug

## Step 1: Investigate

Spawn a **debugger** agent to find the root cause. Give it the bug description and ask it to:

1. Search the codebase for files related to the bug
2. Read the relevant code paths
3. Identify the root cause
4. Report back with:
   - **Affected files** (exact paths)
   - **Layers involved** (frontend, backend, or both)
   - **Root cause** (what's wrong and why)
   - **Suggested fix** (what to change)

```
Task: debugger agent
Prompt: "Investigate this bug: $ARGUMENTS

Search the codebase, read the relevant files, and report:
1. Affected files (exact paths)
2. Layers involved (frontend, backend, or both)
3. Root cause diagnosis
4. Suggested fix approach

Do NOT make any changes — investigation only."
```

## Step 2: Assess & Fix

Based on the debugger's findings, choose a path:

### Path A: Lead fixes directly (default)

Use this when:
- The fix is in a **single layer** (frontend only OR backend only)
- The change is **straightforward** (a few lines in 1-3 files)
- You understand the fix clearly from the investigation

Do:
1. Read the affected files
2. Make the fix following CLAUDE.md conventions
3. Keep changes minimal — fix the bug, nothing else

### Path B: Delegate to specialist agents

Use this when:
- The fix spans **both frontend and backend**
- The fix requires **deep domain knowledge** in a specific area
- The investigation reveals **complexity** beyond a simple patch

Spawn the appropriate agent(s):
- **Backend issue** -> spawn `backend-dev` agent
- **Frontend issue** -> spawn `frontend-dev` agent
- **Both layers** -> spawn both agents **in parallel**

## Step 3: Verify

Run the test suite to confirm the fix doesn't break anything:

**Backend:**
```bash
dotnet test --verbosity normal
```

**Frontend:**
```bash
npm test 2>&1
```

If tests fail:
- Determine if the failure is related to the fix
- If related: fix and re-run
- If unrelated (pre-existing): note it in the summary

## Step 4: Summary

Output a concise report:

```
Fixed: <one-line summary of what was wrong>

Root cause: <why it was broken>

Files changed:
- path/to/file — what changed
- path/to/file — what changed

Tests: all passing (or note pre-existing failures)
Next step: /commit-pr "Fix <short description>"
```
