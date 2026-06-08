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
- **Lean on specialized agents where it makes sense -- research, code-review, optimization.** This pipeline is designed to delegate:
  - `software-architect` — Phase 2 reconnaissance + helping shape the questions to ask. Pass the original prompt for the skill and let the software-architect think about how it makes sense in the context of the system.
  - `code-simplifier` — Phase 5 optimization pass on changed files. Pass all of the files and important context for changes done for a full optimization run for this agent.
  - `code-reviewer` — Phase 7 fresh-context diff review. Have this subagent do a review of all the changes that were done for this feature. Explicitly pass the files to the agent for review.
  - Stay in the driver's seat: brief each agent with the full context they need (it's a fresh context — they haven't seen this conversation), verify their work, and never delegate understanding.

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

### 1.2 Sync with Dev

Feature branches base off `dev`, not `main`. `dev` is the staging environment that auto-deploys to Cloudflare Pages preview — every PR ships there first and is verified before being promoted to `main` (production).

```bash
git checkout dev
git pull origin dev
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

### 2.1 Architect-Led Reconnaissance

Before asking questions — **DO NOT IMPLEMENT YET**.

Read `CLAUDE.md` yourself first (it's small and you need it loaded), then delegate the deeper codebase reconnaissance to the `software-architect` agent. The architect has fresh context, so the prompt must be fully self-contained.

```
Agent: software-architect
Description: Plan reconnaissance for "$ARGUMENTS"
Prompt:
  We're about to plan a new feature: "$ARGUMENTS".

  Do a reconnaissance pass on this codebase to inform the plan. Specifically:
  1. Identify the layers this feature is likely to touch (frontend / backend / DB / config / docs).
  2. Find the existing files, components, services, endpoints, and models most relevant to this work — prefer reuse over net-new. List them with file paths.
  3. Surface the conventions and rules that will constrain implementation (CLAUDE.md, .claude/rules/*, any patterns visible in neighboring code).
  4. Call out architectural risks, gotchas, or hidden coupling that the user should weigh before we lock the plan.
  5. Propose 4-8 targeted clarifying questions the user should answer before we implement — questions that would actually change the design, not generic checklist items. Group them as Foundational / Targeted / Edge-case.

  Do NOT write code or modify files. Return a structured report:
  - Layers touched
  - Relevant existing files (with paths)
  - Conventions to follow
  - Risks / gotchas
  - Suggested clarifying questions (grouped)

  Keep it focused — under ~400 lines.
```

Read the architect's report carefully. **You** own the plan and the questions you ask the user — the architect is advising, not deciding.

### 2.2 Round 1 Questions (Foundational)

Use AskUserQuestion to ask 3-4 questions. Start with these defaults, but **replace or refine any of them with the architect's foundational questions** when those are more specific to the feature:

1. **Layers** — "Which layers does this feature touch?"
   - Options: Frontend only, Backend only, Full-stack (frontend + backend), Full-stack + database changes
2. **Scope** — "How would you describe the scope?"
   - Options: Small (single component/endpoint tweak), Medium (new component or endpoint), Large (multiple components, endpoints, and/or data model changes)
3. **UX intent** — "What should the user experience look like?" (open-ended)
4. **Existing vs new** — "Are we modifying existing functionality or building something net new?"
   - Options: Modify existing, Build new, Both

### 2.3 Round 2 Questions (Targeted)

Based on Round 1 answers + the architect's targeted questions, ask 2-4 follow-ups relevant to the layers/scope:
- **Frontend:** Component placement, UI patterns (dialog, inline, sidebar, page)
- **Backend:** New endpoints, external service integration needs
- **Database:** Data shapes, indexing requirements
- **Large scope:** Milestones, MVP, dependencies between pieces

### 2.4 Round 3 Questions (Edge Cases)

1. **Error scenarios** — what should happen when things go wrong?
2. **Deployment impact** — only ask if new files/deps/config are involved
3. Any edge-case questions the architect flagged

### 2.5 Produce the Plan

Write the plan to `.claude/plans/<slug>.md` yourself, incorporating the architect's findings + the user's answers. Include:
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

If a step persists after 2 fix attempts (whether self or via agent):
1. **Stop editing the problematic file**
2. **Assess**: Is the approach fundamentally wrong, or is it a small mistake?
3. **If wrong approach**: Revert and try a different strategy
4. **If stuck**: Report clearly what's failing, what you tried, what the agent (if any) returned, and ask the user for direction

Do NOT enter a loop of progressively worse fixes.

**After build completes and compiles cleanly across all layers, immediately proceed to Phase 4.**

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

## Phase 5: Optimize (code-simplifier)

### 5.1 Determine Scope

Only optimize files changed in this feature:
```bash
git diff --name-only dev...HEAD
```

Capture that list — it's the exact scope to hand to the simplifier.

### 5.2 Delegate to code-simplifier

Spawn the `code-simplifier` agent with the changed-files list. The agent has fresh context, so include the conventions it needs.

```
Agent: code-simplifier
Description: Post-build cleanup on feature diff
Prompt:
  Feature: $ARGUMENTS
  Branch: feature/<slug>

  Simplify ONLY these recently modified files (don't touch anything else):
  <paste output of `git diff --name-only dev...HEAD`>

  Conventions to enforce:
  - CLAUDE.md (Angular 21 zoneless + signals; ASP.NET .NET 10; 3-project layout)
  - .claude/rules/ — read any rule file scoped to the paths above
  - Angular: signals not RxJS subjects, @if/@for not *ngIf/*ngFor
  - EF Core: AsNoTracking() on read paths; IDbContextFactory for any parallel/fan-out
  - Default to NO comments; only keep "why" comments that document non-obvious reasoning
  - Respect SCSS budgets (peak-detail.scss is tight — compact, don't grow)

  Look for and fix:
  - Deeply nested conditionals -> flatten with early returns / guard clauses
  - Unused imports, variables, parameters
  - Over-engineered abstractions for one-time operations
  - Convention violations
  - Redundant null checks past framework guarantees

  Do NOT change:
  - Working clear logic just for style
  - "Why" comments
  - Test assertions (don't weaken them)
  - Public API contracts — method signatures, endpoint shapes, response JSON

  After simplifying, run:
  - `dotnet build` (if backend files changed)
  - `npm test` (if frontend files changed — vitest runs once, no flags)
  - `npx ng build 2>&1 | tail -20` (if frontend files changed)

  Return:
  1. List of files actually modified
  2. One-line summary per file of what was simplified
  3. Build/test output proving nothing broke
  4. Any change you considered but skipped because it would have broken tests or violated a convention
```

### 5.3 Review the Simplifier's Output

When the agent returns:
- Skim the changed files to confirm the edits are surgical (no scope creep, no rewrites of working logic)
- Confirm the build/test output it pasted actually shows green
- If anything looks wrong, send a focused follow-up to the same agent — don't fix it yourself

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
Prompt: "Review the changes on this branch vs dev.
Run: git diff dev...HEAD

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
