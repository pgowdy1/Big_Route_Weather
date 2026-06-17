# Classic-Objective Peak Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 18 Cascade, 14 Sierra, and 5 Wasatch classic mountaineering objectives (one standard route each) to the seed catalog, widen two range map-polygons to contain them, and make every range's map pins cluster at low zoom.

**Architecture:** Peaks are appended as `RouteEntity` rows in `RouteSeeder.cs`; the seeder is already additive/idempotent so no route migration is needed. Two range polygons (Cascades, Sierra) are widened in `RangeCatalog()` for fresh DBs and via a data-only EF migration for deployed DBs. One conditional is removed from the Angular map render so all ranges cluster.

**Tech Stack:** ASP.NET Core / EF Core (SQLite) backend, xUnit tests; Angular (zoneless) + Vitest/jsdom frontend; Leaflet + markercluster.

**Branch:** `feature/classic-peaks-expansion` (already created off `origin/dev`).

**Spec:** `docs/superpowers/specs/2026-06-17-classic-peaks-expansion-design.md`

**Verified peak data:** Coordinates/elevations cross-checked against Wikipedia + SummitPost/PeakVisor. `ClassDifficulty` is a display-only string (the model types it `string`; the grader documents it as `"3", "4", "5.4", etc.`), so YDS 5th-class grades like `"5.6"` are stored verbatim and shown as `Class 5.6`. SNOTEL triplets reuse existing real stations by region (the existing convention already shares one station per range), so no station lookup is required: WA Cascades → `922:WA:SNTL`, OR Cascades → `651:OR:SNTL`, Lassen → `1067:CA:SNTL`, all Sierra → `428:CA:SNTL`, all Wasatch → `766:UT:SNTL`.

---

## File Structure

- `backend/RouteWeather.Data/RouteSeeder.cs` — **modify**: widen `cascades` + `sierra-nevada` polygons in `RangeCatalog()`; append 18/14/5 rows to `Cascades()` / `Sierras()` / `Wasatch()`.
- `backend/RouteWeather.Data/Migrations/20260617000000_ExpandCascadeSierraPolygons.cs` (+ `.Designer.cs`) — **create**: data-only `UPDATE` of the two polygons for already-seeded DBs. (`RouteWeatherContextModelSnapshot.cs` is **not** touched — there is no schema delta.)
- `backend/RouteWeather.Core.Tests/Data/RangeSeedingTests.cs` — **modify**: update route-count expectations (87 → 124), add per-range counts and a duplicate-slug guard.
- `frontend/src/app/pages/map-home/map-home.ts` — **modify**: cluster every range, not just `colorado-14ers`.

---

## Task 1: Widen the Cascade & Sierra range polygons

The new peaks extend past the current narrow boxes (Lassen sits south of the Cascade box; Conness/Matterhorn/Cathedral sit north and west of the Sierra box). Widen both — kept as simple axis-aligned rectangles, matching every other range. This task ships **before** the peaks so that when the peaks land in Task 2, the existing `Every_peak_falls_within_its_range_polygon` test stays green.

New boxes (lon × lat):
- Cascades: `-122.7…-120.3 × 40.2…49.1`
- Sierra Nevada: `-119.6…-117.9 × 36.1…38.3`
- Wasatch: unchanged (all 5 additions already fall inside `-112.0…-111.4 × 39.6…40.9`).

**Files:**
- Modify: `backend/RouteWeather.Data/RouteSeeder.cs` (the `cascades` and `sierra-nevada` entries in `RangeCatalog()`, ~lines 51-62)
- Create: `backend/RouteWeather.Data/Migrations/20260617000000_ExpandCascadeSierraPolygons.cs`
- Create: `backend/RouteWeather.Data/Migrations/20260617000000_ExpandCascadeSierraPolygons.Designer.cs`

- [ ] **Step 1: Widen the two polygons in `RangeCatalog()`**

In `RouteSeeder.cs`, replace the `cascades` `PerimeterGeoJson` line:

```csharp
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-122.7,49.1],[-120.3,49.1],[-120.3,40.2],[-122.7,40.2],[-122.7,49.1]]]}",
```

and the `sierra-nevada` `PerimeterGeoJson` line:

```csharp
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-119.6,38.3],[-117.9,38.3],[-117.9,36.1],[-119.6,36.1],[-119.6,38.3]]]}",
```

Leave `wind-river`, `sawtooth`, `wasatch`, and `colorado-14ers` exactly as they are.

- [ ] **Step 2: Create the data-only migration**

Create `backend/RouteWeather.Data/Migrations/20260617000000_ExpandCascadeSierraPolygons.cs` with this exact content (mirrors the prior `UpdateRangePolygons` migration; `Down` restores the pre-widening boxes):

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    public partial class ExpandCascadeSierraPolygons : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpdatePolygon(migrationBuilder, "cascades",      "{\"type\":\"Polygon\",\"coordinates\":[[[-122.7,49.1],[-120.3,49.1],[-120.3,40.2],[-122.7,40.2],[-122.7,49.1]]]}");
            UpdatePolygon(migrationBuilder, "sierra-nevada", "{\"type\":\"Polygon\",\"coordinates\":[[[-119.6,38.3],[-117.9,38.3],[-117.9,36.1],[-119.6,36.1],[-119.6,38.3]]]}");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            UpdatePolygon(migrationBuilder, "cascades",      "{\"type\":\"Polygon\",\"coordinates\":[[[-122.5,49.1],[-120.8,49.1],[-120.8,41.1],[-122.5,41.1],[-122.5,49.1]]]}");
            UpdatePolygon(migrationBuilder, "sierra-nevada", "{\"type\":\"Polygon\",\"coordinates\":[[[-118.8,37.4],[-117.9,37.4],[-117.9,36.2],[-118.8,36.2],[-118.8,37.4]]]}");
        }

        private static void UpdatePolygon(MigrationBuilder mb, string slug, string geoJson)
        {
            // SQLite single-quote escape: double them in the literal.
            var escaped = geoJson.Replace("'", "''");
            mb.Sql($"UPDATE Ranges SET PerimeterGeoJson = '{escaped}' WHERE Slug = '{slug}';");
        }
    }
}
```

- [ ] **Step 3: Create the migration Designer (snapshot is unchanged)**

EF requires a `.Designer.cs` carrying the model snapshot **as of this migration**. There is no schema change, so the snapshot is identical to the latest existing one. Two ways — pick whichever the environment allows:

- **Preferred (build not locked):** delete the `.cs` you just wrote, run
  `dotnet ef migrations add ExpandCascadeSierraPolygons --project backend/RouteWeather.Data --startup-project backend/RouteWeather.API`,
  then paste the Step 2 `Up`/`Down` bodies into the generated migration. EF writes the Designer + leaves the snapshot correct automatically.
- **Fallback (running API locks the build — see project memory):** copy the newest existing `*.Designer.cs` in `backend/RouteWeather.Data/Migrations/` (that is `20260608140000_UpdateRangePolygons.Designer.cs`) to `20260617000000_ExpandCascadeSierraPolygons.Designer.cs`, then change **only**:
  - the class name `UpdateRangePolygons` → `ExpandCascadeSierraPolygons`
  - the `[Migration("20260608140000_UpdateRangePolygons")]` attribute → `[Migration("20260617000000_ExpandCascadeSierraPolygons")]`

  Do **not** edit `RouteWeatherContextModelSnapshot.cs` — no schema changed.

- [ ] **Step 4: Build the Data project and run the seeding tests**

Run (build the Data project by path — `dotnet build` from `backend/` can fail if the API is running and locks the Core DLL, per project memory):

```bash
dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
```

Expected: Data builds clean; all existing tests PASS (the widened boxes still contain every existing peak; `Every_range_has_valid_GeoJSON_polygon_and_hex_color` still passes; route counts unchanged at this point).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Data/RouteSeeder.cs backend/RouteWeather.Data/Migrations/20260617000000_ExpandCascadeSierraPolygons.cs backend/RouteWeather.Data/Migrations/20260617000000_ExpandCascadeSierraPolygons.Designer.cs
git commit -m "feat(data): widen Cascade and Sierra range polygons for new peaks"
```

---

## Task 2: Add the 37 classic-objective peaks

Append the rows to the three existing factory methods. Update the seeding tests first (RED), then add the data (GREEN). Containment is already guarded by Task 1's widened polygons.

**Files:**
- Modify: `backend/RouteWeather.Core.Tests/Data/RangeSeedingTests.cs`
- Modify: `backend/RouteWeather.Data/RouteSeeder.cs` (`Cascades()`, `Sierras()`, `Wasatch()`)

- [ ] **Step 1: Update the seeding tests to the new catalog size (RED)**

In `RangeSeedingTests.cs`, replace the `Seeds_all_six_ranges_and_eighty_seven_routes` test with this (renamed, with per-range counts and a duplicate-slug guard):

```csharp
    [Fact]
    public async Task Seeds_all_six_ranges_with_expected_route_counts()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        Assert.Equal(6, await db.Ranges.CountAsync());
        Assert.Equal(124, await db.Routes.CountAsync());

        async Task<int> CountIn(string slug)
        {
            var id = await db.Ranges.Where(r => r.Slug == slug).Select(r => r.Id).SingleAsync();
            return await db.Routes.CountAsync(r => r.RangeId == id);
        }

        Assert.Equal(58, await CountIn("colorado-14ers"));
        Assert.Equal(25, await CountIn("cascades"));
        Assert.Equal(20, await CountIn("sierra-nevada"));
        Assert.Equal(11, await CountIn("wasatch"));

        Assert.All(await db.Routes.ToListAsync(), r => Assert.NotEqual(0, r.RangeId));

        var slugs = await db.Routes.Select(r => r.Slug).ToListAsync();
        Assert.Equal(slugs.Count, slugs.Distinct().Count()); // no duplicate slugs
    }
```

And in `SeedAsync_is_idempotent`, change both `Assert.Equal(87, await db.Routes.CountAsync());` occurrences (there is one in that test) to:

```csharp
        Assert.Equal(124, await db.Routes.CountAsync());
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj`
Expected: FAIL — `Seeds_all_six_ranges_with_expected_route_counts` and `SeedAsync_is_idempotent` report 87 actual vs 124 expected (peaks not added yet).

- [ ] **Step 3: Append the 18 Cascade peaks**

In `RouteSeeder.cs`, inside the `Cascades(int rangeId)` array, add these rows after the existing `mount-st-helens` row (before the closing `};`):

```csharp
        new RouteEntity { RangeId = rangeId, Slug = "mount-shuksan",        Mountain = "Mount Shuksan",        RouteName = "Fisher Chimneys",        SummitElevationFt = 9131,  SummitLat = 48.8315, SummitLon = -121.6032, ClassDifficulty = "4",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-stuart",         Mountain = "Mount Stuart",         RouteName = "West Ridge",             SummitElevationFt = 9415,  SummitLat = 47.4751, SummitLon = -120.9031, ClassDifficulty = "5.6", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "forbidden-peak",       Mountain = "Forbidden Peak",       RouteName = "West Ridge",             SummitElevationFt = 8815,  SummitLat = 48.5115, SummitLon = -121.0579, ClassDifficulty = "5.6", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "dragontail-peak",      Mountain = "Dragontail Peak",      RouteName = "Backbone Ridge",         SummitElevationFt = 8840,  SummitLat = 47.4787, SummitLon = -120.8334, ClassDifficulty = "5.9", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "eldorado-peak",        Mountain = "Eldorado Peak",        RouteName = "East Ridge",             SummitElevationFt = 8873,  SummitLat = 48.5374, SummitLon = -121.1345, ClassDifficulty = "2",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sahale-peak",          Mountain = "Sahale Peak",          RouteName = "Sahale Arm",             SummitElevationFt = 8680,  SummitLat = 48.4912, SummitLon = -121.0390, ClassDifficulty = "3",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "liberty-bell",         Mountain = "Liberty Bell",         RouteName = "Beckey Route",           SummitElevationFt = 7720,  SummitLat = 48.5154, SummitLon = -120.6579, ClassDifficulty = "5.6", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "bonanza-peak",         Mountain = "Bonanza Peak",         RouteName = "Mary Green Glacier",     SummitElevationFt = 9516,  SummitLat = 48.2382, SummitLon = -120.8664, ClassDifficulty = "4",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "goode-mountain",       Mountain = "Goode Mountain",       RouteName = "Northeast Buttress",     SummitElevationFt = 9220,  SummitLat = 48.4829, SummitLon = -120.9109, ClassDifficulty = "5.4", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "black-peak",           Mountain = "Black Peak",           RouteName = "South Ridge",            SummitElevationFt = 8975,  SummitLat = 48.5236, SummitLon = -120.8161, ClassDifficulty = "4",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sloan-peak",           Mountain = "Sloan Peak",           RouteName = "Corkscrew Route",        SummitElevationFt = 7835,  SummitLat = 48.0414, SummitLon = -121.3403, ClassDifficulty = "3",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "silver-star-mountain", Mountain = "Silver Star Mountain", RouteName = "Silver Star Glacier",    SummitElevationFt = 8876,  SummitLat = 48.5480, SummitLon = -120.5852, ClassDifficulty = "3",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "south-sister",         Mountain = "South Sister",         RouteName = "South Ridge",            SummitElevationFt = 10358, SummitLat = 44.1034, SummitLon = -121.7692, ClassDifficulty = "2",   SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-sister",         Mountain = "North Sister",         RouteName = "South Ridge",            SummitElevationFt = 10085, SummitLat = 44.1665, SummitLon = -121.7723, ClassDifficulty = "4",   SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-jefferson",      Mountain = "Mount Jefferson",      RouteName = "Jefferson Park Glacier", SummitElevationFt = 10497, SummitLat = 44.6743, SummitLon = -121.7996, ClassDifficulty = "5.2", SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-thielsen",       Mountain = "Mount Thielsen",       RouteName = "West Ridge",             SummitElevationFt = 9184,  SummitLat = 43.1528, SummitLon = -122.0665, ClassDifficulty = "4",   SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-mcloughlin",     Mountain = "Mount McLoughlin",     RouteName = "East Ridge",             SummitElevationFt = 9495,  SummitLat = 42.4445, SummitLon = -122.3156, ClassDifficulty = "2",   SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "lassen-peak",          Mountain = "Lassen Peak",          RouteName = "Southeast Slopes",       SummitElevationFt = 10457, SummitLat = 40.4881, SummitLon = -121.5050, ClassDifficulty = "1",   SnotelStationTriplet = "1067:CA:SNTL" },
```

- [ ] **Step 4: Append the 14 Sierra peaks**

Inside the `Sierras(int rangeId)` array, add these rows after the existing `mount-langley` row (before the closing `};`). Note the `Arête` rows use the `ê` character — consistent with the existing `Swiss Arête` row; keep the file UTF-8:

```csharp
        new RouteEntity { RangeId = rangeId, Slug = "mount-conness",        Mountain = "Mount Conness",        RouteName = "West Ridge",             SummitElevationFt = 12590, SummitLat = 37.9670, SummitLon = -119.3213, ClassDifficulty = "5.6", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "cathedral-peak",       Mountain = "Cathedral Peak",       RouteName = "Southeast Buttress",     SummitElevationFt = 10916, SummitLat = 37.8478, SummitLon = -119.4056, ClassDifficulty = "5.6", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "matterhorn-peak",      Mountain = "Matterhorn Peak",      RouteName = "North Arête",            SummitElevationFt = 12285, SummitLat = 38.0931, SummitLon = -119.3817, ClassDifficulty = "5.7", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-dana",           Mountain = "Mount Dana",           RouteName = "Northwest Slopes",       SummitElevationFt = 13061, SummitLat = 37.9000, SummitLon = -119.2211, ClassDifficulty = "2",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-lyell",          Mountain = "Mount Lyell",          RouteName = "Northwest Slopes",       SummitElevationFt = 13120, SummitLat = 37.7394, SummitLon = -119.2717, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-ritter",         Mountain = "Mount Ritter",         RouteName = "Southeast Glacier",      SummitElevationFt = 13149, SummitLat = 37.6894, SummitLon = -119.1992, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "banner-peak",          Mountain = "Banner Peak",          RouteName = "Northeast Ridge",        SummitElevationFt = 12942, SummitLat = 37.6967, SummitLon = -119.1953, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-humphreys",      Mountain = "Mount Humphreys",      RouteName = "East Arête",             SummitElevationFt = 13992, SummitLat = 37.2705, SummitLon = -118.6730, ClassDifficulty = "5.4", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-darwin",         Mountain = "Mount Darwin",         RouteName = "West Ridge",             SummitElevationFt = 13837, SummitLat = 37.1670, SummitLon = -118.6724, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "temple-crag",          Mountain = "Temple Crag",          RouteName = "Venusian Blind Arête",   SummitElevationFt = 12982, SummitLat = 37.1097, SummitLon = -118.4926, ClassDifficulty = "5.7", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "bear-creek-spire",     Mountain = "Bear Creek Spire",     RouteName = "North Arête",            SummitElevationFt = 13726, SummitLat = 37.3680, SummitLon = -118.7677, ClassDifficulty = "5.8", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-brewer",         Mountain = "Mount Brewer",         RouteName = "Northwest Ridge",        SummitElevationFt = 13576, SummitLat = 36.7085, SummitLon = -118.4854, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "middle-palisade",      Mountain = "Middle Palisade",      RouteName = "Northeast Face",         SummitElevationFt = 14018, SummitLat = 37.0703, SummitLon = -118.4691, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-tyndall",        Mountain = "Mount Tyndall",        RouteName = "Northwest Rib",          SummitElevationFt = 14025, SummitLat = 36.6557, SummitLon = -118.3373, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
```

- [ ] **Step 5: Append the 5 Wasatch peaks**

Inside the `Wasatch(int rangeId)` array, add these rows after the existing `box-elder-peak` row (before the closing `};`):

```csharp
        new RouteEntity { RangeId = rangeId, Slug = "broads-fork-twin-peaks", Mountain = "Broads Fork Twin Peaks", RouteName = "East Ridge",      SummitElevationFt = 11330, SummitLat = 40.5938, SummitLon = -111.7210, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "dromedary-peak",         Mountain = "Dromedary Peak",         RouteName = "West Ridge",      SummitElevationFt = 11107, SummitLat = 40.5930, SummitLon = -111.7060, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sunrise-peak",           Mountain = "Sunrise Peak",           RouteName = "South Ridge",     SummitElevationFt = 11275, SummitLat = 40.5909, SummitLon = -111.7112, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-superior",         Mountain = "Mount Superior",         RouteName = "Cardiff Ridge",   SummitElevationFt = 11045, SummitLat = 40.5922, SummitLon = -111.6670, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-raymond",          Mountain = "Mount Raymond",          RouteName = "West Ridge",      SummitElevationFt = 10241, SummitLat = 40.6584, SummitLon = -111.7020, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj`
Expected: PASS — total 124, per-range 25/20/11, Colorado still 58, no duplicate slugs, and `Every_peak_falls_within_its_range_polygon` confirms all new peaks fall inside the (Task 1) widened polygons. Idempotency test passes at 124.

- [ ] **Step 7: Commit**

```bash
git add backend/RouteWeather.Data/RouteSeeder.cs backend/RouteWeather.Core.Tests/Data/RangeSeedingTests.cs
git commit -m "feat(data): add 37 classic-objective peaks to Cascades, Sierra, Wasatch"
```

---

## Task 3: Cluster every range's pins on the map

`renderMarkers()` currently routes only `colorado-14ers` markers into the cluster group and drops every other range's pins individually. Tripling the Cascade/Sierra pin counts would overcrowd the default zoom, so cluster all interactive markers. (Ghost/non-interactive markers are handled earlier in the method and stay un-clustered — leave that block alone.)

**Test note:** `renderMarkers()` runs only after Leaflet initializes (`this.map` is non-null). In the Vitest/jsdom suite the map is never created, so `renderMarkers` no-ops and clustering is not reachable by a unit test — the same reason `initMap` is untested. The codebase only unit-tests the *extracted pure* marker decision (`markerIconSpec`). This change removes a branch rather than adding a decision, so there is no pure unit to add; it is verified by the existing suite staying green plus the manual map check in Task 4. Do **not** add a hollow Leaflet test.

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts` (the per-route branch in `renderMarkers`, ~lines 325-331)

- [ ] **Step 1: Replace the per-range branch with unconditional clustering**

In `renderMarkers()`, replace this block:

```typescript
      if (route.rangeSlug === 'colorado-14ers') {
        cluster.addLayer(marker);
        usedCluster = true;
      } else {
        marker.addTo(this.map);
        this.markerLayers.push(marker);
      }
```

with:

```typescript
      // Every range clusters at low zoom and separates at zoom >= 8
      // (disableClusteringAtZoom), so dense ranges (Sierra, Cascades, Colorado)
      // stay readable without per-range special-casing.
      cluster.addLayer(marker);
      usedCluster = true;
```

- [ ] **Step 2: Run the frontend suite**

Run (from `frontend/`): `npm test`
Expected: PASS — the full existing suite is green (the change does not touch any tested surface; `map-home.spec.ts` exercises HTTP/signals/DOM, not Leaflet clustering).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/app/pages/map-home/map-home.ts
git commit -m "feat(frontend): cluster every range's map pins, not just Colorado"
```

---

## Task 4: Full verification & PR

- [ ] **Step 1: Run the full backend and frontend suites**

```bash
dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
cd frontend && npm test
```

Expected: all green.

- [ ] **Step 2: Manual verification (requires the app running in the user's terminals — do not start the servers yourself)**

Ask the user to confirm against their running dev servers:
- `GET /api/routes` returns 124 items (was 87): e.g. `curl http://localhost:5150/api/routes | jq 'length'` → `124`.
- The map shows clustered pins for every range; zooming to ≥ 8 separates them.
- New peaks render **inside** their dashed range boundaries (no pins floating outside the Cascade/Sierra polygons).
- A new slug loads a detail view, e.g. `/peak/mount-shuksan` and `/peak/mount-conness`, showing `Class 5.6` etc.
- A first warm cycle populates grades for the new peaks (they may show ghost markers until the warmer fills the cache).

- [ ] **Step 3: Open the PR to `dev`**

```bash
git push -u origin feature/classic-peaks-expansion
gh pr create --base dev --title "Add classic-objective peaks (Cascades/Sierra/Wasatch) + cluster all ranges" --body "Implements docs/superpowers/specs/2026-06-17-classic-peaks-expansion-design.md. Adds 18 Cascade, 14 Sierra, 5 Wasatch classic objectives (124 routes total), widens the Cascade/Sierra map polygons (seeder + data migration), and clusters every range's pins. No schema change."
```

After merge to `dev`, verify on the Cloudflare Pages preview, then open the `dev` → `main` PR to ship.

---

## Self-Review

**Spec coverage:**
- Peak lists (18/14/5, classic objectives, one standard route, kebab slugs) → Task 2 Steps 3-5. ✓
- Verified lat/lon, class, SNOTEL per row → embedded in Task 2 (verified by research; SNOTEL by region convention). ✓
- Additive seeding, no route migration/schema change → relies on existing `SeedAsync`; counts asserted in Task 2 Step 1. ✓
- Cascade/Sierra polygon widening + data migration; Wasatch unchanged → Task 1. ✓
- `RangeCatalog()` literal and migration carry identical GeoJSON → same strings used in Task 1 Steps 1-2 (verify byte-identical). ✓
- Map clustering generalized to all ranges → Task 3. ✓
- API/warmer budget (no change needed) → spec §6; nothing to implement, noted in Task 4 Step 2 (warm cycle). ✓
- Tests: build + dotnet test + npm test; seeding count/containment/duplicate guards → Tasks 1/2/4. ✓
- Branch off dev; PR → dev → main → Task 4 Step 3. ✓

**Placeholder scan:** No TBD/TODO; the only `<timestamp>`-style value is the concrete `20260617000000` migration id. Designer step gives a concrete generate-or-copy procedure (snapshot is genuinely unchanged, so reproducing it by hand is neither needed nor desirable). ✓

**Type consistency:** `ClassDifficulty` stored as string (incl. `"5.6"`) matches the `string` model field; SNOTEL triplets match the existing `NNN:ST:SNTL` format; GeoJSON strings in Task 1 Step 1 (seeder) and Step 2 (migration `Up`) are identical; route-count math is consistent (7+18=25, 6+14=20, 6+5=11; 25+20+5+5+11+58 = 124). ✓
