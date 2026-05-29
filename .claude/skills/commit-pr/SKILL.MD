---
name: commit-pr
description: "Stage, commit, push, and create a PR with a well-formatted description"
disable-model-invocation: true
argument-hint: <pr-title>
---

# Commit and Create PR

## Steps

1. Stage all changes:
```bash
git add -A
```

2. Generate a commit message by analyzing the diff:
```bash
git diff --cached --stat
git diff --cached
```

Write a conventional commit message:
- `feat:` for new features
- `fix:` for bug fixes
- `refactor:` for restructuring
- `test:` for test additions
- `docs:` for documentation

3. Commit:
```bash
git commit -m "<type>: $ARGUMENTS"
```

4. Push:
```bash
git push -u origin HEAD
```

5. Create the PR:

Generate a PR body that includes:
- **Summary**: What this PR does in 2-3 sentences
- **Changes**: Bullet list grouped by area (Backend, Frontend, Tests)
- **Testing**: What was tested and how
- **Screenshots**: Note if UI changes need screenshots
```bash
gh pr create --title "$ARGUMENTS" --body "$(cat /tmp/pr-body.md)" --base main
```

6. Output the PR URL for the user.
