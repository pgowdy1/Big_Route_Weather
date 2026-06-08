# Multi-range support + interactive map homepage

**Status:** Approved design, ready for implementation plan.
**Date:** 2026-06-08
**Branch base:** `dev`

## Goal

Expand Big_Route_Weather beyond Colorado 14ers to cover five additional Western US ranges (Cascades, Sierra Nevada, Wind River, Sawtooth, Wasatch) and replace the flat homepage grid with an interactive US map. The map's primary job is fast access — the user reaches any peak's route grade in at most two clicks (click dot → click popover CTA).

## Scope

In scope:
- New `Range` entity in the data model; every route belongs to exactly one range.
- 29 new curated peaks across five new ranges added to the seed; existing 58 Colorado 14ers retagged.
- New `MapHome` page on `/` showing a Leaflet map with range perimeter polygons + color-coded peak markers.
- Existing flat grid moves to `/all`, grouped by range (collapsible).
- One new API endpoint (`GET /api/ranges`), DTO additions to existing route endpoints.
- Removal of user-facing refresh affordances (button, signals, methods, controller actions) — server-side cache is the only refresh mechanism.

Out of scope:
- Spatial database extensions (SpatiaLite, NetTopologySuite). We do no spatial queries; polygons are render-only.
- Accurate USFS/USGS-derived range polygons. v1 ships with hand-authored rough approximations; the schema and code support a later swap via migration.
- A second snowpack source for Sierra coverage. Existing aggregator already tolerates SNOTEL nulls gracefully; we document the gap.
- A separate `/api/ranges/geometry` endpoint. Geometry is served inline with `/api/ranges`.
- Adding non-Western US ranges (Northeast, Appalachians, Tetons specifically, etc.) — those land later as additional range entries.

## Architecture overview

**Backend (`backend/`)**
- New `RangeEntity` table.
- `RouteEntity` gains `RangeId` (int FK, NOT NULL) + nav property `Range`.
- One hand-authored EF migration creates `Ranges`, inserts the 6 rows, adds `RangeId` to `Routes` (nullable), backfills existing routes to `colorado-14ers`, then enforces NOT NULL with FK.
- New `RangeRepository` (read-only catalog).
- New `RangesController` exposing `GET /api/ranges`.
- `RoutesController` DTOs gain `rangeSlug` + `rangeName`.
- `RoutesController.GetAllRefresh` and `GetBySlugRefresh` actions deleted.
- `RouteSeeder` extended to seed the 6 ranges then 87 routes.

**Frontend (`frontend/`)**
- New deps: `leaflet`, `leaflet.markercluster`, plus their `@types/*` packages.
- New `MapHome` component on `''` route (uses Leaflet over Carto Dark Matter tiles).
- Existing `RouteGrid` moved to `/all` with collapsible range groupings.
- New `RangesService` + `RangeMeta` model.
- `RouteCard` and `PeakDetail` gain a small range chip.
- Refresh buttons, `refreshing` signals, and `refresh()` methods removed from `RouteGrid` and `PeakDetail`; `RoutesService.listRefresh()` and `detailRefresh()` deleted.

## Data model

### `RangeEntity` (new)

`backend/RouteWeather.Data/Entities/RangeEntity.cs`

```csharp
public class RangeEntity
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Slug { get; set; } = string.Empty;          // e.g., "cascades"

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;          // e.g., "Cascade Range"

    [Required, MaxLength(9)]
    public string Color { get; set; } = string.Empty;         // hex, e.g., "#5fa8d8"

    [Required]
    public string PerimeterGeoJson { get; set; } = string.Empty;  // GeoJSON Polygon

    [MaxLength(500)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<RouteEntity> Routes { get; set; } = new List<RouteEntity>();
}
```

Constraints:
- Unique index on `Slug`.
- All columns NOT NULL except `Description`.

### `RouteEntity` additions

```csharp
public int RangeId { get; set; }
public RangeEntity? Range { get; set; }
```

Constraints:
- `RangeId` NOT NULL once migrated.
- Index on `Routes.RangeId`.
- FK with `OnDelete: Restrict` (no cascade — deleting a range with routes should fail loudly).

### Migration

Hand-authored per `feedback_handauthor_ef_migration.md` (the running API locks the Core DLL, blocking `dotnet ef migrations add`). Trio of files: `<timestamp>_AddRanges.cs` + `.Designer.cs` + updated `RouteWeatherContextModelSnapshot.cs`.

`Up()` order:
1. `CreateTable("Ranges", ...)` with all columns above and unique index on `Slug`.
2. `InsertData("Ranges", ...)` for the 6 ranges with their slugs, names, colors, hand-drawn polygon GeoJSON, descriptions, and display orders.
3. `AddColumn<int>("RangeId", "Routes", nullable: true)`.
4. Raw SQL: `UPDATE Routes SET RangeId = (SELECT Id FROM Ranges WHERE Slug = 'colorado-14ers')` — safe because every existing route is a Colorado 14er.
5. `AlterColumn<int>("RangeId", "Routes", nullable: false)`.
6. `AddForeignKey("FK_Routes_Ranges_RangeId", "Routes", "RangeId", "Ranges", principalColumn: "Id", onDelete: ReferentialAction.Restrict)`.
7. `CreateIndex("IX_Routes_RangeId", "Routes", "RangeId")`.

`Down()` reverses in opposite order.

Verification: build the Data project alone (`dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj`) to confirm the hand-authored migration compiles. Don't run from `backend/` because the API typically holds the lock.

## API surface

### `GET /api/ranges` (new)

Returns the full range catalog. No weather fan-out.

```json
[
  {
    "slug": "cascades",
    "name": "Cascade Range",
    "color": "#5fa8d8",
    "description": "Volcanic peaks of the Pacific Northwest.",
    "displayOrder": 1,
    "perimeterGeoJson": { "type": "Polygon", "coordinates": [...] }
  },
  ...
]
```

`perimeterGeoJson` is parsed from the stored string and emitted as a JSON object, not double-encoded.

Cache headers: `Cache-Control: public, max-age=3600, stale-while-revalidate=86400`. The Pages Function proxy in front of Fly passes through; the edge handles the actual caching.

### `GET /api/routes` (updated)

Each summary gains `rangeSlug` and `rangeName`:

```json
{
  "slug": "mount-rainier",
  "mountain": "Mount Rainier",
  "routeName": "Disappointment Cleaver",
  "summitElevationFt": 14411,
  "classDifficulty": "4",
  "rangeSlug": "cascades",
  "rangeName": "Cascade Range",
  "grade": "A",
  "overallScore": 92,
  "drivers": [...],
  "updatedAt": "...",
  "isStale": false,
  "consensus": {...}
}
```

`RouteRepository.GetAllAsync` and `GetBySlugAsync` are updated to `.Include(r => r.Range)`. `rangeName` is denormalized into the route DTO so the frontend doesn't need to cross-reference `/api/ranges` to render a route list.

### `GET /api/routes/{slug}` (updated)

Same `rangeSlug` + `rangeName` additions to detail DTO.

### `GET /api/routes/refresh` and `GET /api/routes/{slug}/refresh` (deleted)

Both endpoints removed from `RoutesController`. `ConditionsAggregator.GetConditionsAsync(...)` keeps its `useCache` parameter (still useful for testing).

## Frontend

### Routing (`frontend/src/app/app.routes.ts`)

```ts
{ path: '',           component: MapHome },
{ path: 'all',        component: RouteGrid },
{ path: 'peak/:slug', component: PeakDetail },
{ path: 'about',      component: About },
{ path: '**',         redirectTo: '' },
```

### `MapHome` (new, `pages/map-home/`)

Behavior:
- On init, `forkJoin([RangesService.list(), RoutesService.list()])` requests both in parallel.
- Map initialized inside `afterNextRender(...)` so SSR/prerender doesn't choke.
- `combineLatest`-style effect rebuilds polygon/marker layers whenever routes or ranges change.
- `ngOnDestroy` calls `map.remove()` to prevent leaks on navigation.
- Header shows "Updated Xm ago" pulled from `RoutesService.list()` response timestamps (the existing `lastFetchedAt` pattern from `RouteGrid`). No refresh button.

Map configuration:
- Tiles: Carto Dark Matter — `https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png`, attribution `© OpenStreetMap contributors © CARTO`.
- Initial view: lat 41.5, lng -113, zoom 5 (frames lower-48 Western US).
- `minZoom: 4`, `maxZoom: 12`.
- `maxBounds` set generously around the Western US.
- Scroll-wheel zoom, drag, and pinch-zoom enabled.

Range polygons:
- For each `RangeMeta`, `L.geoJSON(perimeterGeoJson, { style: { fillColor, fillOpacity: 0.22, color, weight: 1.5, dashArray: '4,3', interactive: false } })`.
- A `divIcon` label at polygon centroid showing range name in uppercase letterspaced text.
- Polygons on a non-interactive layer — never compete with peak dot clicks.

Peak markers:
- `colorado-14ers` markers go into an `L.markerClusterGroup` with `disableClusteringAtZoom: 8`. At low zoom they collapse to a single cluster bubble; zooming in splits them.
- All other ranges' markers added directly to the map (no cluster — their 5–7 peaks already spread well at the initial zoom).
- Each marker is a `divIcon` with a 14×14 px CSS circle, grade-color background, 2 px white border (boosted from 1.5 px for contrast on the dark base), 28×28 px CSS hit area for mobile.
- Hover state on desktop: enlarges to 18 px with a glow ring (CSS only).

Popover:
- `bindPopup` shows: peak name (bold), elevation + route class, large grade badge (A–F with grade color), top 2 drivers, "View full forecast →" CTA.
- CTA is a real `<a href="/peak/${slug}">`. A delegated click handler on the map container intercepts plain clicks, calls `event.preventDefault()`, and routes via `Router.navigate(['/peak', slug])`. Middle-click and right-click bypass the handler and behave as native anchors.

Search overlay:
- Small input + autocomplete in the map's top-left corner (semi-transparent dark background).
- Filters the loaded routes list by mountain name. Selecting a result pans the map to the peak and opens its popover.
- Collapses to a search icon at viewport widths < 480 px.

### `RangesService` (new, `services/ranges-service.ts`)

```ts
@Injectable({ providedIn: 'root' })
export class RangesService {
  private http = inject(HttpClient);

  list(): Observable<RangeMeta[]> {
    return this.http.get<RangeMeta[]>('/api/ranges');
  }
}
```

### `models/range.ts` (new)

```ts
export interface RangeMeta {
  slug: string;
  name: string;
  color: string;
  description: string | null;
  displayOrder: number;
  perimeterGeoJson: GeoJSON.Polygon;
}
```

### `models/route-conditions.ts` (updated)

Add to `RouteSummary` and `RouteDetail`:

```ts
rangeSlug: string;
rangeName: string;
```

Every inline `RouteSummary` builder in specs must add these per `.claude/rules/testing.md`.

### `RouteGrid` (updated, now on `/all`)

- Refresh button, `refreshing` signal, and `refresh()` method removed.
- Layout: render a `<details>` block per range (in `displayOrder`), each holding the existing `<app-route-card>` grid for routes in that range.
- `colorado-14ers` expanded by default; other ranges collapsed.
- Search input filters across all groups simultaneously — groups stay visible if any peak inside matches.
- Group headers show range name + count of peaks in that range matching the current filter.

### `RouteCard` and `PeakDetail` (updated)

- Small range chip rendered next to existing badges, colored with `rangeColor` resolved from the `RangesService` catalog (cached after first fetch).
- `PeakDetail`: refresh button, `refreshing` signal, and `refresh()` method removed; "Updated Xm ago" indicator stays.

### Header nav (`app.html`)

```html
<nav class="hero-nav">
  <a routerLink="/all" class="nav-link">All peaks</a>
  <a routerLink="/about" class="nav-link">About</a>
</nav>
```

## Curated peak list

Total: **87 routes** (58 retagged Colorado 14ers + 29 new).

### `cascades` — Cascade Range (color `#5fa8d8`, displayOrder 1)

| Peak | Route | Elev (ft) | Class | SNOTEL |
|---|---|---|---|---|
| Mount Rainier | Disappointment Cleaver | 14,411 | 4 | 679:WA:SNTL |
| Mount Hood | South Side / Hogsback | 11,239 | 3 | 651:OR:SNTL |
| Mount Adams | South Spur | 12,281 | 2 | 657:WA:SNTL |
| Mount Baker | Coleman-Deming | 10,781 | 3 | 999:WA:SNTL |
| Mount Shasta | Avalanche Gulch | 14,179 | 3 | 1067:CA:SNTL |
| Glacier Peak | Sitkum Glacier | 10,541 | 3 | 922:WA:SNTL |
| Mount St. Helens | Monitor Ridge | 8,366 | 2 | 999:WA:SNTL |

### `sierra-nevada` — Sierra Nevada (color `#d8a85f`, displayOrder 2)

| Peak | Route | Elev | Class | SNOTEL |
|---|---|---|---|---|
| Mount Whitney | Mountaineer's Route | 14,505 | 3 | 428:CA:SNTL |
| Mount Williamson | West Face | 14,379 | 3 | 428:CA:SNTL |
| North Palisade | LeConte Route | 14,248 | 4 | 428:CA:SNTL |
| Mount Sill | Swiss Arête | 14,159 | 4 | 428:CA:SNTL |
| Mount Russell | East Ridge | 14,094 | 3 | 428:CA:SNTL |
| Mount Langley | Old Army Pass | 14,032 | 2 | 428:CA:SNTL |

### `wind-river` — Wind River Range (color `#7fc878`, displayOrder 3)

| Peak | Route | Elev | Class | SNOTEL |
|---|---|---|---|---|
| Gannett Peak | Gooseneck Glacier | 13,809 | 3 | 1010:WY:SNTL |
| Fremont Peak | Southwest Slopes | 13,745 | 2 | 367:WY:SNTL |
| Mount Helen | East Ridge | 13,620 | 3 | 1010:WY:SNTL |
| Mount Sacagawea | NE Ridge | 13,569 | 2 | 1010:WY:SNTL |
| Wind River Peak | NW Ridge | 13,192 | 2 | 367:WY:SNTL |

### `sawtooth` — Sawtooth Range (color `#c898d8`, displayOrder 4)

| Peak | Route | Elev | Class | SNOTEL |
|---|---|---|---|---|
| Thompson Peak | Southwest Slopes | 10,751 | 3 | 837:ID:SNTL |
| Mount Heyburn | East Face Standard | 10,229 | 4 | 837:ID:SNTL |
| Mount Cramer | SE Ridge | 10,716 | 3 | 837:ID:SNTL |
| Williams Peak | South Slopes | 10,635 | 3 | 837:ID:SNTL |
| Snowyside Peak | NE Ridge | 10,651 | 2 | 837:ID:SNTL |

### `wasatch` — Wasatch Range (color `#f0a878`, displayOrder 5)

| Peak | Route | Elev | Class | SNOTEL |
|---|---|---|---|---|
| Mount Timpanogos | Aspen Grove | 11,752 | 2 | 766:UT:SNTL |
| Mount Nebo | North Peak Standard | 11,933 | 2 | 766:UT:SNTL |
| Lone Peak | NW Couloir | 11,253 | 3 | 766:UT:SNTL |
| Pfeifferhorn | NE Ridge | 11,326 | 3 | 766:UT:SNTL |
| Mount Olympus | Standard | 9,026 | 3 | 766:UT:SNTL |
| Box Elder Peak | South Ridge | 11,101 | 2 | 766:UT:SNTL |

### `colorado-14ers` — Colorado 14ers (color `#e8b04f`, displayOrder 6)

All 58 existing peaks in `RouteSeeder.Colorado14ers()`, retagged via migration backfill.

Summit lat/lng for new peaks are populated from public sources (Wikipedia/USGS GNIS) during seeder implementation. Polygon GeoJSON coordinates are hand-drawn rough approximations sufficient for v1 visual indication; they can be replaced with USFS/USGS-derived accurate polygons via a follow-up migration without changing any other code.

## Testing

### Backend (`backend/RouteWeather.Core.Tests/`)

- `RangeSeedTests` (new): every range entry's `PerimeterGeoJson` parses to a valid GeoJSON Polygon; slugs are unique; colors are valid 7-character hex; `Routes` collection populated correctly after seeding.
- `RouteSeederTests` (extended): post-seed, every `RouteEntity.RangeId` is non-zero and FK-resolvable. Colorado 14er count == 58. Total route count == 87.
- `RoutesController` integration: summary DTOs include `rangeSlug` + `rangeName`; refresh endpoints return 404.

### Frontend (Vitest + jsdom per `.claude/rules/testing.md`)

- `RangesService` spec: GETs `/api/ranges`, returns `RangeMeta[]`, HTTP mock asserts URL.
- `MapHome` spec: forkJoin of routes + ranges resolves into a render-ready state; error state when either call fails; loading state. Leaflet itself is not unit-tested (library boundary).
- `RouteGrid` spec updates: search filters across grouped peaks; collapse/expand each group; refresh-related assertions removed.
- `PeakDetail` spec: refresh-related assertions removed; range chip present.
- `RouteCard` spec: range chip rendered when `rangeSlug` present.
- All inline `RouteSummary` builders in specs updated to include `rangeSlug` and `rangeName` per `.claude/rules/testing.md`.

### Manual verification before merging

Per `feedback_plan_question_interactions.md`, walk the full combination end-to-end:

1. `/` — map renders, polygons drawn for all 6 ranges, all peak dots present.
2. Low zoom: Colorado 14ers display as a single cluster bubble; non-CO ranges' dots stay individual.
3. Zoom past level 8: CO 14ers split into individual dots.
4. Click dot → popover appears with grade + drivers + CTA.
5. Click "View full forecast" → `/peak/:slug` (SPA-routed, no full reload).
6. `/all` — 87 peaks visible, grouped by range, CO 14ers expanded, others collapsed.
7. Search "rainier" in `/all` → only matching peaks visible, group structure preserved.
8. `/peak/mount-rainier` shows range chip; no refresh button anywhere in the app.
9. `/api/routes/refresh` returns 404.
10. Mobile viewport (~390 px): popover readable; dots have 28 px hit area; search collapses to icon.

## Risks and known limitations

- **Sierra snowpack accuracy is limited** by sparse NRCS SNOTEL coverage in California. Bishop Pass (428:CA:SNTL) is the closest single station that reports consistently but is east-side and at a lower elevation than the Sierra peaks. The aggregator already tolerates SNOTEL nulls; the snowpack factor will naturally show as inactive when data is unavailable, which is the correct UX per `feedback_grading_signal_relevance.md`. Document the limitation in the About page in a follow-up.
- **SNOTEL stations are lower-elevation proxies** for most peaks (already true for Colorado 14ers). Not a regression.
- **Cold-start latency** on full uncached load: ~87 peaks × ~5 sources = ~435 fetches behind the existing 8-wide semaphore. First load after cache eviction could take 60–90 s. The 5-minute `IMemoryCache` plus the per-source 1-hour SQLite cache absorb subsequent loads. The edge cache (15 min + 1 h SWR) means real users almost never see the cold start.
- **Leaflet + zoneless interop**: Leaflet manipulates DOM outside Angular's view. Construction wrapped in `afterNextRender`; cleanup via `ngOnDestroy`. No CDR coordination needed because all marker/popover DOM is fully Leaflet-managed.
- **Polygon accuracy**: v1 ships with rough hand-drawn polygons. Schema, API, and rendering remain identical when polygons are later replaced with USFS-derived accurate versions.
- **Tile traffic**: Carto Dark Matter is free under attribution. If tile rate-limits ever become an issue, swap to OSM Standard or self-host via OpenMapTiles — no code change beyond the URL template.

## Memory follow-ups (after merge)

Two existing memory entries reference behavior changed by this work and will need updating:

- `project_two_tier_cache_architecture.md` says `/refresh bypasses tiers 1+2 only` — this becomes obsolete; the 3-tier cache stays but nothing bypasses it anymore.
- Add a new memory entry capturing the range data model (DB-stored GeoJSON in `Ranges.PerimeterGeoJson`) and the Leaflet + Carto Dark Matter rendering choice, so future sessions don't re-explore those.

## Summary of changes

- **1** new entity (`Range`)
- **1** schema change (`Route.RangeId` FK)
- **1** hand-authored migration with inline range data + backfill
- **29** new peaks added
- **58** existing peaks retagged
- **1** new API endpoint (`GET /api/ranges`)
- **2** API endpoints deleted (`/api/routes/refresh` + per-slug variant)
- **2** DTOs updated (route summary + detail)
- **1** new top-level page (`MapHome` on `/`)
- **1** route relocated (existing `RouteGrid` → `/all`)
- **2** new npm dependencies (`leaflet` + `leaflet.markercluster`) plus `@types/*`
- Cross-cutting cleanup removing user-facing refresh affordances
