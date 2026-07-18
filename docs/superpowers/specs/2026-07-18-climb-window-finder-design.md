# Climb-Window Finder — Design Spec

**Date:** 2026-07-18
**Status:** Approved pending user review
**Depends on:** `feature/user-options` (time-format setting) merging to `dev` first

## Goal

Turn the site from "how are conditions now?" into "when should I go?" For every route, scan the hourly forecast for contiguous stretches good enough to climb, sized to that route's typical summit-day duration, and present them as concrete windows: *"Sat 2:00 AM – 11:00 AM · A · closes as storm energy builds."*

## Decisions (locked during brainstorming)

1. **7-day horizon** (up from 48h), with low-confidence treatment on days 5–7.
2. **Route-aware windows:** each route gets a seeded `TypicalClimbHours`; only stretches long enough for *that* route become windows.
3. **V1 output is window + end-reason.** No synthetic "summit by" ETA — every displayed number comes straight from the forecast.
4. **Peak detail UI:** hero callout (best window) + 7-day strip.
5. **Route card UI:** one muted "Next window: …" line under the drivers.
6. **Architecture:** extend the existing snapshot pipeline (single blended series; one source of truth). No separate planning fetch; no frontend window computation.

## Out of scope (v1)

- Summit-ETA / turnaround clock (fast-follow candidate once duration data proves out).
- Cross-route "best objective this weekend" ranking (separate Weekend Planner feature).
- Marginal/sub-threshold "stretch" callouts — if no window qualifies, we say so plainly.
- Multi-day itinerary modeling — windows describe the summit-day push only.
- Consensus-based confidence — v1 confidence is horizon-based only.
- Alerts/notifications.

## Backend design

### Horizon extension

- `OpenMeteoClient`: `forecast_days=2` → `forecast_days=7`.
- `NwsClient`: stop truncating gridpoint hourly (natively ~156h); pass through what exists.
- `WeatherSnapshot.Next48Hours` → renamed `Hourly`, now the full blended series (~168h). Hours where a source has no data simply have fewer voters in the existing per-hour blend/consensus machinery.

### Invariant protection (critical)

The headline grade, factor scores, and 12/24/48h `WindowGrades` are computed over `Hourly.Take(48)` — explicitly. The PR #3 aggregation-window invariant (headline aligns with the UI's visible forecast window) must survive the horizon extension by construction.

**Regression test:** a 168h snapshot and a twin truncated to its first 48 hours produce byte-identical headline `GradeResult` and `WindowGrades`.

### WindowFinder (new: `RouteWeather.Core/Grading/WindowFinder.cs`, pure static)

1. **Score every hour.** Run `GradeCalculator.Compute` on single-hour slices (same `Aggregate` path `WindowGradeCalculator` uses), with the snapshot's snowpack/AQI context. An hour **qualifies** iff its post-cap grade is **A or B** (score ≥ 80 and no cap worse than B). Hours with no computable data return F and disqualify — conservative by default.
2. **Frame by climbing day.** For each UTC date in the horizon, `SolarCalculator.ComputeUtc(lat, lon, date)` yields a frame `[sunrise − 6h, sunset]`. Qualifying runs are intersected with frames: a 60-hour bluebird run becomes per-day windows ("Sat 1 AM – 9 PM", "Sun 1 AM – 9 PM"), and night-only runs never become windows. At these latitudes frames never overlap (winter gap ≈ 9h, summer gap ≈ 2h).
3. **Fit filter.** A frame intersection becomes a `ClimbWindow` only if its span ≥ that route's `TypicalClimbHours`.
4. **Annotate.**
   - *Grade/score:* the window's hours graded through the existing aggregate path.
   - *End-reason:* inspect the first non-qualifying hour at/after the window end — its capping factor if one is active, otherwise its worst-scoring factor, supplies a short phrase from the factor's existing detail text ("storm energy builds", "wind rises", "rain arrives"). If the window ends at frame end while the run continues: "daylight ends". If clipped by the data horizon: "beyond forecast range".
   - *LowConfidence:* true when the window midpoint is more than 96h out (day 5+).

**Best window** = highest score; ties break to the soonest start.

### Data model

- `Route` / `RouteEntity`: add `TypicalClimbHours` (double, required). EF migration (hand-authored trio if the running API blocks `dotnet ef`). Seed all ~30 routes from guidebook norms, using the **slow end** of published ranges; values drafted as a review table in the implementation plan. Semantics: **summit-day push** (car-to-car or camp-to-camp), not multi-day itineraries.
- New Core records:
  - `ClimbWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc, Grade Grade, int Score, string EndReason, bool LowConfidence)`
  - `HourlyQuality(DateTimeOffset TimeUtc, int Score, bool Qualifies)`
  - `NextWindowSummary(DateTimeOffset StartUtc, DateTimeOffset EndUtc, Grade Grade, bool LowConfidence)`
- `RouteConditions` gains: `Windows: IReadOnlyList<ClimbWindow>`, `HourlyQuality: IReadOnlyList<HourlyQuality>` (strip data), `DailyDaylight: IReadOnlyList<DaylightInfo>` (one per UTC date the horizon touches, 7–8 entries; night shading).
- `RouteSummary` (list endpoint) gains one nullable `NextWindow: NextWindowSummary?` — keeps the list payload light.

### Pipeline & caching

`ConditionsAggregator` computes windows at warm time; results ride the existing cached payloads. User GETs stay cache-only (memory 30m/2m → SQLite ≤24h → ghosts). Ghost routes have null/empty windows. Stale payloads keep the existing stale semantics — windows share the snapshot's `UpdatedAt`; no separate staleness. Detail payload grows to ~168 hourly entries (~20 KB gzipped) — acceptable; SQLite rows grow ~3.5×.

## Frontend design

### Components

- **`window-hero`** (new standalone, own SCSS): best-window callout — times, grade badge, end-reason sentence. No-window state: "No climbable window in the next 7 days."
- **`week-strip`** (new standalone, own SCSS): 7-day horizontal band rendered from `hourlyQuality` as plain divs/SVG — no external libs, SSR-safe. Night shading from `dailyDaylight`; hatched low-confidence region past hour 96; qualifying window extents highlighted; day tick labels.
- **Route card:** one muted line — "Next window: **Sat 2 AM – 11 AM · A**" — from `nextWindow`; hidden when null. Low-confidence windows get a muted suffix, consistent with the silent-by-default indicator policy.
- `peak-detail.scss` stays under its 7 kB budget: new sections get only a thin layout wrapper there; all component styling lives in the new components' own files.

### Display rules

- All times browser-local, honoring the 12/24h time-format setting.
- Day labels ("Sat") derive from browser-local start time — never from `*.Date` of UTC instants (documented `DaylightInfo` trap at western longitudes). Daylight pairs map to strip days by local date of sunrise.
- A window already underway renders "Now – 11 AM".
- Prerendered (SSG) pages render the existing no-data state; fetches stay browser-gated. Hydration verified on the dev preview, not just jsdom.

## Edge cases

- **No windows in 7 days:** hero states it plainly; the strip still shows the week's shape.
- **Short-horizon sources:** NWS ends ~hour 156 — later hours just have fewer blend voters.
- **Horizon-clipped window:** end-reason "beyond forecast range".
- **Missing CAPE/gust hours:** existing nullable-factor gating applies unchanged.
- **DST transitions:** frames are absolute UTC instants (DST-immune); browser-local display may show a 23/25-hour day — accepted.
- **`TypicalClimbHours` larger than any frame** (e.g., midwinter short days on a long route): no windows qualify — honest output, not a bug.

## Testing

- **Core / WindowFinder:** qualifying bar boundary (score 79 vs 80; cap-blocked hour), frame intersection (night-only run excluded; multi-day run split per day), fit filter, end-reason selection (storm / wind / rain / daylight / horizon), LowConfidence midpoint boundary at 96h, ranking tie → soonest.
- **Invariant regression:** 168h vs first-48h twin snapshots → identical headline grade, factors, and 12/24/48 `WindowGrades`.
- **Clients:** Open-Meteo URL carries `forecast_days=7` and parses 168h; NWS passes through >48h.
- **Data:** every seeded route has `TypicalClimbHours > 0`; Data project builds alone (running-API lock workaround).
- **API:** aggregator populates `Windows` / `HourlyQuality` / `DailyDaylight`; list endpoint serializes `NextWindow`.
- **Frontend (Vitest + jsdom):** card line (renders / hidden when null / time-format / low-confidence suffix), hero states (window / no-window), strip structure (band counts, hatch region present, night shading present) — structure-anchored assertions, no literal prose.
- **Fixtures:** every inline `RouteSummary` builder gains `nextWindow`; update `.claude/rules/testing.md` field list accordingly.
- **Manual:** SSR/hydration check on the dev Pages preview.

## Sequencing

1. `feature/user-options` merges to `dev` (time-format dependency).
2. New branch `feature/climb-window-finder` off `dev`.
3. Implementation order: migration + seeding → horizon extension + invariant tests → `WindowFinder` + unit tests → aggregator/DTO wiring → frontend components → fixture sweep → dev-preview verification.
