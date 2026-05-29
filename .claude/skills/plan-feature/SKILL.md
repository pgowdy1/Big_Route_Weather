---
name: plan-feature
description: Interactive requirements gathering and plan creation for a new feature
argument-hint: <feature-description>
disable-model-invocation: true
---

# Plan Feature

You are gathering requirements for a new feature through adaptive questioning, then producing a structured plan document that `/build-with-agent-team` or `/new-feature` can consume.

## Phase 1: Understand the Feature

Read the feature description from `$ARGUMENTS`. Before asking questions, do quick codebase reconnaissance:

1. Read `CLAUDE.md` for current architecture and conventions
2. Use Grep/Glob to scan for files related to the feature description
3. Identify which existing components, services, endpoints, and models are relevant

This gives you context to ask smarter questions.

## Phase 2: Round 1 Questions (Foundational)

Use AskUserQuestion to ask 3-4 foundational questions. These are always asked regardless of the feature:

1. **Layers** — "Which layers does this feature touch?"
   - Options: Frontend only, Backend only, Full-stack (frontend + backend), Full-stack + database changes

2. **Scope** — "How would you describe the scope?"
   - Options: Small (single component/endpoint tweak), Medium (new component or endpoint), Large (multiple components, endpoints, and/or data model changes)

3. **UX intent** — "What should the user experience look like?"
   - This should be an open-ended question. Let the user describe the interaction in their own words.

4. **Existing vs new** — "Are we modifying existing functionality or building something net new?"
   - Options: Modify existing, Build new, Both (extend existing with new pieces)

## Phase 3: Round 2 Questions (Targeted Follow-ups)

Based on Round 1 answers, ask 2-4 targeted follow-ups. Only ask questions relevant to the layers and scope identified:

**If Frontend is involved:**
- Which existing component(s) should this live in, or is a new component needed?
- Any specific UI patterns to follow (dialog, inline, sidebar, page)?

**If Backend is involved:**
- New API endpoint needed? What should it do at a high level?
- Does this need any external service integration?

**If Database is involved:**
- New data to store? Describe the shape (new entity vs new columns on existing).
- Any indexing or search requirements?

**If scope is Large:**
- Can you break this into smaller milestones? What's the MVP?
- Are there dependencies between the pieces (what must be built first)?

## Phase 4: Round 3 Questions (Edge Cases & Testing)

Ask 1-2 final questions:

1. **Error scenarios** — "What should happen when things go wrong?" (e.g., API fails, empty results, invalid input). Let user describe or say "use sensible defaults."

2. **Deployment impact** — Only ask if the feature adds new files, dependencies, or configuration: "Does this affect deployment or packaging?"

## Phase 5: Produce the Plan

Generate a slug from `$ARGUMENTS` (lowercase, hyphens, ~40 chars).

Write a structured plan file to `.claude/plans/<slug>.md` with this format:

```markdown
# Feature: <feature name>

**Branch:** feature/<slug>
**Scope:** <small | medium | large>
**Layers:** <frontend | backend | full-stack | full-stack + database>

## Description

<2-3 sentence summary of what we're building and why>

## Requirements

<Bulleted list of specific requirements gathered from the Q&A>

## Affected Files

### Existing (modify)
- `path/to/file.ts` — what changes
- `path/to/file.cs` — what changes

### New (create)
- `path/to/new-file.ts` — purpose
- `path/to/new-file.cs` — purpose

## API Contract

<Only if backend is involved. Define endpoints, methods, request/response shapes.>

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/api/...` | POST | `{ ... }` | `{ ... }` |

## Data Model Changes

<Only if database is involved. Describe new entities, columns, migrations.>

## Implementation Steps

<Ordered list of implementation steps. Group by layer if full-stack.>

### Backend
1. ...
2. ...

### Frontend
1. ...
2. ...

### Database
1. ...
2. ...

## Edge Cases & Error Handling

<Bulleted list of error scenarios and how to handle them>

## Test Plan

- [ ] Backend: <what to test>
- [ ] Frontend: <what to test>
- [ ] Integration: <what to test>

## Complexity Assessment

**Recommended execution:** `/new-feature` (solo) | `/build-with-agent-team` (team)
**Rationale:** <why this complexity level>
```

## Phase 6: Present Summary

After writing the plan file, output a concise summary to the conversation:

```
Plan saved to: .claude/plans/<slug>.md

Summary: <1-2 sentences>
Layers: <layers>
Scope: <scope>
Files affected: <count>
Recommended: /new-feature or /build-with-agent-team

Review the plan, then run the recommended skill to execute it.
```
