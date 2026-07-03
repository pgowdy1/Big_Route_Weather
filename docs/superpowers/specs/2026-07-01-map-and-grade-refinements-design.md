# Map & Grade Refinements — Design

**Date:** 2026-07-01
**Status:** Approved (pending spec review)
**Branch:** `feature/map-and-grade-refinements` (off `dev`; glaciated work already merged via PR #45)

Three small, independent refinements, batched into one spec:

1. Move the glacier snowflake badge closer to its map dot.
2. Fold Air Quality Index (AQI) into the route grade when AQI ≥ 101.
3. Restore the map's zoom/center when the user returns to the map mid-session.

---

## Feature 1 — Glacier snowflake closer to its dot

### Problem

On the map, the glaciated badge (`❄`) is absolutely positioned at
`top: -2px; right: -2px` inside the 28×28 marker box, but the colored grade dot
is only 14×14 and centered (icon anchor `[14,14]`). The snowflake therefore
lands at the box's far corner — roughly 10px up and right of the dot's
upper-right shoulder, reading as detached from the dot it describes.

### Change

Nudge `.peak-marker .glacier-badge` in `frontend/src/app/pages/map-home/map-home.scss`
from `top: -2px; right: -2px` to `top: 1px; right: 2px`, tucking the badge
against the dot's upper-right shoulder.

- Pure taste; exact px dialed in during manual testing if `1px / 2px` isn't
  quite right.
- Frontend only. No logic, no test changes.

---

## Feature 2 — AQI as a conditional grade factor

### Problem

AQI is currently, by explicit design, **display-only** — the value is fetched,
cached, and shown on the peak detail "Air quality" tile, but never touches the
grade (`ConditionsAggregator` comments state this in several places). When air
is genuinely unhealthy, a route can still show an "A" that ignores it.

### Change

Promote AQI to a **conditional** grade factor using the existing
"silent-until-it-matters" pattern (same shape as `ThunderstormFactor` /
`GustFactor`): silent when the signal isn't actionable, active and grade-moving
when it is.

New `AirQualityFactor` in `backend/RouteWeather.Core/Grading/`, following the
established factor shape (`Weight`, `IsActive`, `Score`, `Detail`,
`InactiveDetail`, `Cap`).

### Behavior (US EPA AQI bands)

| AQI | Band | Effect on grade |
|-----|------|-----------------|
| ≤ 100 | Good / Moderate | **Silent** — no factor card, no drag. The existing "Air quality" tile still shows the raw value. |
| 101–150 | Unhealthy for sensitive groups | Active factor, score drag only, **no hard cap** |
| 151–200 | Unhealthy | Cap grade at **C** |
| 201+ | Very Unhealthy / Hazardous | Cap grade at **F** |

- **Active floor:** `IsActive(aqi) => aqi >= 101`.
- **Score curve:** `ScoringMath.LinearBetween(aqi, goodValue: 50, badValue: 300)`
  → AQI 101 ≈ 80, 150 ≈ 60, 200 ≈ 40, 300 → 0. Because it scores below 85 the
  instant it activates, bad air can only **drag** the grade, never inflate it,
  and never appears as a positive driver.
- **Weight:** `0.15` — below wind/precip/storm (0.18–0.20), above the 0.10
  minors. Gated + capped, so weight is a secondary knob.
- **Cap:** `null` for 101–150; `Grade.C` for 151–200; `Grade.F` for 201+.
- **Driver:** when active it surfaces as a negative/neutral driver
  ("Poor air quality" / "Reduced air quality"), flowing into the map popup and
  card automatically via the existing `LabelFor` / driver path (new `LabelFor`
  entry required for "Air quality").

### Wiring

- `GradeCalculator.Compute(weather, snowpack)` gains a third parameter:
  `AirQualitySnapshot? airQuality = null` (optional, so existing callers/tests
  compile unchanged). When `airQuality is not null` **and** `IsActive`, add the
  AQI `FactorScore` and its cap candidate.
- `WindowGradeCalculator.Compute` / `GradeWindow` thread the same current AQI
  snapshot into each window's `GradeCalculator.Compute`, so the 12h/24h/48h
  window grades and the headline grade stay consistent (no headline-vs-window
  mismatch — see the aggregation-window invariant).
- `ConditionsAggregator.BuildConditions` passes `airQuality.Snapshot` into both
  `GradeCalculator.Compute` and `WindowGradeCalculator.Compute`.

### Invariants preserved

- **Staleness asymmetry:** AQI now affects the grade **value**, but a
  stale/missing AQI reading must still **never** set `isStale`. The existing
  `forceStale` logic (weather + snowpack only) stays exactly as-is. A recent
  cached AQI (within the 24h serve-stale cap) is used for grading; its age does
  not gate route staleness.
- **Ghost routes:** the aggregator's existing rule — no weather **and** no
  snowpack → null grade — still fully governs ghost markers. AQI alone can never
  manufacture a grade on a data-less route.

### UI

No frontend changes required. The factor breakdown, drivers, and grade badge all
render generically, so:

- AQI ≤ 100 → no factor card at all (the "Air quality" tile carries the raw
  number). This is deliberate: silent on OK, surfaced only when actionable.
- AQI ≥ 101 → an active "Air quality" factor card appears in the breakdown with
  its score, and the driver flows to card/popup.

### Alternatives considered

- **Weight-only, no caps:** rejected — the grading system already uses caps for
  genuine hazards (storm energy, extreme wind), and unhealthy+ air is a real
  reason a "send" day can't stay an A. Caps match the house style.
- **AQI on the headline grade only, not window grades:** rejected — the 24h
  window drives the peak-detail hero while the map dot uses the headline grade;
  omitting AQI from windows reintroduces the headline-vs-window mismatch the
  per-window grades were built to avoid.

---

## Feature 3 — Restore map view on return

### Problem

When a user zooms in on the map, clicks a peak, and later returns to the map,
the map re-initializes at the default center/zoom — losing where they were.

### Decision

The map at `/` remembers its zoom/center **for the session** and restores it on
any return to the map (via the "← Map" link or any other path back). The
"All peaks" link (`/all` → grid) stays unchanged.

### Approach

A tiny root-singleton service `MapViewState` holding
`{ center: [number, number], zoom: number } | null` in memory
(`frontend/src/app/services/map-view-state.ts`).

- `MapHome` writes current center + zoom to `MapViewState` on Leaflet's
  `moveend` event (captures the final view before navigation away).
- `MapHome.initMap` reads `MapViewState`: if a saved view exists, use it for
  center/zoom; otherwise fall back to today's defaults (`[43.0, -113.6]`, zoom
  `isMobile ? 4 : 6`).
- In-memory (root singleton) means the view survives client-side navigation but
  resets on a hard reload — mid-session you return where you were; a fresh visit
  gets the default. This matches the chosen behavior exactly and needs no
  storage plumbing.

### Non-Goals

- **Re-opening the popup** of the peak the user viewed. Requested behavior is
  zoom/view only; the popup adds async-timing complexity (marker render vs.
  restore) for little gain. Easy to add later.
- Persisting the view across hard reloads or browser sessions (in-memory is
  intentional).

### Testing

- Unit spec for `MapViewState` (set → get round-trip, null default).
- `MapHome` map init runs behind `afterNextRender` + `isPlatformBrowser` and uses
  real Leaflet, which jsdom does not exercise; keep `MapHome` test changes
  minimal and cover the restore logic at the service level.

---

## Branching

Per project policy (feature → dev → main), features base off `dev`.

- The glaciated work that feature 1 tweaks is already merged to `dev` (PR #45),
  so there is no ambiguity: branch `feature/map-and-grade-refinements` off `dev`.
- Plan: one feature branch, three separate commits (one per feature), one PR to
  `dev`. Split into separate PRs only if preferred.

## Testing summary

- **Feature 1:** manual visual check on the map; no automated tests.
- **Feature 2:** backend unit tests for `AirQualityFactor` (score curve, active
  floor, cap thresholds at 100/101/150/151/200/201) and `GradeCalculator` /
  `WindowGradeCalculator` integration (AQI drag + cap applied; stale/missing AQI
  does not set `isStale`; no grade on data-less route). No frontend tests.
- **Feature 3:** `MapViewState` unit spec.
