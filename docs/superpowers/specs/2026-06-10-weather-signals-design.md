# Weather Signals Expansion: Storm Risk, Gusts, Precip Amount + Context Signals

**Date:** 2026-06-10
**Branch:** `feature/weather-signals`
**Status:** Approved

## Problem

The grade is built from five factors — sustained wind, temperature, precipitation
*probability*, recent snow, and snowpack — but a climber's go/no-go decision hinges
on signals the app never sees:

- **Thunderstorms.** A 20% precip day can be a high-CAPE day. Afternoon convection
  on an exposed ridge is the classic alpine accident, and precip probability does
  not capture it.
- **Gusts.** Sustained 10m wind understates what knocks you off balance on a ridge;
  Open-Meteo's `wind_gusts_10m` is unfetched.
- **Precip quantity.** 90% chance of 0.04" drizzle is climbable; 40% chance of
  1.5" is not. The factor grades only on probability.
- **Context.** Summit cloud/visibility (navigation), wildfire smoke (western
  summers), daylight, and feels-like temperature inform the decision without
  needing to move the grade.

**Audience decision:** the app primarily serves **alpine rock & scrambles** and
**summer hiking (class 1–2)**. Deep-winter signals (wind slab proxies, storm
totals) are out of scope; lightning, gusts, wet-weather quantity, visibility,
smoke, heat, and daylight rank highest.

## Goals

- Grade reflects storm risk, gust exposure, and precip quantity — each gated on
  "is the signal real today?" (no silent default-100 factors).
- Detail page shows cloud/visibility, air quality, daylight, and feels-like
  context without crowding cards.
- Cards stay silent-by-default: one muted chip, only when smoke is actionable.
- Net NWS call volume goes **down** (gridpoints swap + cached point lookups);
  Open-Meteo stays inside the free tier with documented headroom at 300+
  routes; one new lightweight source (AQI).

## Non-Goals

- **Recent rain / rock-drying signal** — considered and deferred (cut in scoping).
- Winter / ski-mountaineering signals (wind slab, storm totals, wind chill grading).
- Route aspect/exposure metadata (not stored; prerequisite for sun/shade signals).
- Avalanche-center forecast integration.
- UV index, humidity/dew-point friction signals (weak relative to the above).
- Any change to the warm-loop / cache-only architecture (PR #27).

## Design

### 1. Grading factors (RouteWeather.Core/Grading)

Hybrid model: hazard-class signals become **gated grading factors**; context
signals are **display-only** (Section 3). Storm modeling is **CAPE-led**
(chosen over weather-code/text triggers, which are binary and fire late, and
over a composite convective index, which is unexplainable in a driver pill).

**ThunderstormFactor (new)**
- Input: max hourly CAPE (J/kg) in the grading window, consensus-blended across
  sources that report it.
- Gate: active only when any window hour ≥ **200 J/kg**; otherwise "Not a factor
  today" (same inactive rendering as snow factors).
- Score: `LinearBetween(maxCape, good: 200, bad: 2000)`.
- Caps: ≥ 1000 → C, ≥ 2000 → D.
- Drivers: "Storm risk" (negative) / "Some instability" (neutral) /
  "Low storm risk" (positive — reachable when the gate fires but the window
  max stays near the floor).
- Thresholds are named constants, deliberately calibrated for marine-influenced
  ranges (Cascades CAPE rarely exceeds ~1500 J/kg); flagged tunable.
- Known bias: CAPE overcalls in instability-without-trigger setups. Accepted —
  for go/no-go the miss is asymmetric.

**GustFactor (new)**
- Input: max hourly gust (mph) in the window.
- Gate: active only when max gust ≥ **25 mph**. Below that, gusts are noise
  around sustained wind; an always-on twin of WindFactor would permanently
  dilute every other weight.
- Score: `LinearBetween(maxGust, good: 25, bad: 55)`.
- Caps: > 70 → F, > 55 → D, > 45 → C (sustained-wind caps shifted +15–20 mph).
- Drivers: "Strong gusts" (negative) / "Gusty" (neutral) / "Manageable gusts"
  (positive — same boundary case as above).

**PrecipitationFactor (upgraded — stays one factor)**
- Adds an amount dimension to the existing probability scoring:
  `amountScore = LinearBetween(windowTotalIn, good: 0, bad: 1.0 × windowHours/24)`
  — i.e. the bad-threshold is 0.5" for the 12h window, 1.0" for 24h, 2.0" for
  48h.
- Composition: `score = min(probabilityScore, amountScore)`.
- The amount dimension engages only when window total ≥ **0.05"** so trace
  forecasts don't drag the score.
- Probability caps unchanged.

**Weights (rebalanced; active-weight renormalization unchanged)**

| Factor | Weight | Was |
|---|---|---|
| Wind | 0.20 | 0.25 |
| Thunderstorm | 0.20 | — |
| Precipitation | 0.18 | 0.20 |
| Temperature | 0.12 | 0.15 |
| Gust | 0.10 | — |
| RecentSnow | 0.10 | 0.20 |
| Snowpack | 0.10 | 0.20 |

Snow factors keep their grade caps (> 8" new → D), which carry the safety
signal when they activate despite the lower weight.

**Consensus / per-source:** CAPE and gusts join per-factor consensus exactly as
precip-probability does today (already GFS+NWS-only): each factor blends only
sources reporting the field. Spread floors: CAPE 200 J/kg, gusts 8 mph.

**Window invariant:** all three factors compute per-window (12/24/48h) through
`WindowGradeCalculator`, keeping headline grades aligned with the visible window.

### 2. Data plumbing (RouteWeather.API / Core / Data)

**Open-Meteo forecast call (extended, no new HTTP requests):** add hourly
`cape`, `wind_gusts_10m`, `precipitation`, `cloud_cover`, `visibility`,
`apparent_temperature` in `OpenMeteoClient`. Models lacking a field (likely
ECMWF for CAPE/gusts) return nulls, which per-source factor logic tolerates.
`HourlyForecast` gains nullable `capeJkg`, `gustMph`, `precipitationIn`,
`cloudCoverPct`, `visibilityMiles`, `apparentTempF`. Also pass the route's
`SummitElevationFt` (as meters) via the `elevation` parameter so Open-Meteo's
lapse-rate downscaling targets the summit, not the DEM cell mean — a real
accuracy lever for summit temperature. Note: Open-Meteo meters quota by
*variable count*, not HTTP requests (>10 variables per request counts
fractionally as multiple calls), so this widening raises quota burn — see
"Upstream call budget" below.

**NWS — swap derived forecast for raw gridpoints:** replace the
`/gridpoints/{wfo}/{x},{y}/forecast/hourly` call with the raw
`/gridpoints/{wfo}/{x},{y}` endpoint, which exposes `windGust`,
`quantitativePrecipitation`, `skyCover`, `visibility`, and
`apparentTemperature` — making gusts and precip amount **dual-sourced into
consensus** (CAPE remains model-only by nature; NWS issues no CAPE). Exact
field availability varies slightly by forecast office; verify during
implementation. Two consequences:

- **Points lookup becomes cached.** The lat/lon → (office, gridX, gridY)
  mapping is static per route; persist it (route-keyed cache row) instead of
  re-resolving every refresh. Re-resolve only on a 404 from the gridpoints
  endpoint (NWS occasionally regrids). Net NWS calls per route-refresh drop
  from 2 to 1.
- **Conditions text changes source.** The raw endpoint has no `shortForecast`
  prose; derive the hourly-table Conditions text and the snow-relevance check
  from the gridpoint's structured `weather` layer (with Open-Meteo's
  `weather_code` WMO mapping as fallback). Structured values beat the current
  "text contains snow" matching anyway.

**Air quality (new source):** `AirQualityClient` →
`air-quality-api.open-meteo.com` for `us_aqi` + `pm2_5` at route coordinates.
Follows the existing source pattern: fetched only by the warmer, cached in
SQLite, **TTL 3h** (AQI moves slowly; keeps warm loop light), served stale
≤ 24h. **Deviation:** AQI staleness/failure does **not** set route `isStale` —
it is context, not a grading input. Detail page shows "Air quality unavailable".

**Daylight (no upstream):** pure `SolarCalculator` in Core (NOAA solar position
algorithm) computes sunrise/sunset/daylight hours from `SummitLat/SummitLon` +
date, expressed in the same local timezone the hourly forecast series already
uses (Open-Meteo's `timezone` resolution for the route's coordinates).
Unit-testable; immune to cache staleness.

**Upstream call budget** (87 routes today; plan for 300+):

- **NWS** publishes no daily cap — its limiter is a burst-rate firewall
  (403 + Reference ID when tripped; keep the User-Agent header and treat
  403/429 as source-missing, the existing failure path). Today:
  87 routes × 2 calls/hr ≈ **4.2k/day**. After the gridpoints swap + cached
  point lookups: ≈ **2.1k/day** — half of today. At 300 routes: ≈ 7.2k/day,
  an average of 5 req/min, with warm-cycle bursts bounded by the existing
  8-wide semaphore (~1–2 req/s worst case). Comfortable at both scales.
- **Open-Meteo** free tier: 10,000 calls/day, 5,000/hr, 600/min, where a
  request with >10 variables counts fractionally (15 vars = 1.5 calls). The
  widened fetch (~8 vars × 4 models) makes each refresh cost roughly 3× a
  plain request — measure the real multiplier during implementation. At the
  current 60-min TTL and 87 routes that flirts with the daily cap, so this
  feature includes the mitigation: **per-source TTLs matched to model update
  cadence** — GFS/ECMWF/ICON publish 6-hourly (TTL 3h), HRRR hourly (TTL 1h).
  That cuts global-model volume ~3× and restores headroom at 87 routes.
- **At 300+ routes**, two further levers exist, deliberately out of scope
  here: dedupe fetches by shared location — routes on the same summit share
  coordinates, and nearby routes share NWS grid cells, so caching by
  (coordinate, source) instead of (route, source) cuts volume by the
  routes-per-summit factor; and Open-Meteo's paid tier as the fallback.
  The AQI source is negligible either way (3h TTL → ~0.7k/day at 87 routes).

**Contract changes:**
- `RouteConditions` (detail): adds `airQuality` (AQI value, category,
  fetched-at; nullable), `daylight` (sunrise, sunset, daylightHours);
  per-source rows gain max-gust and max-CAPE.
- `RouteSummary` (card): gains exactly one nullable field `airQualityUsAqi` —
  the minimum for the card chip. All frontend inline fixture builders must be
  updated (TypeScript enforces).

**Migration:** none. Cached payloads are JSON blobs per source; a new source
name and new JSON fields don't change the SQLite schema.

### 3. Frontend

**Peak-detail page:**
- New **"Sky & Air" tile section** (mirrors snowpack's 4-tile grid, reusing its
  tile styles): current cloud cover, visibility, AQI (value + category, or
  "unavailable"), daylight (sunrise–sunset, total hours).
- **Hourly table:** two new columns only — **Gust** (beside Wind) and
  **Clouds %**. Feels-like, visibility, AQI stay in tiles (contextual, not
  hour-timing-critical; table is already five columns).
- **Hourly table collapses to 24h by default** with a "Show 48h" expander;
  state is component-local. No window-invariant concern — the table is raw
  hourly data, not a graded headline.
- **Factor breakdown:** no UI work; new factors flow through existing
  active/inactive rendering including "Not a factor today".
- **Per-source table:** Max gust and CAPE columns, "—" where unreported.
- **SCSS budget:** `peak-detail.scss` is at its 5kB budget. Reuse snowpack tile
  classes; compact first, bump budget only if needed.

**Route card:** one muted chip — `Smoky air · AQI {n}` — when
`airQualityUsAqi ≥ 151` (US AQI "Unhealthy"). Silent below. New grading
factors surface via existing driver pills and grade caps; no other card change.

### 4. Error handling

- All-sources-null CAPE or gusts → factor **inactive**, never a silent default
  score.
- AQI failure/staleness → tiles "unavailable", chip suppressed, `isStale`
  untouched.
- Null per-hour gust/cloud/visibility cells render "—".
- Daylight always computable (pure function).

### 5. Testing

**Backend:**
- Per-factor units: gate boundaries (200 J/kg, 25 mph, 0.05"), score curves,
  caps, precip `min()` composition.
- `SolarCalculator` vs known sunrise/sunset values (e.g., equinox, known lat).
- Aggregator with fake `AirQualityClient`: success / failure / stale; verify
  `isStale` exemption.
- NWS gridpoints parsing (including sparse `windGust` series and the
  structured `weather` layer → conditions text); points-lookup cache hit path
  and 404 → re-resolve path.
- Consensus with partial-source field coverage (CAPE on subset of models).
- Window alignment for new factors across 12/24/48h.

**Frontend (Vitest + jsdom):**
- Every inline `RouteSummary` fixture gains `airQualityUsAqi`.
- Chip boundary at AQI 150 (hidden) / 151 (shown).
- Sky & Air tiles: render + unavailable states.
- Hourly table: new columns; collapsed 24h default, expander reveals 48h.
- Specs anchor on structure, not prose.

## Decision log

| Decision | Chosen | Rejected |
|---|---|---|
| Storm modeling | CAPE-led gate/score/caps | Weather-code/text (binary, late); composite index (untunable, unexplainable) |
| Signal roles | Hybrid: hazard → graded, context → display | All-graded (tuning risk); display-only (grade lies on storm days) |
| Display placement | Detail sections + one actionable card chip | Cards-everywhere (violates silent-by-default); detail-only (smoke hidden) |
| Precip amount | Upgrade existing factor, `min()` composition | Separate amount factor (two correlated rain factors) |
| Display-only cut | Cloud/visibility, AQI, daylight/feels-like | Recent rain & drying (deferred) |
| NWS integration | Swap derived hourly for raw gridpoints + cached point lookup (dual-sources gusts/QPF, halves NWS volume) | Third NWS call (+50% volume); derived-only (fields missing) |
| Quota headroom | Per-source TTLs matched to model update cadence (in scope) | Location-dedup cache keying & paid tier (deferred to 300-route scale) |
