# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Big_Route_Weather** — a grade card for conditions on big routes on big objectives.

## Tech Stack

- **Frontend:** Angular (zoneless, signal-based), SCSS
- **Backend:** ASP.NET Core (.NET 10), C#
- **Database:** TBD

## Branching

Work flows **feature → dev → main**:
- Feature branches base off `dev`, never `main`.
- PRs target `dev`. Merges to `dev` auto-deploy to a Cloudflare Pages preview at `https://dev.<project>.pages.dev` for verification.
- Once verified on the dev preview, open a `dev` → `main` PR to ship to production.
- The slash commands `/fresh-start`, `/full-feature`, `/commit-pr`, `/optimize`, and `/review-pr` follow this policy automatically.

## Commands

### Backend
```bash
dotnet build                    # Build
dotnet run                      # Run
dotnet test                     # Test
```

### Frontend (run from `frontend/` directory)
```bash
npm start       # Dev server (http://localhost:4200)
npm run build   # Production build
npm test        # Unit tests — Vitest + jsdom, NOT Karma. Do NOT pass --watch=false or --browsers=...; vitest runs once by default.
```

## Agent Delegation

**CRITICAL**: When the user requests delegating to a specific agent (e.g., "backend agent, do X"), **immediately check if that agent exists** in the available agents list. If the agent does NOT exist, **immediately tell the user** that the agent doesn't exist and show them the list of available agents. Do NOT attempt to call the Task tool with a non-existent agent.

Available specialized agents for this project:
- `backend-dev` — C# and ASP.NET Core tasks
- `frontend-dev` — Angular tasks
- `debugger` — Debugging issues
- `code-reviewer` — Code review before merging
- `code-simplifier` — Post-build code cleanup
- `git-security` — Secret detection and git hygiene
- `readme-writer` — Documentation
- `software-architect` — Architecture and planning
- `Explore` — Codebase exploration
- `Plan` — Implementation planning
