# Frontend Testing Rules

Frontend tests use **Vitest + jsdom** (Angular 21 default), not Karma/Jasmine.

## Running tests
- Just `npm test` — vitest runs once and exits by default.
- Do NOT pass `--watch=false`, `--browsers=ChromeHeadless`, or other Karma flags. They throw "Unknown argument" or require @vitest/browser-* peer deps the project doesn't install.

## Canvas mocking
- jsdom does not implement `HTMLCanvasElement.getContext()`. Tests that exercise the renderer use a hand-rolled `MockCtx` class with `fillStyle`, `fillRect`, `save`, `restore`, `scale` — see `src/app/rendering/wall-renderer.spec.ts` for the pattern.
- Do NOT install the `canvas` npm package to fix this — the mock is intentional and faster.

## Route fixtures in specs
- Any `Route` object literal in a spec must include `rockType` (e.g., `'granite'`). The TS compiler will catch missing fields, but lint-style helpers in spec files often build routes inline — keep them current.
