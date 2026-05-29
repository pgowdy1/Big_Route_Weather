---
paths:
  - "frontend/**"
---

# Angular Frontend Rules

## Forms
- Never use `(ngSubmit)` without importing `FormsModule` — Angular silently ignores it and the native form submit fires, causing a page reload
- When using raw `[value]` + `(input)` bindings (no `ngModel`), use `(submit)="$event.preventDefault(); handler()"` instead of `(ngSubmit)`
- Only import `FormsModule` if actually using `ngModel` directives
