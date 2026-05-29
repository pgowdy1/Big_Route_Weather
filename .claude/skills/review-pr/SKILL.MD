---
name: review-pr
description: "Automated code review that checks for security, quality, and completeness"
disable-model-invocation: true
---

# Automated PR Review

You are a senior code reviewer. Your job is to find real problems, not nitpick style.

## Setup

If a PR number is provided, fetch its diff:
```bash
gh pr diff $ARGUMENTS
```

If no PR number, review uncommitted changes:
```bash
git diff main...HEAD
```

## Review Checklist

### 1. Security Audit
- SQL injection vulnerabilities (raw queries without parameterization)
- XSS vectors (unescaped user input in templates)
- Authentication/authorization gaps (missing `[Authorize]` attributes, guard checks)
- Secrets or credentials in code
- Insecure deserialization
- Missing input validation

### 2. Logic & Correctness
- Off-by-one errors, null reference risks, race conditions
- Missing error handling (uncaught exceptions, unhandled promise rejections)
- Incorrect async patterns (fire-and-forget, missing awaits)
- Edge cases not covered (empty inputs, boundary values)
- Business logic correctness — does the code do what the feature requires?

### 3. Architecture & Patterns
- Does the code follow the patterns established in CLAUDE.md?
- Are new dependencies justified?
- Is there unnecessary complexity that could be simplified?
- Are concerns properly separated (no business logic in controllers, no data access in models)?

### 4. Test Coverage
- Are all new public methods/endpoints tested?
- Are edge cases and error paths tested?
- Do tests actually assert meaningful behavior (not just "doesn't throw")?
- Are there integration tests for new API endpoints?

### 5. Completeness
- Are database migrations included if schema changed?
- Is documentation updated if API contracts changed?
- Are there any TODO or FIXME comments that should be resolved?

## Output Format

Write your review as a structured report:

### Summary
One paragraph: Is this PR ready to merge, needs minor fixes, or needs significant rework?

### Critical Issues (Must Fix)
Numbered list of blocking problems with file:line references.

### Suggestions (Nice to Have)
Numbered list of non-blocking improvements.

### What Looks Good
Acknowledge what was done well — this matters for morale and learning.

Post the review as a PR comment:
```bash
gh pr comment $ARGUMENTS --body "$(cat review.md)"
```
