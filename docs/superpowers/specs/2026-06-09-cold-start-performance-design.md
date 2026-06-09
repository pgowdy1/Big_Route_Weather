# Cold-Start Performance: Warm Loop + Serve-Stale Design

**Date:** 2026-06-09
**Branch:** `feature/cold-start-performance`
**Status:** Approved

## Problem

A fully cold visit (Fly machine stopped, memory cache empty, SQLite source cache
expired) takes **60–90 seconds** before grades appear on the map. The time goes to:

1. Fly machine boot + .NET init + EF migrations (~3–6s) — `fly.toml` has
   `min_machines_running = 0` with `auto_stop_machines = "stop"`.
2. Conditions aggregation: `/api/routes` fans out 87 routes × ~6 weather sources
   through an 8-wide semaphore with 15s per-source timeouts
   (`ConditionsAggregator.cs`, `RoutesController.cs`).
3. The Pages Function proxy (`frontend/functions/api/[[path]].ts`) is a raw
   passthrough — `Cache-Control` headers only help each returning browser, and
   there is no stale copy anywhere to serve while the backend works.

The perceived-performance levers (ghost markers, progressive paint, loading chip —
PRs #22–25) are already pulled. Meanwhile last-known forecasts sit in SQLite on a
persistent Fly volume, unused once their 1–3h TTLs lapse.

## Goals

- A cold-start visitor sees graded markers (marked stale) in **~2–3s**, not 60–90s.
- Fresh data replaces stale data automatically, with no user action and no new UI.
- User requests are **never** on the critical path of upstream weather APIs.
- Upstream call volume does not increase (still bounded by per-source SQLite TTLs).

## Non-Goals

- Edge caching in the Pages Function (per-colo caches are rarely warm at current
  traffic; the always-on machine removes the outage window it would cover). Can be
  a later feature if Fly reliability becomes a problem.
- Reducing the duration of aggregation itself (batched upstream calls, payload
  splits, tighter timeouts).
- Polling/refetch on the peak-detail page (it has the manual refresh button and the
  muted stale chip).

## Architecture

**Core inversion:** today the user's request triggers upstream fetches. After this
change, upstream fetches happen only in a background warmer loop; user requests are
pure reads.

```
ConditionsWarmerService (on startup, then every 10 min)
    └─ aggregation for all 87 routes (8-wide semaphore)
       ├─ respects SQLite per-source TTLs (no extra upstream volume)
       ├─ fetches expired sources upstream (15s timeouts, as today)
       └─ writes results → IMemoryCache (TTL 30 min)

User request (GET /api/routes, GET /api/routes/{slug})
    └─ IMemoryCache hit?  → return            (the ~always case once warm)
    └─ miss?              → last-known SQLite rows (one bulk query)
                            ├─ rows ≤ 24h old → graded, IsStale = true
                            ├─ rows > 24h old → source treated as missing
                            └─ zero usable rows → grade: null (ghost marker)
       Never fetches upstream. Worst case ≈ 2–3s after a deploy, not 60–90s.
```

**Who may fetch upstream:** the warmer only. Ordinary GETs may not. (Implementation
note: there is no server-side `/refresh` endpoint in the current code — the `/all`
page's Refresh button re-GETs `/api/routes` — so the warmer is the sole upstream
caller and there is no refresh path to migrate.)

**Invariants preserved:**

- Summary and detail read the same memory-cached `RouteConditions`, so the
  Next24h summary/detail consistency guarantee holds.
- Per-window grades (Next12h/24h/48h) are untouched.
- Grading's "is the signal real" gating handles excluded (>24h) sources exactly
  like missing sources today — no silent default-100 factors.

## Components

### 1. `ConditionsWarmerService` (new — `RouteWeather.API/Services`)

A `BackgroundService`:

- `ExecuteAsync`: run one warm cycle immediately on startup, then loop on a
  `PeriodicTimer` (interval from `WarmerOptions`). The single-loop structure is
  naturally non-reentrant — a slow cycle delays the next tick rather than
  overlapping it.
- A warm cycle is what `RoutesController.GetAll` does today:
  `RouteRepository.GetAllAsync()` → 8-wide `SemaphoreSlim` →
  `ConditionsAggregator` in `ReadThrough` mode. The `MaxConcurrentFetches = 8`
  constant moves from the controller to the warmer.
- Error containment: try/catch per route (one failed peak never kills the cycle)
  and per cycle (one failed cycle never kills the service). Failures log and wait
  for the next tick.
- `DailyCallCounter` counts warmer-driven calls as it does user-driven ones today,
  so the diagnostics endpoint stays accurate.

### 2. `ConditionsAggregator` mode split

The `useCache: bool` parameter becomes an explicit `FetchMode`:

- **`FetchMode.ReadThrough`** (warmer only): current behavior — check SQLite
  per-source TTL, fetch upstream on expiry, upsert, compute grade, write to memory
  cache with a **30-minute** TTL (up from 5m). Rationale: the warmer overwrites
  entries every 10 minutes; if the warmer stalls, entries expire at 30m and reads
  degrade to SQLite-stale rather than serving a frozen memory entry indefinitely.
- **`FetchMode.CacheOnly`** (all user GETs): memory hit → return. Miss → read
  last-known SQLite rows regardless of TTL, capped at **24 hours** measured from
  each row's fetch timestamp (rows fetched more than 24h ago are treated as
  missing). Compute the grade from what remains. Mark `IsStale = true`
  when any enabled source's row is past its TTL. Cache the result in memory with a
  short **2-minute** TTL so the warmer's fresh write supersedes it quickly.
  **No code path in `CacheOnly` may construct an upstream HTTP call.**
- Zero usable rows for a route → the same response shape as today's
  all-sources-failed case: the route is returned with `grade: null`
  (`ToSummary` already emits `window?.Grade?.ToString()`).

### 3. `ForecastCacheRepository.GetAllLatestAsync()` (new)

One query returning all `(RouteId, Source)` cache rows ignoring TTL, so the
`GetAll` cache-only path on a memory miss costs **1 query, not 522**. The
single-route detail path uses an analogous single query for that route's rows.
Repository access stays on `IDbContextFactory` (required for the warmer and
requests touching SQLite concurrently).

### 4. Controllers

- `GetAll` / `GetBySlug` lose their aggregation logic and call `CacheOnly`.
- **Header rule:** if the response contains any stale data, send
  `Cache-Control: no-cache` instead of `public, max-age=900,
  stale-while-revalidate=3600`. Otherwise the browser caches the stale payload for
  15 minutes and the frontend's recovery refetch would receive the same stale bytes.
  Fresh responses keep the existing policy. The same rule applies to `GetBySlug`.
- The `/all` page's Refresh button (a plain re-GET of `/api/routes`) becomes a
  cache-only read like any other GET — no server change needed.

### 5. Configuration

New `appsettings.json` section, bound to a `WarmerOptions` class:

```json
"Warmer": {
  "Enabled": true,
  "IntervalMinutes": 10,
  "ServeStaleMaxHours": 24
}
```

Enabled everywhere, including Development — user reads no longer fetch upstream, so
a fresh local database would never acquire data without the warmer.

### 6. `fly.toml`

`min_machines_running = 1`. (`auto_stop_machines` never stops below the minimum, so
no other change is needed.) Cost: roughly $2–3/month for the shared-cpu-1x/256MB
machine running continuously — accepted.

## Frontend changes (both in `map-home`)

1. **Stale-recovery refetch:** when `/api/routes` resolves and any summary has
   `isStale: true`, silently refetch every **60 seconds, max 5 attempts**, cancelled
   on component destroy, stopping as soon as a fully fresh response lands. Markers
   update in place through the existing render path. No new UI: staleness stays
   silent on the map (heads-up indicators are silent by default), and the
   "Loading conditions…" chip does **not** reappear during recovery refetches.
2. **Null-grade rendering:** summaries with `grade: null` must render as
   ghost-style markers, not broken graded markers. If the marker code already
   degrades this way, this change is a test pinning the behavior.

No changes to the peak-detail page.

## Error handling

- Map refetch failures are `console.warn`-silent (grades are already on screen);
  the existing error overlay continues to govern only initial-load failure.
- Degradation ladder, each step no worse than today's cold behavior:
  warmer dies → memory entries expire at 30m → reads serve SQLite-stale (≤24h) →
  beyond 24h → `grade: null` ghost markers.
- A slow or failing upstream source affects only its own column on the next warm
  cycle — the same per-source isolation as today.
- Warmer exceptions never propagate to the host; the API keeps serving cache-only
  reads regardless of warmer health.

## Upstream call volume

Unchanged in the steady state. The warmer respects the same per-source SQLite TTLs
(NWS/SNOTEL 60m, Open-Meteo variants 180m) that bound today's user-driven fetches:
~7–8k calls/day worst case, under Open-Meteo's 10k/day free tier. The warmer shifts
*who* pays fetch latency (a background loop instead of a person), not *how much* is
fetched. `DailyCallCounter` remains the observability check.

## Testing

**Backend (xUnit):**

- `CacheOnly` never invokes upstream clients (mocked clients assert zero calls).
- ≤24h SQLite rows → graded result with `IsStale = true`.
- >24h rows excluded from grading; zero usable rows → `grade: null` response shape.
- Memory-cache hit short-circuits SQLite entirely.
- Warmer: a per-route exception does not abort the cycle; a cycle exception does
  not kill the service; `Enabled: false` results in no cycles.
- Controllers: stale content → `Cache-Control: no-cache`; fresh content → existing
  900s+SWR policy; the warmer still reaches upstream (`ReadThrough`).

**Frontend (Vitest + jsdom, per `.claude/rules/testing.md`):**

- Fake-timer specs for the refetch loop: schedules at 60s on stale, stops on fresh,
  caps at 5 attempts, cleans up on destroy.
- `provideHttpClient()` + `provideHttpClientTesting()`, `httpMock.verify()` in
  `afterEach`.
- `RouteSummary` fixtures carry every interface field.
- Ghost rendering for `grade: null` anchored on structure (CSS class / element
  presence), not prose.

**Acceptance (dev preview):**

- After a fresh deploy, the first visitor sees graded, stale-marked markers in
  ~2–3s instead of 60–90s.
- Fresh grades replace stale within one warm cycle (≤ ~10–13 min worst case),
  with no user action.
- `/api/diagnostics` daily call counts remain in their current range.

## Rollout

1. Ship backend + frontend together in one PR to `dev` (the frontend change is
   inert against a backend without stale serving, and the backend change is safe
   without the frontend refetch — recovery would just wait for the browser's next
   natural visit).
2. Verify on the dev preview against the acceptance criteria, including one
   forced-cold test (restart the Fly machine, load the page).
3. Apply `min_machines_running = 1` via `fly.toml` in the same PR; it takes effect
   on the next `flyctl deploy`.
4. Promote `dev` → `main` once verified.
