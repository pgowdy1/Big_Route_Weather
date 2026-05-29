# Frontend Testing Rules

Frontend tests use **Vitest + jsdom** (Angular 21 default), not Karma/Jasmine.

## Running tests
- Just `npm test` — vitest runs once and exits by default.
- Do NOT pass `--watch=false`, `--browsers=ChromeHeadless`, or other Karma flags. They throw "Unknown argument" or require @vitest/browser-* peer deps the project doesn't install.

## HTTP mocking
- Use `provideHttpClient()` + `provideHttpClientTesting()` together in `TestBed.configureTestingModule({ providers: [...] })`. Inject `HttpTestingController` to assert URL/method and `flush()` a response or `error()` it.
- After each test, call `httpMock.verify()` in `afterEach` to catch unmatched or extra HTTP requests.

## Signal inputs in tests
- For components using `input.required<T>()` / `input<T>()`, call `fixture.componentRef.setInput('name', value)` before the first `detectChanges()`. Assigning to the signal property directly will throw.

## RouteSummary fixtures
- Specs that build a `RouteSummary` object literal must include every field on the interface (`slug`, `mountain`, `routeName`, `summitElevationFt`, `classDifficulty`, `grade`, `overallScore`, `drivers`, `updatedAt`, `isStale`). TypeScript will catch omissions, but inline builders are common — keep them current as the contract evolves.
