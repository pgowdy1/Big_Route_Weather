# Spec: Classic-objective peak expansion — Cascades, Sierra, Wasatch

**Date:** 2026-06-17
**Branch:** `feature/classic-peaks-expansion` (based off `dev`)
**Layers:** Backend seed data + one range-polygon data migration; one frontend map-render tweak.
**Scope:** Medium — mostly curated data entry, one small map change, one data migration.

## Goal

Broaden coverage beyond the Colorado-heavy catalog by adding the most notable
**classic mountaineering objectives** ("big routes on big objectives") to three
existing ranges. One standard route per peak, consistent with the established
one-route-per-peak convention.

| Range | Current | Add | Total |
|---|---|---|---|
| Cascades | 7 | 18 | 25 |
| Sierra Nevada | 6 | 14 | 20 |
| Wasatch | 6 | 5 | 11 |

**Selection basis:** classic mountaineering objectives (notable, sought-after
climbs), not strict topographic prominence and not raw elevation. Geographic
spread within each range is intentional.

## Non-goals

- No new ranges (Tetons etc. remain future work).
- No multiple-routes-per-peak.
- No grading-logic change. New peaks grade exactly like existing ones.
- No About-page copy rewrite (the page already names the Cascades/Sierra/Wasatch
  as planned coverage).

## Peak lists

Elevations below are rounded. **Exact summit lat/lon, `ClassDifficulty`, and the
nearest SNOTEL triplet are verified per row during implementation** against
authoritative sources (the same way existing rows were built). Slugs are
`kebab-case(mountain)`. The aggregator tolerates SNOTEL failures gracefully, so a
peak with no reasonable nearby station gets an empty triplet rather than a bad one.

### Cascades (+18)

The major volcanoes are mostly already seeded, so these lean to North Cascades
alpine classics plus the remaining Oregon / Northern-California volcanoes, for
full-range geographic coverage.

| Mountain | Standard route | ~Elev (ft) | Sub-region |
|---|---|---|---|
| Mount Shuksan | Fisher Chimneys | 9,131 | N. Cascades, WA |
| Mount Stuart | West Ridge | 9,416 | WA |
| Forbidden Peak | West Ridge | 8,816 | N. Cascades, WA |
| Dragontail Peak | Backbone Ridge | 8,842 | WA |
| Eldorado Peak | East Ridge | 8,871 | N. Cascades, WA |
| Sahale Peak | Sahale Arm | 8,681 | N. Cascades, WA |
| Liberty Bell | Beckey Route | 7,720 | Washington Pass, WA |
| Bonanza Peak | Mary Green Glacier | 9,516 | WA (highest non-volcanic) |
| Goode Mountain | Northeast Buttress | 9,206 | N. Cascades, WA |
| Black Peak | South Ridge | 8,970 | N. Cascades, WA |
| Sloan Peak | Corkscrew Route | 7,835 | WA |
| Silver Star Mountain | Silver Star Glacier | 8,876 | Washington Pass, WA |
| South Sister | South Ridge (Devils Lake) | 10,358 | OR |
| North Sister | South Ridge | 10,085 | OR |
| Mount Jefferson | Jefferson Park Glacier | 10,497 | OR |
| Mount Thielsen | West Ridge | 9,184 | OR |
| Mount McLoughlin | East Ridge | 9,495 | OR |
| Lassen Peak | Southeast Slopes | 10,457 | N. California |

### Sierra Nevada (+14)

The existing six are all southern 14ers (Whitney / Palisades / Langley area), so
these spread the range north into the Tuolumne/Ritter country and fill the
central High Sierra. Middle Palisade + Tyndall represent the south; the rest of
the Palisade cluster (Thunderbolt/Starlight/Polemonium) is intentionally left out.

| Mountain | Standard route | ~Elev (ft) | Sub-region |
|---|---|---|---|
| Mount Conness | West Ridge | 12,590 | N. Sierra / Tuolumne |
| Cathedral Peak | Southeast Buttress | 10,911 | Tuolumne |
| Matterhorn Peak | North Arête | 12,280 | N. Sierra / Sawtooth Ridge |
| Mount Dana | Northwest Slopes | 13,061 | Tioga / Tuolumne |
| Mount Lyell | Northwest Slopes | 13,114 | Yosemite high point |
| Mount Ritter | Southeast Glacier | 13,149 | Ritter Range |
| Banner Peak | Northeast Ridge | 12,945 | Ritter Range |
| Mount Humphreys | East Arête | 13,986 | Central High Sierra |
| Mount Darwin | West Ridge | 13,837 | Evolution region |
| Temple Crag | Venusian Blind Arête | 12,999 | Palisades approach |
| Bear Creek Spire | North Arête | 13,720 | Central High Sierra |
| Mount Brewer | Northwest Ridge | 13,570 | Kings Canyon |
| Middle Palisade | Northeast Face | 14,012 | Palisades |
| Mount Tyndall | Northwest Rib | 14,025 | S. Sierra |

### Wasatch (+5)

The central Wasatch Cottonwood-ridge classics. All five fall inside the existing
Wasatch polygon (no polygon change needed for this range).

| Mountain | Standard route | ~Elev (ft) |
|---|---|---|
| Broads Fork Twin Peaks | East Ridge | 11,330 |
| Dromedary Peak | West Ridge | 11,107 |
| Sunrise Peak | South Ridge | 11,275 |
| Mount Superior | Cardiff Ridge | 11,132 |
| Mount Raymond | West Ridge | 10,241 |

## Architecture / how it fits

### 1. Seeding the peaks — no schema change, no route migration

`RouteSeeder.SeedAsync` is already **additive and idempotent**: it diffs catalog
slugs against the DB and inserts only slugs not yet present (see
`RouteSeeder.cs` lines ~22–28). Therefore:

- Add the 37 new `RouteEntity` initializers into the existing `Cascades()`,
  `Sierras()`, and `Wasatch()` factory methods in `RouteSeeder.cs`.
- On next startup the seeder inserts exactly the new slugs into already-seeded
  databases; a fresh DB gets the full set.
- `RouteEntity` / `RouteWeatherContext` already fit — **no schema migration for
  routes**.

### 2. Range polygon expansion — needs a data-only migration

The map (`map-home.ts` `renderLayers`) draws a dashed polygon per range from
`RangeEntity.PerimeterGeoJson`. Several new peaks fall **outside** the current
narrow boxes and would render as pins floating beyond the boundary. Widen two
polygons; keep them as simple axis-aligned rectangles to match the existing style:

| Range | Current box (lon × lat) | New box (lon × lat) | Reason |
|---|---|---|---|
| Cascades | -122.5…-120.8 × 41.1…49.1 | ~ -122.9…-120.3 × 40.2…49.1 | Lassen (south), Liberty Bell / Silver Star (east) |
| Sierra Nevada | -118.8…-117.9 × 36.2…37.4 | ~ -119.6…-117.8 × 36.1…38.2 | Conness / Matterhorn / Cathedral (north & west) |
| Wasatch | -112.0…-111.4 × 39.6…40.9 | **unchanged** | all 5 additions already inside |

Exact corner coordinates are finalized in the plan so every seeded peak in a
range sits inside that range's polygon (verified by a test, below).

**Migration requirement:** `EnsureRangesAsync` only *inserts* ranges that are
missing — it never updates existing range rows. So updating `RangeCatalog()`
coordinates only affects fresh DBs. Deployed/dev DBs already hold the `cascades`
and `sierra-nevada` rows, so a **data-only migration** must `UPDATE` those two
`PerimeterGeoJson` values — mirroring the prior `20260608140000_UpdateRangePolygons`
migration. Both places (the `RangeCatalog()` literal and the migration `UPDATE`)
must carry the identical new GeoJSON so fresh and migrated DBs agree.

If the running API locks the build and blocks `dotnet ef migrations add`,
hand-author the Migration + Designer + ModelSnapshot trio (the snapshot is
unchanged here since there's no schema delta — only data), and verify by building
the Data project alone.

### 3. Map clustering — generalize to all ranges

`renderMarkers()` in `map-home.ts` currently special-cases
`route.rangeSlug === 'colorado-14ers'` into the marker-cluster group while every
other range drops individual pins. Tripling the Cascade and Sierra pin counts
would overcrowd the map at default zoom. Change: route **every** range's pins
through the cluster group (keep `disableClusteringAtZoom: 8`, so pins separate
on zoom-in). This removes the per-range branch — one contained edit to one method.

### 4. API / warmer budget — verified acceptable, no change needed

The warmer (`ConditionsWarmerService`) re-aggregates every route every
`IntervalMinutes` (default 10) at `MaxConcurrentRoutes` (default 3). Per-source
cache TTL is 60 min (`ForecastSourcesOptions`), so each source is actually fetched
upstream ~24×/day/peak regardless of warmer cadence. Growing 87 → 124 peaks lifts
Open-Meteo from ~2.1k to ~3.0k calls/day/source — comfortably under the 10k
free-tier ceiling; NWS is keyless and generous. `DailyCallCounter` is
observability only (no hard cap to trip). **No mitigation required.**

## Affected files

**Modified:**
- `backend/RouteWeather.Data/RouteSeeder.cs` — +37 `RouteEntity` rows; update
  `cascades` and `sierra-nevada` `PerimeterGeoJson` in `RangeCatalog()`.
- `frontend/src/app/pages/map-home/map-home.ts` — generalize clustering to all ranges.

**Added:**
- `backend/RouteWeather.Data/Migrations/<timestamp>_ExpandCascadeSierraPolygons.cs`
  (+ `.Designer.cs`) — data-only `UPDATE` of the two range polygons.

**Not changed:**
- `RouteEntity` / `RangeEntity` / `RouteWeatherContext` — schema already fits.
- `RouteWeatherContextModelSnapshot.cs` — no schema delta.
- Grading logic, aggregator, controllers — unchanged.
- Frontend route-grid / route-card — already iterate generically.
- About-page copy.

## Edge cases / error handling

- **Slug uniqueness:** all 37 new slugs must be unique and not collide with
  existing slugs (enforced by index). Verified by a seeding test.
- **SNOTEL:** a missing/failed station returns `null` snowpack — already tolerated;
  empty triplet is acceptable where no nearby station exists.
- **Polygon containment:** every new peak must sit inside its (possibly widened)
  range polygon, or it renders as a floating pin. Verified by a test.
- **Migration idempotency / fresh DB parity:** the migration `UPDATE` and the
  `RangeCatalog()` literal must carry byte-identical GeoJSON.

## Test plan

- **Backend** `dotnet build` + `dotnet test`:
  - All existing grading tests still pass (no logic change).
  - New seeding test: after seeding, the new slugs are present, total counts are
    25 / 20 / 11 for the three ranges, and there are no duplicate slugs.
  - New containment test: each seeded route's (lat, lon) lies within its range's
    `PerimeterGeoJson` polygon.
- **Frontend** `npm test`:
  - Clustering change asserted structurally (non-Colorado ranges now route into
    the cluster group), not via literal copy.
- **Manual:** `GET /api/routes` returns the higher count; map shows clustered
  pins for all ranges; new peaks render inside their dashed boundaries; a new
  slug (e.g. `mount-shuksan`) loads a detail view.

## Verification commands

```bash
dotnet build
dotnet test
cd frontend && npm test
# After running the app:
# curl http://localhost:5150/api/routes | jq 'length'   -> previous total + 37
```

## Branching

Base off `dev` (this is unrelated to `feature/precip-consensus`, already merged).
PR targets `dev`; verify on the Pages preview; then `dev` → `main`.
