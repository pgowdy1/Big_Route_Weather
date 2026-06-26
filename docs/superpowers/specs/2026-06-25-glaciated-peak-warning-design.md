# Glaciated-Peak Warning — Design

**Date:** 2026-06-25
**Status:** Approved (pending spec review)
**Branch:** `feature/glaciated-peak-warning` (off `dev`)

## Problem

The grade card scores **weather only**. It says nothing about the hazards that
actually kill people on glaciated peaks: crevasses, falling séracs, heat-driven
rockfall as snow and ice melt out, and route conditions that change as ice or
rock gets exposed. A user can see an "A" grade on Mount Rainier and reasonably
but wrongly conclude the mountain is safe today.

We want a **loud, persistent warning** on every glaciated peak making clear that
the grade does not measure these hazards and that glacier travel is serious
mountaineering, not a hike.

## Goals

- Mark glaciated peaks as first-class data in the **database**, flowed through
  the API and the generated SEO manifest — single source of truth, centrally
  changeable. (No hand-maintained frontend list.)
- A loud, always-visible, non-dismissible warning banner on each glaciated
  peak's detail page, rendered at **prerender time** (no API dependency).
- A muted, discoverable marker on grid cards and map markers — *not* a loud
  chip on every card (respects the existing silent-/muted-indicator principle).

## Non-Goals

- No change to grading. Glaciation is deliberately **outside** the grade — that
  decoupling is the whole message.
- No badge on pre-data ("ghost") map markers — they are transient placeholders.
- No filtering/sorting by glaciation (possible later).

## The flagged peaks (29)

Rule applied: **the peak carries a glacier and commonly-climbed routes cross it.**
Peak-level (conservative), aligning with the planned future move to grade by peak
rather than by route.

- **Cascades (18):** Mount Rainier, Mount Hood, Mount Adams, Mount Baker,
  Mount Shasta, Glacier Peak, Mount Shuksan, Mount Stuart, Forbidden Peak,
  Dragontail Peak, Eldorado Peak, Sahale Peak, Bonanza Peak, Goode Mountain,
  Sloan Peak, Silver Star Mountain, North Sister, Mount Jefferson
- **Sierra (8):** North Palisade, Mount Sill, Middle Palisade, Mount Lyell,
  Mount Ritter, Banner Peak, Mount Darwin, Mount Conness
- **Wind River (3):** Gannett Peak, Mount Helen, Mount Sacagawea

Explicitly **not** flagged (sanity anchors for tests): Mount St. Helens,
Mount Thielsen, South Sister, Black Peak, Fremont Peak, Mount Dana, Mount Whitney,
all Sawtooth, all Wasatch, all 58 Colorado 14ers (permanent snowfields, but no
crevassed glacier travel).

Slugs (single source of truth lives in `RouteSeeder.BuildRoutes`):
`mount-rainier, mount-hood, mount-adams, mount-baker, mount-shasta, glacier-peak,
mount-shuksan, mount-stuart, forbidden-peak, dragontail-peak, eldorado-peak,
sahale-peak, bonanza-peak, goode-mountain, sloan-peak, silver-star-mountain,
north-sister, mount-jefferson, north-palisade, mount-sill, middle-palisade,
mount-lyell, mount-ritter, banner-peak, mount-darwin, mount-conness, gannett-peak,
mount-helen, mount-sacagawea`

## Architecture

Data flows the same path as every other static peak identity field
(`summitElevationFt`, `classDifficulty`, …), which already has a parity guard:

```
RouteSeeder (C#)  ──►  RouteEntity.IsGlaciated (DB column)
       │                        │
       │                        ├──► RoutesController ToSummary/ToDetail ──► /api/routes ──► cards, map
       │                        │
       └────────── parity ──────┴──► generate-peaks-manifest ──► peaks.manifest.json ──► detail banner (prerender)
```

### 1. Database / source of truth

- `RouteEntity` gains `public bool IsGlaciated { get; set; }` (default `false`).
- **Hand-authored EF migration** `AddIsGlaciated`: adds `IsGlaciated INTEGER NOT NULL DEFAULT 0`
  to `Routes`. Authored as the Migration + `.Designer.cs` + updated
  `RouteWeatherContextModelSnapshot.cs` trio, because the running API locks
  `dotnet ef migrations add`. Verify by building `RouteWeather.Data` alone.
- `RouteSeeder.BuildRoutes` sets `IsGlaciated = true` on the 29 slugs above. This
  C# list is the single source of truth.
- **Reconcile pass** in `SeedAsync`: the existing logic only *adds* missing
  peaks; it never updates rows that already exist, so an already-populated
  dev/prod DB would leave the 29 peaks at the migration default `false`. After
  the add step, update `IsGlaciated` on existing rows to match the catalog
  (scoped to just that column). Self-healing, no slug list duplicated in SQL.

### 2. API

- `ToSummary` and `ToDetail` (anonymous DTOs in `RoutesController`) add
  `isGlaciated = route.IsGlaciated` — both methods already receive the
  `RouteEntity`, so no domain `Route` record change.
- `/api/routes/positions` unchanged. The graded markers (`RouteSummary`) carry
  the flag; transient ghost markers do not.

### 3. Manifest + parity

- Add `isGlaciated` to the `FIELDS` array in
  `frontend/scripts/generate-peaks-manifest.mjs` (the not-undefined/null/empty
  validation passes for boolean `false`).
- Add `isGlaciated` to the `PeakSeo` record in `ManifestParityTests` and a
  `Check("IsGlaciated", r.IsGlaciated, p.IsGlaciated)`.
- Regenerate `peaks.manifest.json` against the running API, **or** hand-edit all
  124 entries (29 `true`, rest `false`). The parity test fails CI on any
  seeder/manifest disagreement.

### 4. Frontend models

- `RouteSummary.isGlaciated: boolean` in `route-conditions.ts` (inherited by
  `RouteDetail`). Every `RouteSummary` test fixture must add the field — the TS
  compiler flags each omission (see `.claude/rules/testing.md`).
- `PeakSeo.isGlaciated: boolean` in `peak-seo.ts`.

### 5. Detail page — loud persistent banner

- In `peak-detail.html`, rendered when `peak()?.isGlaciated`, placed immediately
  beneath the `<header class="hero">` block, full-width, high-contrast,
  **not dismissible**. Driven by the static `peak()` catalog → prerenders with
  no API call.
- Accessibility: `role="note"`; the ⚠️ glyph is `aria-hidden`, with the heading
  text carrying the meaning.
- SCSS kept compact. `peak-detail.scss` is the largest component style; verify it
  stays under the `anyComponentStyle` budget (**7 kB warning / 8 kB error** in
  `angular.json`); bump in-PR if it crosses.

**Approved copy:**

> ## ⚠️ Glaciated peak — hazards this grade does **not** measure
> This grade reflects **weather only.** It does **not** account for glacier and
> snow/ice hazards: **crevasses, falling séracs, heat-driven rockfall** as snow
> and ice melt out, and **changing route conditions** (ice or rock newly
> exposed). Real conditions can be far more dangerous than the forecast suggests.
>
> **Glacier travel is serious mountaineering.** Attempt these routes only with
> roped-team travel, crevasse-rescue skills, and adequate glacier experience.

### 6. Cards — muted chip

- In `route-card.html`, inside `.meta`: `@if (route().isGlaciated)` → a small
  muted **"Glaciated"** chip (icy/desaturated styling, *not* red), alongside the
  existing range/stale/AQI chips. `title`/`aria-label` points users to the peak
  page for the full warning.

### 7. Map — muted badge + popup note

- In `map-home.ts` `renderMarkers`, when `route.isGlaciated`: add a small corner
  badge to the marker `divIcon` html, and a one-line glaciated note to
  `popupHtml` (kept a pure function so it is unit-testable). Muted styling.
- Pre-data ghost markers (`renderGhostMarkers`) stay unbadged.

## Testing (TDD)

**Backend**
- Extend `ManifestParityTests` to compare `IsGlaciated`.
- New seeder test: the 29 slugs are `true` (with spot-checks across all three
  ranges), known walk-ups (`pikes-peak`, `south-sister`, `mount-st-helens`) are
  `false`, the reconcile pass flips a row pre-set to the wrong value, and
  `count(IsGlaciated == true) == 29` to catch accidental list edits.

**Frontend**
- `peak-detail.spec`: banner present for `mount-rainier`, absent for
  `pikes-peak`. (Static catalog → works in jsdom without the API.)
- `route-card.spec`: chip renders when `isGlaciated` is `true`, absent when
  `false`.
- `map-home`: `popupHtml` includes the glaciated note when the route is flagged,
  omits it otherwise.
- Update existing `RouteSummary` fixtures across specs to include `isGlaciated`.

## Acceptance criteria

1. Every one of the 29 peaks reports `isGlaciated: true` from `/api/routes` and
   in `peaks.manifest.json`; all other peaks report `false`.
2. Opening a glaciated peak shows the warning banner in the **prerendered** HTML
   (visible before/independent of the API conditions load) and it cannot be
   dismissed.
3. A non-glaciated peak shows no banner.
4. Glaciated peaks show the muted chip on their grid card and a badge + popup
   note on their map marker; non-glaciated peaks show neither.
5. `ManifestParityTests` and the new seeder test pass; the manifest and seeder
   cannot drift without failing CI.
6. `peak-detail.scss` stays within the component-style budget (or the budget is
   bumped in the same PR with a note).

## Risks / notes

- **Existing-DB backfill** is the easy thing to miss — handled by the reconcile
  pass; the seeder test must cover it explicitly.
- **Manifest regeneration** depends on the running API; if regenerated by hand,
  the parity test is the backstop.
- The future "grade by peak" migration can consume this same `IsGlaciated`
  column with no schema change.
