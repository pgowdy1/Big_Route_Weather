# Skills

## Pipeline

```
/full-feature <desc>  — runs everything below automatically
/fresh-start  ->  /plan-feature  ->  /new-feature or /build-with-agent-team  ->  /test  ->  /optimize  ->  /test  ->  /commit-pr  ->  /review-pr  ->  /wrap-up
/fresh-start  ->  /fix  ->  /test  ->  /commit-pr  ->  /wrap-up
```

## Skills Reference

**`/full-feature <description>`** — End-to-end automated pipeline. Creates a branch, gathers requirements via Q&A, builds the feature, tests, optimizes, commits a PR, reviews it, and wraps up. The only user interaction is answering planning questions.

**`/fresh-start <description>`** — Checks for a clean working tree (asks before stashing/committing), pulls latest main, and creates a `feature/<slug>` branch.

**`/plan-feature <description>`** — Scans the codebase, asks 3 rounds of adaptive questions about layers/scope/UX/edge cases, then writes a structured plan to `.claude/plans/<slug>.md`. Recommends solo or team build based on complexity.

**`/new-feature [plan-path]`** — Solo implementation for small/medium features. Reads the plan, implements backend-first then frontend, and runs tests after each change.

**`/build-with-agent-team [plan-path] [num-agents]`** — Multi-agent build for complex features spanning 3+ layers. Spawns agents in dependency order with a contract-first protocol — upstream agents publish API contracts before downstream agents build.

**`/test`** — Runs backend (`dotnet test`) and frontend (`npm test`) test suites and reports pass/fail counts.

**`/optimize [file-or-scope]`** — Simplifies changed files since branching from main. Flattens conditionals, removes dead code, enforces Angular/C# conventions. Reverts anything that breaks tests.

**`/commit-pr <title>`** — Stages all changes, generates a conventional commit message, pushes to origin, and creates a PR with summary and grouped changes.

**`/review-pr [PR-number]`** — Reviews the PR diff for security, correctness, architecture, test coverage, and completeness. Posts findings as a PR comment with critical issues, suggestions, and what looks good.

**`/fix <bug description>`** — Spawns a debugger agent to investigate, then fixes directly or delegates to specialist agents. Runs tests to verify. No plan file needed.

**`/e2e-test`** — Opens a Playwright browser and walks through UI flows. Reports pass/fail per flow with screenshots.

**`/prime`** — Reads key project files (CLAUDE.md, models, entities, controllers) to bootstrap codebase context before starting work.

**`/wrap-up`** — End-of-session checklist: commits/pushes remaining changes, saves learnings to memory, and identifies skill gaps or friction to auto-improve.
