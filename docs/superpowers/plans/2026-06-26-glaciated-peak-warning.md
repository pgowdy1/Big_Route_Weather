# Glaciated-Peak Warning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flag the 29 glaciated peaks in the database and surface a loud, persistent warning on each peak's detail page plus a muted marker on cards and map markers.

**Architecture:** A DB-backed `IsGlaciated` flag is the single source of truth in `RouteSeeder`. It flows to the live API (`/api/routes`) and to the generated SEO manifest (parity-guarded). The prerendered detail page reads it from the static manifest catalog; cards and map markers read it from the API DTO. Glaciation never touches grading.

**Tech Stack:** ASP.NET Core (.NET 10) + EF Core/SQLite backend; Angular 21 (zoneless, signals) + SCSS frontend; xUnit (backend) and Vitest + jsdom (frontend).

---

## Conventions for this plan

- **Windows / PowerShell** environment. Do **not** start `dotnet run` or `npm start` — the user keeps both servers in dedicated terminals.
- The running API locks the `RouteWeather.Core`/`.Data` DLLs, so **build/test by explicit csproj path**, never a bare `dotnet build`/`dotnet test` from `backend/`.
- Frontend: `npm test` runs Vitest **once** and exits. Do **not** pass `--watch=false` or Karma flags. Run it from `frontend/`.
- Every `git commit` message in this plan must end with the trailer:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- Branch is already `feature/glaciated-peak-warning` (off `dev`).

## The 29 glaciated slugs (single source of truth)

```
mount-rainier, mount-hood, mount-adams, mount-baker, mount-shasta, glacier-peak,
mount-shuksan, mount-stuart, forbidden-peak, dragontail-peak, eldorado-peak,
sahale-peak, bonanza-peak, goode-mountain, sloan-peak, silver-star-mountain,
north-sister, mount-jefferson, north-palisade, mount-sill, middle-palisade,
mount-lyell, mount-ritter, banner-peak, mount-darwin, mount-conness,
gannett-peak, mount-helen, mount-sacagawea
```

## File structure

| File | Change | Responsibility |
|---|---|---|
| `backend/RouteWeather.Data/Entities/RouteEntity.cs` | modify | add `IsGlaciated` column property |
| `backend/RouteWeather.Data/RouteSeeder.cs` | modify | `GlaciatedSlugs` set; apply flag in `BuildRoutes`; reconcile existing rows |
| `backend/RouteWeather.Data/Migrations/RouteWeatherContextModelSnapshot.cs` | modify | add `IsGlaciated` to the model snapshot |
| `backend/RouteWeather.Data/Migrations/20260626000000_AddIsGlaciated.cs` | create | add/drop the `IsGlaciated` SQLite column |
| `backend/RouteWeather.Data/Migrations/20260626000000_AddIsGlaciated.Designer.cs` | create | migration model snapshot |
| `backend/RouteWeather.Core.Tests/Data/RouteSeederTests.cs` | create | seeder flag + reconcile tests |
| `backend/RouteWeather.API/Controllers/RoutesController.cs` | modify | map `isGlaciated` into summary + detail DTOs |
| `backend/RouteWeather.API.Tests/RoutesControllerTests.cs` | modify | API exposes `isGlaciated` |
| `backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs` | modify | parity check on `IsGlaciated` |
| `frontend/scripts/generate-peaks-manifest.mjs` | modify | add `isGlaciated` to generated fields |
| `frontend/src/app/seo/peak-seo.ts` | modify | `isGlaciated` on `PeakSeo` |
| `frontend/src/app/seo/peaks.manifest.json` | modify | `isGlaciated` on all 124 entries |
| `frontend/src/app/models/route-conditions.ts` | modify | `isGlaciated` on `RouteSummary` |
| `frontend/src/app/components/route-card/route-card.spec.ts` | modify | fixture + glacier-chip test |
| `frontend/src/app/pages/map-home/map-home.spec.ts` | modify | fixture stays valid |
| `frontend/src/app/pages/peak-detail/peak-detail.spec.ts` | modify | fixture + banner test |
| `frontend/src/app/pages/peak-detail/peak-detail.html` | modify | warning banner |
| `frontend/src/app/pages/peak-detail/peak-detail.scss` | modify | banner styles |
| `frontend/src/app/components/route-card/route-card.html` | modify | glacier chip |
| `frontend/src/app/components/route-card/route-card.scss` | modify | glacier chip styles |
| `frontend/src/app/pages/map-home/map-home.ts` | modify | popup note + marker badge |
| `frontend/src/app/pages/map-home/map-home.scss` | modify | marker badge style |

---

## Task 1: DB flag + seeder (source of truth)

**Files:**
- Modify: `backend/RouteWeather.Data/Entities/RouteEntity.cs`
- Modify: `backend/RouteWeather.Data/RouteSeeder.cs`
- Test: `backend/RouteWeather.Core.Tests/Data/RouteSeederTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/RouteWeather.Core.Tests/Data/RouteSeederTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using RouteWeather.Data.Entities;
using Xunit;

namespace RouteWeather.Core.Tests.Data;

public class RouteSeederTests
{
    private static RouteWeatherContext NewContext() =>
        new(new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static readonly string[] ExpectedGlaciated =
    {
        "mount-rainier", "mount-hood", "mount-adams", "mount-baker", "mount-shasta",
        "glacier-peak", "mount-shuksan", "mount-stuart", "forbidden-peak", "dragontail-peak",
        "eldorado-peak", "sahale-peak", "bonanza-peak", "goode-mountain", "sloan-peak",
        "silver-star-mountain", "north-sister", "mount-jefferson", "north-palisade", "mount-sill",
        "middle-palisade", "mount-lyell", "mount-ritter", "banner-peak", "mount-darwin",
        "mount-conness", "gannett-peak", "mount-helen", "mount-sacagawea",
    };

    [Fact]
    public async Task Seeds_exactly_the_expected_glaciated_peaks()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var glaciated = await db.Routes.Where(r => r.IsGlaciated).Select(r => r.Slug).ToListAsync();

        Assert.Equal(29, glaciated.Count);
        Assert.Equal(ExpectedGlaciated.OrderBy(s => s), glaciated.OrderBy(s => s));
    }

    [Theory]
    [InlineData("pikes-peak")]
    [InlineData("south-sister")]
    [InlineData("mount-st-helens")]
    [InlineData("mount-whitney")]
    public async Task Walk_up_peaks_are_not_glaciated(string slug)
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var route = await db.Routes.SingleAsync(r => r.Slug == slug);
        Assert.False(route.IsGlaciated);
    }

    [Fact]
    public async Task Reconciles_IsGlaciated_on_existing_rows()
    {
        await using var db = NewContext();
        // First seed populates the catalog.
        await RouteSeeder.SeedAsync(db);
        // Corrupt two rows the way a pre-migration DB would look (all false).
        var rainier = await db.Routes.SingleAsync(r => r.Slug == "mount-rainier");
        var pikes = await db.Routes.SingleAsync(r => r.Slug == "pikes-peak");
        rainier.IsGlaciated = false;   // should be true
        pikes.IsGlaciated = true;      // should be false
        await db.SaveChangesAsync();

        // Second seed must reconcile both back to the catalog.
        await RouteSeeder.SeedAsync(db);

        Assert.True((await db.Routes.SingleAsync(r => r.Slug == "mount-rainier")).IsGlaciated);
        Assert.False((await db.Routes.SingleAsync(r => r.Slug == "pikes-peak")).IsGlaciated);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~RouteSeederTests"`
Expected: FAIL to compile — `RouteEntity` has no `IsGlaciated`.

- [ ] **Step 3: Add the entity property**

In `backend/RouteWeather.Data/Entities/RouteEntity.cs`, after the `ClassDifficulty` property (before `SnotelStationTriplet`):

```csharp
    [MaxLength(20)]
    public string ClassDifficulty { get; set; } = string.Empty;

    public bool IsGlaciated { get; set; }

    [MaxLength(40)]
    public string SnotelStationTriplet { get; set; } = string.Empty;
```

- [ ] **Step 4: Add the glaciated set + apply it + reconcile in the seeder**

In `backend/RouteWeather.Data/RouteSeeder.cs`, add the set as a `static readonly` field at the top of the class body (just after `public static class RouteSeeder {`):

```csharp
    // Single source of truth for which peaks carry a glacier that commonly-climbed
    // routes cross. Applied in BuildRoutes and reconciled onto existing rows.
    private static readonly HashSet<string> GlaciatedSlugs = new()
    {
        "mount-rainier", "mount-hood", "mount-adams", "mount-baker", "mount-shasta",
        "glacier-peak", "mount-shuksan", "mount-stuart", "forbidden-peak", "dragontail-peak",
        "eldorado-peak", "sahale-peak", "bonanza-peak", "goode-mountain", "sloan-peak",
        "silver-star-mountain", "north-sister", "mount-jefferson", "north-palisade", "mount-sill",
        "middle-palisade", "mount-lyell", "mount-ritter", "banner-peak", "mount-darwin",
        "mount-conness", "gannett-peak", "mount-helen", "mount-sacagawea",
    };
```

Replace the body of `BuildRoutes` so it materializes the list and stamps the flag in one place. The current `BuildRoutes` ends with `return Cascades(ca).Concat(...).Concat(Colorado14ers(co));` — change it to:

```csharp
        var routes = Cascades(ca)
            .Concat(Sierras(si))
            .Concat(WindRiver(wr))
            .Concat(Sawtooth(sa))
            .Concat(Wasatch(wa))
            .Concat(Colorado14ers(co))
            .ToList();

        foreach (var r in routes)
            r.IsGlaciated = GlaciatedSlugs.Contains(r.Slug);

        return routes;
```

In `SeedAsync`, the fresh-insert path already stamps the flag via `BuildRoutes`, so leave its early `return`. After the "add missing peaks" block, add a reconcile call. Change the tail of `SeedAsync` from:

```csharp
        if (toAdd.Count == 0) return;

        db.Routes.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
    }
```

to:

```csharp
        if (toAdd.Count > 0)
        {
            db.Routes.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }

        await ReconcileGlaciatedAsync(db, ct);
    }

    // The add-only path above never updates rows that already exist, so an
    // already-populated DB (dev/prod) would keep the migration default (false).
    // Bring every existing row's IsGlaciated in line with GlaciatedSlugs.
    private static async Task ReconcileGlaciatedAsync(RouteWeatherContext db, CancellationToken ct)
    {
        var rows = await db.Routes.ToListAsync(ct);
        var changed = false;
        foreach (var row in rows)
        {
            var shouldBe = GlaciatedSlugs.Contains(row.Slug);
            if (row.IsGlaciated != shouldBe)
            {
                row.IsGlaciated = shouldBe;
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~RouteSeederTests"`
Expected: PASS (4 cases incl. the 3 theory rows + reconcile).

- [ ] **Step 6: Commit**

```bash
git add backend/RouteWeather.Data/Entities/RouteEntity.cs backend/RouteWeather.Data/RouteSeeder.cs backend/RouteWeather.Core.Tests/Data/RouteSeederTests.cs
git commit -m "feat(data): IsGlaciated flag on routes, seeded + reconciled"
```

---

## Task 2: EF migration for the SQLite column

The running API blocks `dotnet ef migrations add`, so hand-author the migration trio. The in-memory test DB (Task 1) builds schema from the model directly, so this task is only for real SQLite; verify by building `RouteWeather.Data` alone.

**Files:**
- Modify: `backend/RouteWeather.Data/Migrations/RouteWeatherContextModelSnapshot.cs`
- Create: `backend/RouteWeather.Data/Migrations/20260626000000_AddIsGlaciated.cs`
- Create: `backend/RouteWeather.Data/Migrations/20260626000000_AddIsGlaciated.Designer.cs`

- [ ] **Step 1: Update the model snapshot**

In `backend/RouteWeather.Data/Migrations/RouteWeatherContextModelSnapshot.cs`, inside the `modelBuilder.Entity("RouteWeather.Data.Entities.RouteEntity", b => { ... })` block, add the property between `ClassDifficulty` and `Mountain` (EF orders non-key properties alphabetically; `IsGlaciated` sorts there):

```csharp
                    b.Property<string>("ClassDifficulty").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
                    b.Property<bool>("IsGlaciated").HasColumnType("INTEGER");
                    b.Property<string>("Mountain").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
```

- [ ] **Step 2: Create the migration**

Create `backend/RouteWeather.Data/Migrations/20260626000000_AddIsGlaciated.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsGlaciated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGlaciated",
                table: "Routes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGlaciated",
                table: "Routes");
        }
    }
}
```

- [ ] **Step 3: Create the migration designer (model snapshot at this migration)**

Create `backend/RouteWeather.Data/Migrations/20260626000000_AddIsGlaciated.Designer.cs`. Its `BuildTargetModel` body must be **identical** to the just-updated `RouteWeatherContextModelSnapshot.cs` `BuildModel` body (so it includes the new `IsGlaciated` line). Use this exact header, then paste the full body of the updated snapshot (everything between `#pragma warning disable 612, 618` and `#pragma warning restore 612, 618`, inclusive of both pragmas):

```csharp
// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RouteWeather.Data;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    [DbContext(typeof(RouteWeatherContext))]
    [Migration("20260626000000_AddIsGlaciated")]
    partial class AddIsGlaciated
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            // PASTE the body of RouteWeatherContextModelSnapshot.BuildModel here,
            // verbatim, including the `#pragma warning disable 612, 618` line, the
            // `modelBuilder.HasAnnotation("ProductVersion", ...)` line, every
            // modelBuilder.Entity(...) block (with the new IsGlaciated property),
            // and the closing `#pragma warning restore 612, 618`.
        }
    }
}
```

- [ ] **Step 4: Build the Data project alone to verify the trio compiles**

Run: `dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Data/Migrations/
git commit -m "feat(data): migration adding IsGlaciated column"
```

---

## Task 3: Expose `isGlaciated` from the API

**Files:**
- Modify: `backend/RouteWeather.API/Controllers/RoutesController.cs`
- Test: `backend/RouteWeather.API.Tests/RoutesControllerTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `backend/RouteWeather.API.Tests/RoutesControllerTests.cs` (inside the class, before the final `}`):

```csharp
    [Fact]
    public async Task GetAll_summaryIncludesIsGlaciated()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_summaryIncludesIsGlaciated));
        var ice = TestData.Route(id: 1, slug: "mt-ice", mountain: "Mt Ice");
        ice.IsGlaciated = true;
        var rock = TestData.Route(id: 2, slug: "mt-rock", mountain: "Mt Rock");
        await TestData.SeedRoutesAsync(dbFactory, ice, rock);
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        var summaries = Json(await controller.GetAll(CancellationToken.None));

        var iced = summaries.EnumerateArray().Single(s => s.GetProperty("slug").GetString() == "mt-ice");
        Assert.True(iced.GetProperty("isGlaciated").GetBoolean());
        var rocked = summaries.EnumerateArray().Single(s => s.GetProperty("slug").GetString() == "mt-rock");
        Assert.False(rocked.GetProperty("isGlaciated").GetBoolean());
    }

    [Fact]
    public async Task GetBySlug_detailIncludesIsGlaciated()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_detailIncludesIsGlaciated));
        var ice = TestData.Route(slug: "mt-test", mountain: "Mt Test");
        ice.IsGlaciated = true;
        await TestData.SeedRoutesAsync(dbFactory, ice);
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        var detail = Json(await controller.GetBySlug("mt-test", CancellationToken.None));

        Assert.True(detail.GetProperty("isGlaciated").GetBoolean());
    }
```

> Note: `TestData.Route(...)` returns a settable `RouteEntity`; if its default `slug` is not `"mt-test"`, pass `slug: "mt-test"` as shown so `GetBySlug("mt-test", …)` resolves.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter "FullyQualifiedName~IncludesIsGlaciated"`
Expected: FAIL — `isGlaciated` property missing from the JSON.

- [ ] **Step 3: Map the field in both DTOs**

In `backend/RouteWeather.API/Controllers/RoutesController.cs`, in `ToSummary`, add after `classDifficulty = c.Route.ClassDifficulty,`:

```csharp
            isGlaciated = route.IsGlaciated,
```

In `ToDetail`, add after `classDifficulty = c.Route.ClassDifficulty,`:

```csharp
        isGlaciated = route.IsGlaciated,
```

(Both methods already take `RouteEntity route` as their first parameter.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter "FullyQualifiedName~IncludesIsGlaciated"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.API/Controllers/RoutesController.cs backend/RouteWeather.API.Tests/RoutesControllerTests.cs
git commit -m "feat(api): expose isGlaciated in summary and detail"
```

---

## Task 4: Manifest + parity

**Files:**
- Modify: `frontend/src/app/seo/peak-seo.ts`
- Modify: `frontend/scripts/generate-peaks-manifest.mjs`
- Modify: `frontend/src/app/seo/peaks.manifest.json`
- Test: `backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs`

- [ ] **Step 1: Write the failing parity check**

In `backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs`, add `bool IsGlaciated` to the record (it deserializes case-insensitively from `isGlaciated`):

```csharp
    private sealed record PeakSeo(
        string Slug, string Mountain, string RouteName, int SummitElevationFt,
        string ClassDifficulty, string RangeName, string RangeSlug,
        double SummitLat, double SummitLon, bool IsGlaciated);
```

And add a check inside the `foreach (var r in seeded)` loop, next to the other `Check(...)` calls:

```csharp
            Check("IsGlaciated", r.IsGlaciated, p.IsGlaciated);
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~ManifestParityTests"`
Expected: FAIL — manifest entries deserialize `IsGlaciated=false` for the 29 peaks the seeder marks `true`.

- [ ] **Step 3: Add `isGlaciated` to the `PeakSeo` TS interface**

In `frontend/src/app/seo/peak-seo.ts`, add the field:

```typescript
export interface PeakSeo {
  slug: string;
  mountain: string;
  routeName: string;
  summitElevationFt: number;
  classDifficulty: string;
  rangeName: string;
  rangeSlug: string;
  summitLat: number;
  summitLon: number;
  isGlaciated: boolean;
}
```

- [ ] **Step 4: Add `isGlaciated` to the manifest generator fields**

In `frontend/scripts/generate-peaks-manifest.mjs`, extend `FIELDS`:

```javascript
const FIELDS = ['slug', 'mountain', 'routeName', 'summitElevationFt',
  'classDifficulty', 'rangeName', 'rangeSlug', 'summitLat', 'summitLon', 'isGlaciated'];
```

- [ ] **Step 5: Patch the committed manifest (no server needed)**

Create a one-shot script `frontend/scripts/patch-glaciated.mjs`:

```javascript
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const GLACIATED = new Set([
  'mount-rainier', 'mount-hood', 'mount-adams', 'mount-baker', 'mount-shasta', 'glacier-peak',
  'mount-shuksan', 'mount-stuart', 'forbidden-peak', 'dragontail-peak', 'eldorado-peak', 'sahale-peak',
  'bonanza-peak', 'goode-mountain', 'sloan-peak', 'silver-star-mountain', 'north-sister', 'mount-jefferson',
  'north-palisade', 'mount-sill', 'middle-palisade', 'mount-lyell', 'mount-ritter', 'banner-peak',
  'mount-darwin', 'mount-conness', 'gannett-peak', 'mount-helen', 'mount-sacagawea',
]);

const path = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'app', 'seo', 'peaks.manifest.json');
const peaks = JSON.parse(readFileSync(path, 'utf8'));
for (const p of peaks) p.isGlaciated = GLACIATED.has(p.slug);
writeFileSync(path, JSON.stringify(peaks, null, 2) + '\n');
console.log(`Patched ${peaks.length} peaks; ${peaks.filter(p => p.isGlaciated).length} glaciated.`);
```

Run it from `frontend/`:

Run: `node scripts/patch-glaciated.mjs`
Expected: `Patched 124 peaks; 29 glaciated.`

Then delete the one-shot script:

Run: `Remove-Item scripts/patch-glaciated.mjs`

- [ ] **Step 6: Run the parity test to verify it passes**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~ManifestParityTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/app/seo/peak-seo.ts frontend/scripts/generate-peaks-manifest.mjs frontend/src/app/seo/peaks.manifest.json backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs
git commit -m "feat(seo): carry isGlaciated through the manifest, parity-guarded"
```

---

## Task 5: Frontend model + fixtures

Adding `isGlaciated` to `RouteSummary` breaks every fixture that builds one. Fix the model and all fixtures in one green step (no behavior change yet).

**Files:**
- Modify: `frontend/src/app/models/route-conditions.ts`
- Modify: `frontend/src/app/components/route-card/route-card.spec.ts`
- Modify: `frontend/src/app/pages/map-home/map-home.spec.ts`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`

- [ ] **Step 1: Add the field to `RouteSummary`**

In `frontend/src/app/models/route-conditions.ts`, add to `RouteSummary` (after `classDifficulty`):

```typescript
export interface RouteSummary {
  slug: string;
  mountain: string;
  routeName: string;
  summitElevationFt: number;
  classDifficulty: string;
  isGlaciated: boolean;
  rangeSlug: string;
  rangeName: string;
  summitLat: number;
  summitLon: number;
  grade: Grade | null;
  overallScore: number | null;
  drivers: Driver[];
  updatedAt: string;
  isStale: boolean;
  consensus: Consensus | null;
  airQualityUsAqi: number | null;
}
```

(`RouteDetail extends RouteSummary`, so it inherits the field automatically.)

- [ ] **Step 2: Update the three fixtures to include `isGlaciated: false`**

In `frontend/src/app/components/route-card/route-card.spec.ts`, in the `summary()` builder, add `isGlaciated: false,` (e.g. after `classDifficulty: '3',`).

In `frontend/src/app/pages/map-home/map-home.spec.ts`, in the `summary()` builder, add `isGlaciated: false,` (after `classDifficulty: '2',`).

In `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`, in the `detail()` builder, add `isGlaciated: false,` (after `classDifficulty: '3',`).

- [ ] **Step 3: Run the full frontend suite to verify it still compiles and passes**

Run (from `frontend/`): `npm test`
Expected: PASS, no TS errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/app/models/route-conditions.ts frontend/src/app/components/route-card/route-card.spec.ts frontend/src/app/pages/map-home/map-home.spec.ts frontend/src/app/pages/peak-detail/peak-detail.spec.ts
git commit -m "feat(models): isGlaciated on RouteSummary; keep fixtures green"
```

---

## Task 6: Detail-page warning banner

**Files:**
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.html`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.scss`

- [ ] **Step 1: Write the failing tests**

Append to `frontend/src/app/pages/peak-detail/peak-detail.spec.ts` (inside the `describe`, before the final `}`):

```typescript
  it('shows the glaciated warning banner for a glaciated peak', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'mount-rainier'); // glaciated in the manifest
    fixture.detectChanges();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('.glacier-warning');
    expect(banner).not.toBeNull();
    expect(banner!.textContent ?? '').toContain('Glaciated peak');

    httpMock.expectOne('/api/routes/mount-rainier').flush({} as RouteDetail);
  });

  it('shows no glaciated warning for a non-glaciated peak', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'pikes-peak'); // not glaciated
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.glacier-warning')).toBeNull();

    httpMock.expectOne('/api/routes/pikes-peak').flush({} as RouteDetail);
  });
```

- [ ] **Step 2: Run them to verify they fail**

Run (from `frontend/`): `npm test -- peak-detail`
Expected: FAIL — `.glacier-warning` not found for `mount-rainier`.

- [ ] **Step 3: Add the banner to the template**

In `frontend/src/app/pages/peak-detail/peak-detail.html`, inside the existing `@if (peak(); as p) { ... }` block, immediately **after** the closing `</header>` of `.hero`, add:

```html
    @if (p.isGlaciated) {
      <aside class="glacier-warning" role="note">
        <h2 class="gw-title"><span aria-hidden="true">⚠️</span> Glaciated peak — hazards this grade does <strong>not</strong> measure</h2>
        <p>This grade reflects <strong>weather only.</strong> It does <strong>not</strong> account for glacier and snow/ice hazards: <strong>crevasses, falling séracs, heat-driven rockfall</strong> as snow and ice melt out, and <strong>changing route conditions</strong> (ice or rock newly exposed). Real conditions can be far more dangerous than the forecast suggests.</p>
        <p><strong>Glacier travel is serious mountaineering.</strong> Attempt these routes only with roped-team travel, crevasse-rescue skills, and adequate glacier experience.</p>
      </aside>
    }
```

- [ ] **Step 4: Style the banner**

Append to `frontend/src/app/pages/peak-detail/peak-detail.scss`:

```scss
.glacier-warning {
  margin: 1rem 0 0;
  padding: 1rem 1.25rem;
  border: 1px solid #c62828;
  border-left: 4px solid #ef5350;
  border-radius: 0.75rem;
  background: #2a1414;
  color: #ffd9d9;

  .gw-title {
    margin: 0 0 0.5rem;
    font-size: 1.05rem;
    line-height: 1.3;
    color: #ff8a80;
  }

  p { margin: 0.4rem 0 0; font-size: 0.9rem; line-height: 1.5; }
  strong { color: #fff; }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run (from `frontend/`): `npm test -- peak-detail`
Expected: PASS.

- [ ] **Step 6: Verify the component-style budget still holds**

Run (from `frontend/`): `npm run build`
Expected: Build succeeds with no `anyComponentStyle` budget error (limit 8 kB error / 7 kB warning). If `peak-detail.scss` exceeds 7 kB and warns, condense the new rules; if it errors at 8 kB, bump `anyComponentStyle` `maximumWarning`/`maximumError` in `frontend/angular.json` by 1 kB in this commit and note it.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/app/pages/peak-detail/
git commit -m "feat(detail): loud persistent glaciated-peak warning banner"
```

---

## Task 7: Muted glacier chip on cards

**Files:**
- Modify: `frontend/src/app/components/route-card/route-card.spec.ts`
- Modify: `frontend/src/app/components/route-card/route-card.html`
- Modify: `frontend/src/app/components/route-card/route-card.scss`

- [ ] **Step 1: Write the failing tests**

Append to `frontend/src/app/components/route-card/route-card.spec.ts` (inside the `describe`, before the final `}`):

```typescript
  it('shows a muted glacier chip when the peak is glaciated', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('mount-baker'), isGlaciated: true });
    fixture.detectChanges();

    const chip = (fixture.nativeElement as HTMLElement).querySelector('.glacier-chip');
    expect(chip).toBeTruthy();
    expect(chip!.textContent ?? '').toContain('Glaciated');
  });

  it('stays silent when the peak is not glaciated', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('pikes-peak'), isGlaciated: false });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.glacier-chip')).toBeNull();
  });
```

- [ ] **Step 2: Run them to verify they fail**

Run (from `frontend/`): `npm test -- route-card`
Expected: FAIL — `.glacier-chip` not found.

- [ ] **Step 3: Add the chip to the template**

In `frontend/src/app/components/route-card/route-card.html`, inside the `.meta` block, after the `range-chip` `@if` and before the `stale-chip` `@if`:

```html
    @if (route().isGlaciated) {
      <span class="glacier-chip" title="Glaciated peak — open for hazard details" aria-label="Glaciated peak — open for hazard details">❄ Glaciated</span>
    }
```

- [ ] **Step 4: Style the chip (muted, not red)**

In `frontend/src/app/components/route-card/route-card.scss`, after the `.range-chip` rule, add:

```scss
.glacier-chip {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.7rem;
  background: rgba(120, 170, 200, 0.16);
  color: #cfe2ee;
  letter-spacing: 0.02em;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run (from `frontend/`): `npm test -- route-card`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/components/route-card/
git commit -m "feat(cards): muted glacier chip on glaciated peaks"
```

---

## Task 8: Map popup note + marker badge

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts`
- Modify: `frontend/src/app/pages/map-home/map-home.spec.ts`
- Modify: `frontend/src/app/pages/map-home/map-home.scss`

- [ ] **Step 1: Write the failing test**

The popup string is built by `popupHtml`, currently un-exported. Export it and test it. First add the test — append to `frontend/src/app/pages/map-home/map-home.spec.ts` a new top-level `describe` (after the `markerIconSpec` describe), and extend the import on line 6:

Change the import:

```typescript
import { MapHome, markerIconSpec, popupHtml } from './map-home';
```

Add:

```typescript
describe('popupHtml', () => {
  function summary(overrides: Partial<RouteSummary> = {}): RouteSummary {
    return {
      slug: 'mt-x', mountain: 'Mt X', routeName: 'SW Ridge', summitElevationFt: 12000,
      classDifficulty: '2', isGlaciated: false, rangeSlug: 'r', rangeName: 'R',
      summitLat: 40, summitLon: -105, grade: 'A', overallScore: 92, drivers: [],
      updatedAt: new Date().toISOString(), isStale: false, consensus: null, airQualityUsAqi: null,
      ...overrides,
    };
  }

  it('adds a glaciated note when the route is glaciated', () => {
    expect(popupHtml(summary({ isGlaciated: true }))).toContain('Glaciated');
  });

  it('omits the glaciated note otherwise', () => {
    expect(popupHtml(summary({ isGlaciated: false }))).not.toContain('Glaciated');
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run (from `frontend/`): `npm test -- map-home`
Expected: FAIL — `popupHtml` is not exported (import error).

- [ ] **Step 3: Export `popupHtml` and add the note + marker badge**

In `frontend/src/app/pages/map-home/map-home.ts`:

(a) Export the function — change `function popupHtml(route: RouteSummary): string {` to:

```typescript
export function popupHtml(route: RouteSummary): string {
```

(b) Inside `popupHtml`, add a glaciated note line into the returned template, right after the `popup-sub` div:

```typescript
  return `
    <div class="popup-name">${escapeHtml(route.mountain)}</div>
    <div class="popup-sub">${route.summitElevationFt.toLocaleString()} ft &middot; Class ${escapeHtml(route.classDifficulty)}</div>
    ${route.isGlaciated ? '<div class="popup-glacier">❄ Glaciated — extreme hazard; see full forecast</div>' : ''}
    <div class="popup-grade grade-${grade.toLowerCase()}">${grade}</div>
    ${drivers}
    <a class="popup-cta" data-peak="${escapeHtml(route.slug)}" href="/peak/${escapeHtml(route.slug)}">View full forecast &rarr;</a>
  `;
```

(c) In `renderMarkers`, badge the glaciated marker's icon. Change the `html` of the `L.divIcon({...})` from `html: \`<span class="dot ${spec.dotClass}"></span>\`,` to:

```typescript
        html: `<span class="dot ${spec.dotClass}"></span>${route.isGlaciated ? '<span class="glacier-badge" aria-hidden="true">❄</span>' : ''}`,
```

- [ ] **Step 4: Style the badge + popup note**

Append to `frontend/src/app/pages/map-home/map-home.scss`:

```scss
.peak-marker .glacier-badge {
  position: absolute;
  top: -2px;
  right: -2px;
  font-size: 0.7rem;
  line-height: 1;
  color: #d6ecf7;
  text-shadow: 0 0 2px #0b1620, 0 0 2px #0b1620;
  pointer-events: none;
}

.popup-glacier {
  margin-top: 0.25rem;
  font-size: 0.72rem;
  color: #bcd9ea;
}
```

> If `.peak-marker` is not already `position: relative`, the badge will anchor to the wrong box. If the build/inspection shows that, add `position: relative;` to the existing `.peak-marker` rule in this file.

- [ ] **Step 5: Run the test to verify it passes**

Run (from `frontend/`): `npm test -- map-home`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/pages/map-home/
git commit -m "feat(map): glaciated marker badge and popup note"
```

---

## Task 9: Full verification

- [ ] **Step 1: Backend — run the three affected test projects**

Run:
```
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```
Expected: all PASS (RouteSeederTests, ManifestParityTests, RoutesControllerTests included).

- [ ] **Step 2: Frontend — full suite + production build**

Run (from `frontend/`):
```
npm test
npm run build
```
Expected: all tests PASS; build succeeds with no budget error.

- [ ] **Step 3: Confirm no stray files / plan didn't slip in**

Run: `git status --porcelain`
Expected: clean (the one-shot `patch-glaciated.mjs` was deleted in Task 4; the plan/spec under `docs/` are intentionally committed).

- [ ] **Step 4: Push the branch and open a PR to `dev`**

```bash
git push -u origin feature/glaciated-peak-warning
gh pr create --base dev --title "Glaciated-peak warning" --body "Implements docs/superpowers/specs/2026-06-25-glaciated-peak-warning-design.md — DB-backed IsGlaciated flag, loud detail-page banner, muted card/map markers."
```

---

## Manual verification checklist (on the dev preview after merge)

Hydration mismatches only surface in a real browser, so verify on the Pages preview, not just jsdom:

1. Open a glaciated peak (e.g. `/peak/mount-rainier/`) — the red warning banner is present in the **initial prerendered HTML** (visible before conditions load) and cannot be dismissed.
2. Open a non-glaciated peak (e.g. `/peak/pikes-peak/`) — no banner.
3. Grid card for a glaciated peak shows the muted "❄ Glaciated" chip; a non-glaciated card shows none.
4. Map marker for a glaciated peak shows the ❄ badge; its popup carries the glaciated note.
5. No console hydration (NG0500/NG0504) errors on the glaciated detail page.

---

## Self-review notes

- **Spec coverage:** DB flag + seeder (Task 1), migration (Task 2), API (Task 3), manifest/parity (Task 4), models (Task 5), banner (Task 6), card chip (Task 7), map badge/popup (Task 8), verification (Task 9). All spec sections covered.
- **Reconcile (the easy-to-miss backfill):** explicitly tested in Task 1 Step 1.
- **Type consistency:** `isGlaciated` (camelCase) on TS `RouteSummary`/`PeakSeo` and API JSON; `IsGlaciated` (Pascal) on C# `RouteEntity` and the parity record. `popupHtml` is exported in Task 8 before the spec imports it. `.glacier-warning`, `.glacier-chip`, `.glacier-badge`, `.popup-glacier` class names match between template, SCSS, and tests.
