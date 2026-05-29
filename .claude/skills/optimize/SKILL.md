---
name: optimize
description: Simplify code and enforce project conventions across recent changes or a specific scope
argument-hint: "[file-or-scope]"
disable-model-invocation: true
---

# Optimize

Simplify code and ensure it adheres to project conventions. Focus on clarity, not cleverness.

## Step 1: Determine Scope

If `$ARGUMENTS` is provided:
- If it's a file path -> optimize that file
- If it's a component/service name -> find and optimize all related files
- If it's "recent" -> optimize all files changed since branching from main

If no arguments:
- Default to all files changed since branching from main:
```bash
git diff --name-only main...HEAD
```

If on main with uncommitted changes:
```bash
git diff --name-only HEAD
```

## Step 2: Read Conventions

Before making any changes, read the project standards:

1. `CLAUDE.md` — architecture rules, patterns, tech stack conventions
2. `.claude/rules/` — any topic-specific rules files

## Step 3: Analyze

For each file in scope, check for:

**Complexity reduction:**
- Deeply nested conditionals that can be flattened (early returns, guard clauses)
- Redundant null checks or type checks the framework already handles
- Over-engineered abstractions for one-time operations
- Unused imports, variables, or parameters
- Duplicated logic that should be extracted (only if used 3+ times)

**Convention adherence:**
- Angular: signals instead of subjects, `@if`/`@for` instead of `*ngIf`/`*ngFor`, standalone components
- C#: async/await patterns, proper DI usage, `AsNoTracking()` for read queries
- General: consistent naming, proper error boundaries, no swallowed exceptions

**Things to NOT change:**
- Working logic that's already clear — don't rewrite for style preference
- Comments that explain "why" (only remove comments that restate the code)
- Test assertions — don't weaken them for simplicity
- Public API contracts — don't change method signatures or endpoint shapes

## Step 4: Apply Changes

Make the simplification changes. For each change:
- Use the Edit tool (not Write) to make targeted modifications
- Keep changes minimal and focused — one concern per edit

## Step 5: Verify

Run tests to confirm nothing broke:

```bash
dotnet test --verbosity quiet
```

```bash
npm test 2>&1
```

If any test fails:
- Immediately revert the change that caused the failure
- Note it in the report as "skipped — would break tests"

## Step 6: Report

Present a summary of what was changed:

```
Optimized X files

Changes:
- path/to/file.ts:42 — flattened nested conditional to early return
- path/to/file.cs:15 — removed unused import
- path/to/component.ts:88 — replaced *ngFor with @for

Skipped (would break tests):
- path/to/file.cs:30 — attempted simplification but test X relies on current behavior

No changes needed:
- path/to/clean-file.ts — already follows conventions
```
