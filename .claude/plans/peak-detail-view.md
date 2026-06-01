# Peak Detail View + Multi-Window Grades

**Branch:** `feature/peak-detail-view`
**Scope:** Medium-Large (full-stack, +1 frontend route, +1 backend computed payload, no DB changes)
**Layers:** Frontend (Angular + new route + chart lib) and Backend (.NET grading + SNOTEL series)
**Complexity:** Recommend **solo build** (`/new-feature` path) — incremental, well-bounded; no need for an agent team.

## Goal

Clicking a route card on the grid navigates to a dedicated page at `/peak/:slug`. The page shows:
1. Route header (mountain · route · class · summit · summit lat/lon · back link to grid)
2. **Three grades side by side: next 12h / 24h / 48h**, each a `GradeBadge`
3. Per-window factor breakdowns (so user can see *why* 12h is a B but 48h is a D)
4. Full 48-hour forecast table (all rows, not the current 12-row slice)
5. Detailed snowpack panel: SWE, depth, new snow (7d), % of normal SWE, **SNOTEL station name + last-fetched timestamp + 7-day depth sparkline**
6. Cache freshness per source (NWS updated 23m ago · SNOTEL updated 3h ago)
7. The inline expand on `RouteCard` goes away — the card becomes a link.

## Requirements

- `GET /api/routes/{slug}` returns the new fields (`grades.next12h / next24h / next48h`, expanded `snowpack`, per-source freshness).
- Per-window grade is computed by re-running `GradeCalculator` against a `WeatherSnapshot` derived from the first N hours of `Next48Hours` (N = 12, 24, 48). Snowpack is shared across windows.
- Partial-forecast handling: compute the grade with whatever's available and include `hoursCovered` so the UI can badge it.
- Frontend uses Angular Router (`provideRouter`) — first time in this app.
- Sparkline uses **chart.js** (per user choice) loaded via `afterRenderEffect` so it plays nicely with zoneless change detection.
- Existing list endpoint `/api/routes` keeps its current 48h summary grade — no contract change for the grid view.

## Affected files

### Backend (existing — modify)
- `backend/RouteWeather.API/Controllers/RoutesController.cs` — extend `ToDetail` with new fields
- `backend/RouteWeather.API/Services/SnotelClient.cs` — return the daily SNWD series (not just latest)
- `backend/RouteWeather.API/Services/ConditionsAggregator.cs` — surface per-source `fetchedAt` to caller
- `backend/RouteWeather.Core/Models/SnowpackSnapshot.cs` — add `StationTriplet`, `DailyDepthIn` series
- `backend/RouteWeather.Core/Models/RouteConditions.cs` — add `WindowGrades` and per-source freshness

### Backend (new)
- `backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs` — slices `Next48Hours` into 12/24/48h windows, runs `GradeCalculator.Compute` per slice
- `backend/RouteWeather.Core.Tests/Grading/WindowGradeCalculatorTests.cs`

### Frontend (existing — modify)
- `frontend/src/app/app.config.ts` — add `provideRouter`
- `frontend/src/app/app.ts` + `app.html` — switch to `RouterOutlet`
- `frontend/src/app/components/route-card/route-card.ts` + `.html` + `.scss` — strip inline expand, make the header a `routerLink` to `/peak/:slug`
- `frontend/src/app/components/route-card/route-card.spec.ts` — drop expand-related cases (if any), add navigation/link assertion
- `frontend/src/app/services/routes-service.ts` — no signature change; just consumes the richer `RouteDetail`
- `frontend/src/app/models/route-conditions.ts` — extend interfaces (`WindowGrade`, `SnowpackSnapshot`, `RouteDetail`)
- `frontend/package.json` — add `chart.js`

### Frontend (new)
- `frontend/src/app/pages/peak-detail/peak-detail.ts` + `.html` + `.scss` + `.spec.ts`
- `frontend/src/app/components/sparkline/sparkline.ts` + `.spec.ts`

## API contract (`GET /api/routes/{slug}`)

```jsonc
{
  "slug": "...",
  "mountain": "...",
  "routeName": "...",
  "summitElevationFt": 14047,
  "summitLat": 37.124,
  "summitLon": -105.186,
  "classDifficulty": "2",
  "grade": "C",                         // unchanged — 48h overall (back-compat)
  "overallScore": 71,
  "drivers": [...],
  "factors": [...],
  "rationale": "...",
  "updatedAt": "...",                   // unchanged — max of per-source
  "isStale": false,
  "forecastNext48h": [...],
  "snowpack": {
    "snowWaterEquivalentIn": 0.0,
    "snowDepthIn": 0.0,
    "newSnowLast7DaysIn": 0.0,
    "percentOfNormalSwe": 100,
    "stationTriplet": "1234:CO:SNTL",   // NEW
    "dailyDepthIn": [                   // NEW — chronological, oldest first
      { "date": "2026-05-25", "depthIn": 3.2 },
      ...
    ]
  },
  "windowGrades": {                     // NEW
    "next12h": { "grade": "B", "overallScore": 82, "hoursCovered": 12, "factors": [...], "rationale": "..." },
    "next24h": { "grade": "C", "overallScore": 74, "hoursCovered": 24, "factors": [...], "rationale": "..." },
    "next48h": { "grade": "C", "overallScore": 71, "hoursCovered": 48, "factors": [...], "rationale": "..." }
  },
  "sources": {                          // NEW
    "nws":    { "fetchedAt": "..." },
    "snotel": { "fetchedAt": "..." }
  }
}
```

`hoursCovered` may be smaller than the requested window if NWS returned fewer periods.

## Data model changes

None at the DB level. `ForecastCache` payloads change shape (SNOTEL now includes daily depths) — but the cache is opaque JSON and self-healing on TTL expiry, so no migration is required.

## Implementation steps (backend-first)

1. **Backend models** — add `StationTriplet` and `DailyDepthIn` to `SnowpackSnapshot`. Add `WindowGrade` record and `WindowGrades(Next12h, Next24h, Next48h)`. Extend `RouteConditions` with `WindowGrades` and `Sources(NwsFetchedAt, SnotelFetchedAt)`.
2. **SnotelClient** — return the full daily SNWD list (chronological). Keep latest-SWE behavior. Include `stationTriplet` in the snapshot.
3. **WindowGradeCalculator** — given `WeatherSnapshot? full, SnowpackSnapshot? snow`, produce a `WindowGrade` for each (12, 24, 48) by slicing `full.Next48Hours.Take(n)`, re-aggregating wind/temp/precip (Max wind, Min temp, Max precip), and calling `GradeCalculator.Compute`. Set `HoursCovered = min(n, available)`.
4. **ConditionsAggregator** — compute `WindowGrades` after `GradeCalculator.Compute`. Surface per-source fetched timestamps onto `RouteConditions.Sources`.
5. **RoutesController.ToDetail** — emit the new payload shape (snake_to_camel via `JsonNamingPolicy.CamelCase` is already configured by ASP.NET Core defaults).
6. **Build + xunit tests pass** — checkpoint.
7. **Frontend models** — extend TS interfaces to match contract.
8. **Router wiring** — `provideRouter([{path: '', component: RouteGrid}, {path: 'peak/:slug', component: PeakDetail}])` in `app.config.ts`; `App` template becomes `<router-outlet />`.
9. **RouteCard** — turn the header into `<a [routerLink]="['/peak', route().slug]">`. Remove `expanded` signal, `detail()` signal, `toggle()`, and the inline `<section class="detail">` block entirely.
10. **Install chart.js** — `npm install chart.js`. Confirm bundle still builds.
11. **Sparkline component** — input: numeric series; renders a tiny line chart on a canvas with `afterRenderEffect`. Tooltip disabled, axes hidden, gradient line. ~50 LOC.
12. **PeakDetail component** — reads `slug` from route param via `input()` (`withComponentInputBinding()` in router config). Calls `RoutesService.detail(slug)`. Renders header → window-grades strip → per-window factors → full 48h forecast → snowpack panel with sparkline → sources footer. Handles loading and 404.
13. **Frontend build + vitest pass** — checkpoint.

## Edge cases

- **NWS returns < 12h periods**: every window's `hoursCovered < requested`. UI shows `(partial — Nh)` chip next to the grade.
- **NWS returns null**: no window grades; show "Forecast unavailable" and only the snowpack panel.
- **SNOTEL returns null**: no snowpack panel; window grades computed from weather only (existing GradeCalculator handles `snowpack: null`).
- **Daily depth series is empty**: hide sparkline, keep numeric fields.
- **Unknown slug**: backend returns 404, frontend shows "Peak not found" with back link.
- **Cache miss + upstream slow**: existing semaphore + cache flow handles it; detail page just sees the normal loading state longer.
- **Sparkline canvas after navigation**: destroy chart instance on cleanup so navigating between peaks doesn't leak.

## Test plan

### Backend (xunit, runs via `dotnet test`)
- `WindowGradeCalculatorTests`
  - 12h grade differs from 48h grade when a late-window precip spike exists (regression on the bug user reported).
  - Equal grades across windows when forecast is uniform.
  - Returns null/zero grade when `Next48Hours` is empty.
  - `HoursCovered` reflects actual count when forecast < requested.
  - Snowpack-only (weather null) still produces grades, all with `HoursCovered = 0`.
- `GradeCalculatorTests` — existing pass unchanged.

### Frontend (vitest + jsdom, runs via `npm test`)
- `RouteCard` — clicking the card produces a `[routerLink]` to `/peak/<slug>`; no inline expand on click.
- `PeakDetail` —
  - Loads detail on init when route param is set; uses `provideHttpClientTesting` to stub `/api/routes/<slug>`.
  - Renders three `GradeBadge`s for window grades.
  - Renders all 48 forecast rows when supplied.
  - Sparkline component renders the snowpack series count.
  - Shows "Peak not found" on 404.
- `Sparkline` — given `[1,2,3,4]` renders a `<canvas>` and exposes its data length.

## Verification commands

Backend builds + tests:
```bash
cd backend && dotnet build && dotnet test --verbosity normal
```

Frontend builds + tests:
```bash
cd frontend && npx ng build && npm test
```

End-to-end smoke (manual):
```bash
# Terminal 1
cd backend && dotnet run --project RouteWeather.API
# Terminal 2
cd frontend && npm start
# Browser http://localhost:4200 → click any peak → verify detail page
```
