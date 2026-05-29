---
name: full-feature
description: "End-to-end feature pipeline: branch, plan, build, test, optimize, verify — ending ready for manual testing"
argument-hint: <feature-description>
---

# Full Feature Pipeline

You are running the complete feature development pipeline from start to finish. Execute each phase in order, transitioning automatically between phases. The pipeline ends with the app running locally, ready for manual testing.

**Feature:** `$ARGUMENTS`

**Pipeline:**
```
1. Fresh Start -> 2. Plan -> 3. Build (incremental) -> 4. Test -> 5. Optimize -> 6. Verify -> 7. Ready for Manual Testing
```

**CRITICAL RULES:**
- After completing each phase, **immediately proceed to the next phase**. Do NOT stop to say "Next step: run /skill-name." Do NOT wait for the user to invoke the next skill.
- The only time you STOP is when a phase **fails** and you cannot recover after 2 attempts. Report the issue clearly and let the user decide.
- Phase 2 (Plan) requires user answers to questions — that's expected. After the user answers, continue automatically.
- **Show evidence, not assertions.** Always paste actual command output (test results, build output, errors). Never just say "tests pass" — prove it.

---

## Phase 1: Fresh Start

### 1.1 Check Git Status

```bash
git status
```

If there are **any** uncommitted changes:
1. List the dirty files
2. **STOP** — ask the user via AskUserQuestion:
   - **Stash** the changes (`git stash push -m "WIP before feature"`)
   - **Commit** them to the current branch first
   - **Abort** — let the user handle it
3. Execute the user's choice before continuing.

If clean, proceed.

### 1.2 Sync with Main

```bash
git checkout main
git pull origin main
```

If merge conflicts or pull failures, **STOP** and report.

### 1.3 Create Feature Branch

Generate a URL-safe slug from `$ARGUMENTS`:
- Lowercase, hyphens for spaces/special chars, collapse multiple hyphens, ~40 chars max

```bash
git checkout -b feature/<slug>
```

Output status, then **immediately proceed to Phase 2**.

---

## Phase 2: Plan Feature

### 2.1 Codebase Reconnaissance

Before asking questions — **DO NOT IMPLEMENT YET**:
1. Read `CLAUDE.md` for architecture and conventions
2. Use Grep/Glob to scan for files related to `$ARGUMENTS`
3. Identify relevant components, services, endpoints, models
4. Note reusable patterns and existing utilities — prefer extending over creating

### 2.2 Round 1 Questions (Foundational)

Use AskUserQuestion to ask 3-4 questions:

1. **Layers** — "Which layers does this feature touch?"
   - Options: Frontend only, Backend only, Full-stack (frontend + backend), Full-stack + database changes
2. **Scope** — "How would you describe the scope?"
   - Options: Small (single component/endpoint tweak), Medium (new component or endpoint), Large (multiple components, endpoints, and/or data model changes)
3. **UX intent** — "What should the user experience look like?" (open-ended)
4. **Existing vs new** — "Are we modifying existing functionality or building something net new?"
   - Options: Modify existing, Build new, Both

### 2.3 Round 2 Questions (Targeted)

Based on Round 1, ask 2-4 follow-ups relevant to the layers/scope:
- **Frontend:** Component placement, UI patterns (dialog, inline, sidebar, page)
- **Backend:** New endpoints, external service integration needs
- **Database:** Data shapes, indexing requirements
- **Large scope:** Milestones, MVP, dependencies between pieces

### 2.4 Round 3 Questions (Edge Cases)

1. **Error scenarios** — what should happen when things go wrong?
2. **Deployment impact** — only ask if new files/deps/config are involved

### 2.5 Produce the Plan

Write plan to `.claude/plans/<slug>.md` with:
- Feature name, branch, scope, layers
- Description, requirements, affected files (existing + new)
- API contract (if backend), data model changes (if DB)
- Implementation steps ordered by layer (backend first)
- Edge cases & error handling
- Test plan with specific assertions
- **Verification commands** — exact commands to prove each layer works
- Complexity assessment: recommend solo (`/new-feature`) or team (`/build-with-agent-team`)

**Immediately proceed to Phase 3.**

---

## Phase 3: Build (Incremental)

Read the plan from `.claude/plans/<slug>.md`. Check the complexity assessment.

### Build-Verify Loop

The core principle: **build incrementally and verify after each meaningful change.** Do not write an entire feature and test at the end.

For each implementation step in the plan:
1. **Implement** the step
2. **Verify** — run the relevant check (compile, type-check, or quick test)
3. **Fix or bail** — if verification fails, fix it. If the same error persists after **2 fix attempts**, stop and reassess your approach rather than looping

### Path A: Solo Build (plan says small/medium or recommends `/new-feature`)

Work through the plan's implementation steps in order:

- **Read before editing** — always read a file before modifying it
- **Backend first** — if full-stack, implement the API before the frontend
- **Follow conventions** — CLAUDE.md and `.claude/rules/`
- **One concern at a time** — don't mix unrelated changes
- **Verify after each layer** — build/compile after completing each layer before moving to the next

Backend verification checkpoint:
```bash
dotnet build
```

Frontend verification checkpoint:
```bash
npx ng build 2>&1 | tail -5
```

### Path B: Agent Team Build (plan says large, 3+ layers, or recommends `/build-with-agent-team`)

1. Determine team structure from plan (2-5 agents based on layers)
2. Define agent roles, ownership boundaries, and cross-cutting concerns
3. Map the **contract chain**: Database -> Backend -> Frontend
4. **Spawn upstream agents first** — their first task is publishing their contract
5. **Receive and verify each contract** — exact URLs, JSON shapes, status codes
6. **Forward verified contracts to downstream agents** with "build to this exactly"
7. Agents build in parallel once contracts are verified
8. Run **contract diff** before integration (compare backend endpoints vs frontend fetch URLs)
9. Run end-to-end validation after all agents complete

### Bail-Out Rules

If you encounter a problem that persists after 2 fix attempts:
1. **Stop editing the problematic file**
2. **Assess**: Is the approach fundamentally wrong, or is it a small mistake?
3. **If wrong approach**: Revert the file and try a different strategy
4. **If stuck**: Report clearly what's failing, what you tried, and ask the user for direction

Do NOT enter a loop of progressively worse fixes.

**After build completes and compiles cleanly, immediately proceed to Phase 4.**

---

## Phase 4: Test

### 4.1 Run Full Test Suite

**Backend:**
```bash
dotnet test --verbosity normal
```

**Frontend:**
```bash
npm test 2>&1
```

### 4.2 Evaluate Results

Paste the actual test output. Report:
```
Phase 4: Test Results
Backend:  X passed, Y failed, Z skipped
Frontend: X passed, Y failed
```

### 4.3 Fix Failures (max 2 rounds)

**If any tests FAIL:**
1. Read the failure message and stack trace carefully
2. Identify the root cause — is it a bug in the new code, a missing mock, or a pre-existing issue?
3. Fix the issue and re-run tests
4. If the **same test fails again** after the fix, try one more fundamentally different approach
5. If it fails a **third time**, stop and report: what the test expects, what's actually happening, and what you've tried

**When all pass, immediately proceed to Phase 5.**

---

## Phase 5: Optimize

### 5.1 Determine Scope

Only optimize files changed in this feature:
```bash
git diff --name-only main...HEAD
```

### 5.2 Read Conventions

Read: `CLAUDE.md`, `.claude/rules/` (any files scoped to changed paths)

### 5.3 Analyze & Apply

For each changed file, check for and fix:
- Deeply nested conditionals -> flatten with early returns/guard clauses
- Unused imports, variables, parameters
- Over-engineered abstractions for one-time operations
- Convention violations (signals vs subjects, `@if` vs `*ngIf`, `AsNoTracking()`, etc.)

**Do NOT change:**
- Working clear logic — don't rewrite for style preference
- "Why" comments (only remove comments that restate the code)
- Test assertions — don't weaken them for simplicity
- Public API contracts — don't change method signatures or endpoint shapes

Use Edit tool for targeted modifications. One concern per edit.

**Immediately proceed to Phase 6.**

---

## Phase 6: Verify (Post-Optimization)

Run the **exact same test commands** as Phase 4. Paste the output.

**If any test fails due to an optimization change:**
1. Immediately revert that specific optimization
2. Re-run tests to confirm the revert fixed it
3. Note the reverted change as "skipped — would break tests"

**When all pass, immediately proceed to Phase 7.**

---

## Phase 7: Ready for Manual Testing

This is the final phase. Prepare the project for the user to test manually.

### 7.1 Verify Build is Clean

```bash
dotnet build
```

```bash
npx ng build 2>&1 | tail -5
```

Both must succeed with no errors. Warnings are acceptable.

### 7.2 Subagent Review

Spawn an `Explore` subagent to review the diff in a **fresh context**:

```
Task: Explore agent
Prompt: "Review the changes on this branch vs main.
Run: git diff main...HEAD

Check for:
1. Files that were changed but shouldn't have been (unrelated changes)
2. Leftover debug code (console.log, debugger statements, TODO comments)
3. Missing imports or unused imports
4. Obvious logic errors a fresh pair of eyes might catch
5. Any hardcoded values that should be configurable

Report findings as a numbered list. If everything looks clean, say so."
```

If the review finds issues, fix them quickly (don't re-run the full pipeline — just fix, build, and verify tests pass).

### 7.3 Start the App

Start the backend and frontend servers so the user can test immediately:

**Backend:**
```bash
dotnet run
```
(Use `run_in_background` so it doesn't block)

**Frontend:**
```bash
npm start
```
(Use `run_in_background` so it doesn't block)

Poll for readiness — check that both servers are responding before declaring done.

### 7.4 Manual Testing Checklist

Generate a checklist based on the plan's requirements and test plan. Output it clearly:

```
Feature Ready for Manual Testing
=======================================
Feature:  $ARGUMENTS
Branch:   feature/<slug>
Tests:    All passing (X backend, Y frontend)

Servers running:
  Backend:  http://localhost:<port>
  Frontend: http://localhost:4200

Manual Testing Checklist:
  [ ] <requirement 1 from plan — describe what to verify>
  [ ] <requirement 2 from plan — describe what to verify>
  [ ] <edge case 1 — describe how to trigger and expected behavior>
  [ ] <edge case 2 — describe how to trigger and expected behavior>

Automated checks passed:
  [x] Backend builds without errors
  [x] Frontend builds without errors
  [x] All backend tests pass
  [x] All frontend tests pass
  [x] Code review (subagent) — clean
  [x] Optimization pass applied

When done testing:
  /commit-pr "<title>" to commit and create a PR
  /wrap-up to save learnings and close the session
=======================================
```

**This is the end of the pipeline. STOP here and let the user test.**
