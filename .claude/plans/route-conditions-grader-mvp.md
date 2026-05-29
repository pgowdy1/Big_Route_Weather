# Plan: Route Conditions Grader (MVP)

**Branch:** `feature/route-conditions-grader-mvp`
**Scope:** Large — greenfield full-stack project
**Layers:** Frontend + Backend + Database

## Description

An MVP web app that grades (A–F) whether today is a good day to attempt a popular Colorado 14er route. Pulls weather forecasts from the National Weather Service and snowpack from USDA NRCS SNOTEL, runs a transparent heuristic, and shows a grid of cards — one per route — with the grade and the top 3–4 driving factors. Click a card to expand and see the full forecast and per-factor breakdown.

## MVP Routes

| # | Mountain | Route | Summit (ft) | Class | Lat,Lon (summit) | Closest SNOTEL |
|---|----------|-------|-------------|-------|------------------|----------------|
| 1 | Longs Peak | Keyhole | 14,259 | 3 | 40.2549, -105.6160 | Wild Basin (1042) |
| 2 | Capitol Peak | NE Ridge / Knife Edge | 14,137 | 4 | 39.1503, -107.0830 | Schofield Pass (737) |
| 3 | Pyramid Peak | NE Ridge | 14,025 | 4 | 39.0716, -106.9501 | Schofield Pass (737) |

(Coordinates and SNOTEL assignments are the seed values — verify and revise in seeding code.)

## Tech Stack (per CLAUDE.md)

- **Frontend:** Angular 21 (zoneless, signal-based), SCSS, Vitest
- **Backend:** ASP.NET Core (.NET 10), C#
- **Database:** SQLite via EF Core (migrations auto-apply on startup)

## Architecture

### Backend (3-project solution, mirrors WSU layout)

```
backend/
├── RouteWeather.sln (or .slnx)
├── RouteWeather.API/        # Controllers, Services, Program.cs, appsettings
│   ├── Controllers/RoutesController.cs
│   ├── Services/
│   │   ├── NwsClient.cs            # NWS API wrapper (typed HttpClient)
│   │   ├── SnotelClient.cs         # USDA NRCS SNOTEL wrapper
│   │   ├── ConditionsAggregator.cs # Fetch + cache orchestration per route
│   │   └── GradingService.cs       # Heuristic A–F grading
│   └── Program.cs
├── RouteWeather.Core/       # Domain models, grading rules, no I/O
│   ├── Models/
│   │   ├── Route.cs
│   │   ├── RouteConditions.cs
│   │   ├── FactorScore.cs
│   │   └── Grade.cs (enum A..F)
│   └── Grading/
│       ├── WindFactor.cs
│       ├── TemperatureFactor.cs
│       ├── PrecipitationFactor.cs
│       ├── RecentSnowFactor.cs
│       └── SnowpackFactor.cs
└── RouteWeather.Data/       # EF Core DbContext, repos, migrations
    ├── RouteWeatherContext.cs
    ├── Entities/
    │   ├── RouteEntity.cs
    │   └── CachedForecastEntity.cs
    ├── Repositories/
    │   ├── RouteRepository.cs
    │   └── ForecastCacheRepository.cs
    └── Migrations/
```

### Frontend (Angular 21, standalone components, signals)

```
frontend/
├── src/app/
│   ├── components/
│   │   ├── route-grid/         # Top-level grid of route cards
│   │   │   ├── route-grid.ts
│   │   │   ├── route-grid.html
│   │   │   └── route-grid.scss
│   │   ├── route-card/         # Single card with grade + driver pills
│   │   │   ├── route-card.ts
│   │   │   ├── route-card.html
│   │   │   └── route-card.scss
│   │   ├── route-card-detail/  # Expanded view (forecast table + factor breakdown)
│   │   └── grade-badge/        # Reusable A–F badge with color
│   ├── services/
│   │   └── routes-service.ts   # GET /api/routes, GET /api/routes/{slug}
│   ├── models/
│   │   ├── route.ts
│   │   ├── route-conditions.ts
│   │   └── grade.ts
│   ├── app.ts
│   ├── app.html
│   └── app.scss
├── angular.json
├── package.json
├── vitest.config.ts
└── tsconfig.json
```

## API Contract

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/routes` | GET | List all routes with **summary**: name, mountain, grade, top 3 driver pills, `updatedAt`, `isStale` |
| `/api/routes/{slug}` | GET | Single route **full detail**: summary + raw weather forecast (next 48h) + all factor scores + grade rationale |
| `/api/health` | GET | Liveness check |

### Response shapes (sketch)

```jsonc
// GET /api/routes → array of:
{
  "slug": "longs-peak-keyhole",
  "mountain": "Longs Peak",
  "routeName": "Keyhole",
  "summitElevationFt": 14259,
  "grade": "B",
  "drivers": [
    { "label": "High winds", "severity": "negative" },
    { "label": "Clear skies", "severity": "positive" },
    { "label": "Cold summit (12°F)", "severity": "negative" }
  ],
  "updatedAt": "2026-05-28T20:13:00Z",
  "isStale": false
}

// GET /api/routes/longs-peak-keyhole → above PLUS:
{
  "factors": [
    { "name": "Wind",            "score": 45, "weight": 0.25, "detail": "Sustained 35 mph at summit" },
    { "name": "Temperature",     "score": 60, "weight": 0.15, "detail": "Summit 12°F, dawn -2°F" },
    { "name": "Precipitation",   "score": 90, "weight": 0.20, "detail": "10% chance" },
    { "name": "Recent snow",     "score": 70, "weight": 0.20, "detail": "1.2\" in last 7 days" },
    { "name": "Snowpack (SWE)",  "score": 80, "weight": 0.20, "detail": "SNOTEL Wild Basin: 4.1\" SWE (95% of normal)" }
  ],
  "forecastNext48h": [ /* hourly entries */ ],
  "rationale": "Manageable conditions overall. Wind is the primary concern — consider an early start to summit before peak gusts."
}
```

## Heuristic Grading Model

Each factor returns a sub-score 0–100 (higher = better). Weighted average maps to letter:

| Total Score | Grade |
|---|---|
| 90–100 | A |
| 80–89  | B |
| 70–79  | C |
| 60–69  | D |
| < 60   | F |

Initial weights (sum = 1.0):

| Factor | Weight | Rationale |
|---|---|---|
| Wind (summit) | 0.25 | Most common turnaround reason on exposed ridges |
| Precipitation prob | 0.20 | Wet rock + lightning = nope on Class 3/4 |
| Recent snow | 0.20 | Fresh snow on rock = serious downgrade for Class 3/4 |
| Snowpack (SWE vs normal) | 0.20 | Tells if route is "in season" |
| Temperature (summit) | 0.15 | Frostbite floor; extreme heat ceiling rarely binds for 14ers |

Per-factor scoring (initial sketch — easy to tune):

- **Wind:** `<10mph → 100`, linear down to `>50mph → 0`
- **Temp:** Piecewise — `20–60°F → 100`, drops to `0` at `-20°F` or `>90°F`
- **Precip:** `100 - precipProb_%`
- **Recent snow:** `0" → 100`, `>6" in 7 days → 0` (lin)
- **Snowpack SWE:** Bell around 80–120% of normal → 100; tails to 0 at <30% or >200%

All factor logic lives in `RouteWeather.Core/Grading/` as pure functions — easy to unit test.

## Data Model

### Routes table (seeded, ~3 rows)

```csharp
public class RouteEntity {
    public int Id { get; set; }
    public string Slug { get; set; }
    public string Mountain { get; set; }
    public string RouteName { get; set; }
    public int SummitElevationFt { get; set; }
    public double SummitLat { get; set; }
    public double SummitLon { get; set; }
    public string ClassDifficulty { get; set; } // "3", "4", "5.4", etc.
    public string SnotelStationTriplet { get; set; } // e.g. "1042:CO:SNTL"
}
```

### CachedForecasts table

```csharp
public class CachedForecastEntity {
    public int Id { get; set; }
    public int RouteId { get; set; }
    public string Source { get; set; }   // "NWS" | "SNOTEL"
    public string PayloadJson { get; set; }
    public DateTime FetchedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
```

Composite index on `(RouteId, Source)`.

## Implementation Steps (ordered)

### Backend (do first — frontend consumes its contract)

1. **Scaffold solution & 3 projects** — `dotnet new sln`, `webapi`, two `classlib`s, wire references. Add EF Core packages to Data. Verify with `dotnet build`.
2. **Domain models in Core** — `Route`, `RouteConditions`, `FactorScore`, `Grade` enum, `WeatherSnapshot`, `SnowpackSnapshot`. Pure POCOs, no I/O.
3. **Grading factor functions in Core** — `WindFactor.Score(double mph)`, `TemperatureFactor.Score(double f)`, etc. Each is `public static int Score(...)`. Unit-testable in isolation.
4. **Aggregate grader in Core** — `GradeCalculator.Compute(WeatherSnapshot, SnowpackSnapshot) → (Grade letter, FactorScore[], rationale string)`.
5. **EF Core in Data** — `RouteWeatherContext` with `Routes` + `CachedForecasts` DbSets, configure EF, add initial migration. Auto-apply migrations on startup in API.
6. **Seed data** — `RouteSeeder` invoked from `Program.cs` after migrate, inserts the 3 MVP routes if none exist.
7. **NWS client in API/Services** — `NwsClient` (typed HttpClient): `GetPointAsync(lat, lon) → GridpointInfo`, `GetForecastAsync(grid)`. User-Agent header required by NWS. No API key. JSON response parsed to `WeatherSnapshot` (next 48h hourly).
8. **SNOTEL client in API/Services** — `SnotelClient`: hits AWDB REST API for recent SWE + snow depth + 7-day delta for a given station triplet. Returns `SnowpackSnapshot`.
9. **ConditionsAggregator** — Orchestrates per-route: check cache → if expired, fetch NWS + SNOTEL in parallel → write cache → return. On upstream failure, return last cached row with `isStale = true`.
10. **GradingService** — Thin wrapper that takes a route + freshly aggregated conditions and produces the response DTO.
11. **RoutesController** — `GET /api/routes` (summary list) and `GET /api/routes/{slug}` (detail). CORS allows `http://localhost:4200`.
12. **Health endpoint + minimal logging.**

**Verification:** `dotnet build` clean. `dotnet test` passes (unit tests for each factor function and the grade calculator). `curl http://localhost:<port>/api/routes` returns 3 routes with grades.

### Frontend (after backend contract verified)

13. **Scaffold Angular 21 app** — `ng new frontend --routing=false --style=scss --skip-tests=false`. Verify `npm start` boots.
14. **Models** — TypeScript interfaces matching API DTOs (`RouteSummary`, `RouteDetail`, `FactorScore`, `Driver`).
15. **RoutesService** — `routes()`, `routeDetail(slug)`. Uses `inject(HttpClient)`. Returns signals (`toSignal`) or plain observables — pick signals for consistency with zoneless.
16. **GradeBadge component** — Pure presentational, takes `grade: 'A'|'B'|...|'F'`, renders colored badge.
17. **RouteCard component** — Shows mountain name, route name, grade badge, driver pills, "updated Xm ago" + stale chip if applicable. `(click)` toggles expanded state (signal). When expanded, lazy-loads detail via service and renders the factor breakdown + forecast snippet.
18. **RouteGrid component** — Calls `RoutesService.routes()`, renders responsive grid of `RouteCard`s. Loading + error states.
19. **App shell** — `app.ts` renders `<route-grid>` with a header. SCSS theme: muted alpine palette.
20. **Proxy config** — `proxy.conf.json` so `/api/*` → `http://localhost:<backendPort>` in dev.

**Verification:** `npx ng build` clean. `npm test` passes. Browser shows 3 cards with real grades after backend is also running.

### Glue

21. **README updates** — Replace current placeholder with prerequisites + run commands.
22. **`.claude/rules/testing.md`** — Replace stale carryover rules with project-specific guidance (HttpClientTesting, signal input setup, RouteSummary fixture completeness). Refresh the agent-identity lines in `.claude/agents/*.md` so they describe this project.

## Edge Cases & Error Handling

| Scenario | Behavior |
|---|---|
| NWS returns 503 or times out | Return last cached forecast with `isStale=true`; rationale notes data age. Log warning. |
| SNOTEL station offline | Same fail-open. Snowpack factor falls back to last known SWE; if no cached value, factor weight is redistributed across remaining factors. |
| Both sources down + empty cache | Grade is `null`; UI shows "—" and "Data unavailable" chip. |
| Route has invalid coordinates | Startup seeder validates lat/lon; fails fast with a clear error. |
| Cache concurrency (two simultaneous requests for same route) | First request triggers fetch; second awaits same task via `SemaphoreSlim` keyed by route slug. |
| Frontend offline / backend unreachable | Service catches HttpErrorResponse; grid shows "Can't reach backend" with retry button. |

## Test Plan

### Backend unit tests (`RouteWeather.Core.Tests`)

- `WindFactor_Score_returns100_atZeroMph`
- `WindFactor_Score_returns0_at60Mph`
- `WindFactor_Score_isMonotonicallyDecreasing`
- Same shape for Temperature, Precipitation, RecentSnow, Snowpack factors
- `GradeCalculator_returnsA_whenAllFactorsPerfect`
- `GradeCalculator_returnsF_whenAllFactorsTerrible`
- `GradeCalculator_weightsAddUpToOne`
- `GradeCalculator_omittedFactor_redistributesWeight`

### Backend integration tests (`RouteWeather.API.Tests`)

- `RoutesController_GetAll_returns3Routes`
- `RoutesController_GetBySlug_returnsDetailIncludingFactors`
- `ConditionsAggregator_returnsStaleOnUpstreamFailure` (mock HttpClient handler returns 503)

### Frontend tests (Vitest)

- `RouteCard_rendersGradeBadge`
- `RouteCard_clickExpands_loadsDetail`
- `GradeBadge_appliesColorClass_perGrade`
- `RouteGrid_showsErrorState_whenServiceFails`

## Verification Commands

```bash
# Build
dotnet build backend/RouteWeather.sln
cd frontend && npx ng build

# Test
dotnet test backend/RouteWeather.sln
cd frontend && npm test

# Run
dotnet run --project backend/RouteWeather.API
cd frontend && npm start

# Smoke-test API
curl http://localhost:5000/api/routes | jq '.[] | {mountain, grade, drivers}'
curl http://localhost:5000/api/routes/longs-peak-keyhole | jq '.factors'
```

## Complexity Assessment

**Recommend solo build** (this single pipeline). Scope is large by line count but the slices are independent and sequential (backend then frontend), not parallel-friendly enough to justify the contract-chain ceremony of an agent team. Two pieces of mild risk: NWS gridpoint resolution (cache the gridpoint per route on first fetch) and the SNOTEL endpoint (verify the AWDB API URL shape before integrating).

## Out of MVP (defer)

- Reddit / Mountain Project / 14ers scraping
- User accounts / saved routes
- Multi-day grade forecast (the model only grades "today" for now)
- Avalanche forecasts (CAIC)
- Lightning model beyond raw precip prob
- Email/SMS alerts when grade ≥ B
- More routes (next: Maroon Bells N. Face, Snowmass, Crestone Needle…)
