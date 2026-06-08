# Multi-range map homepage — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add five Western US ranges (Cascades, Sierra Nevada, Wind River, Sawtooth, Wasatch) alongside the existing Colorado 14ers, and replace the homepage with an interactive Leaflet map where any peak's grade is reachable in ≤ 2 clicks.

**Architecture:** Add a `Range` entity with FK from `Route`. Seed 6 ranges + 29 new peaks. New `GET /api/ranges` endpoint serves catalog (incl. GeoJSON polygons). Existing flat grid moves from `/` to `/all` (grouped by range). New `MapHome` page on `/` renders Leaflet over Carto Dark Matter tiles with range polygons + clustered/individual peak markers and grade-popovers. User-facing refresh affordances removed across the board.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core (SQLite), xUnit. Angular 21 zoneless + signals, Vitest + jsdom, Leaflet 1.x + leaflet.markercluster.

**Spec:** `docs/superpowers/specs/2026-06-08-multi-range-map-design.md`

**Branch:** `feature/multi-range-map-spec` (already created off `origin/dev` and contains the design doc). Implementation continues on this branch.

---

## File structure

### Created
**Backend**
- `backend/RouteWeather.Data/Entities/RangeEntity.cs`
- `backend/RouteWeather.Data/Migrations/<timestamp>_AddRanges.cs`
- `backend/RouteWeather.Data/Migrations/<timestamp>_AddRanges.Designer.cs`
- `backend/RouteWeather.Data/Repositories/RangeRepository.cs`
- `backend/RouteWeather.API/Controllers/RangesController.cs`
- `backend/RouteWeather.Core.Tests/Data/RangeSeedingTests.cs`
- `backend/RouteWeather.Core.Tests/Data/RangeRepositoryTests.cs`

**Frontend**
- `frontend/src/app/models/range.ts`
- `frontend/src/app/services/ranges-service.ts`
- `frontend/src/app/services/ranges-service.spec.ts`
- `frontend/src/app/pages/map-home/map-home.ts`
- `frontend/src/app/pages/map-home/map-home.html`
- `frontend/src/app/pages/map-home/map-home.scss`
- `frontend/src/app/pages/map-home/map-home.spec.ts`

### Modified
**Backend**
- `backend/RouteWeather.Data/Entities/RouteEntity.cs` (adds RangeId + nav)
- `backend/RouteWeather.Data/RouteWeatherContext.cs` (adds Ranges DbSet + FK config)
- `backend/RouteWeather.Data/Migrations/RouteWeatherContextModelSnapshot.cs` (hand-edited)
- `backend/RouteWeather.Data/Repositories/RouteRepository.cs` (.Include(Range))
- `backend/RouteWeather.Data/RouteSeeder.cs` (seed ranges + 29 new peaks + retag 58 14ers)
- `backend/RouteWeather.API/Controllers/RoutesController.cs` (rangeSlug/rangeName in DTOs; remove refresh endpoints)
- `backend/RouteWeather.API/Program.cs` (register RangeRepository)

**Frontend**
- `frontend/package.json` (add leaflet, leaflet.markercluster, @types/leaflet, @types/leaflet.markercluster)
- `frontend/src/styles.scss` (import leaflet + markercluster CSS)
- `frontend/src/app/models/route-conditions.ts` (rangeSlug/rangeName on summary + detail)
- `frontend/src/app/services/routes-service.ts` (delete listRefresh/detailRefresh)
- `frontend/src/app/components/route-card/route-card.ts` + `.html` + `.scss` + `.spec.ts` (range chip)
- `frontend/src/app/components/route-grid/route-grid.ts` + `.html` + `.scss` + `.spec.ts` (remove refresh; group by range)
- `frontend/src/app/pages/peak-detail/peak-detail.ts` + `.html` + `.spec.ts` (remove refresh; range chip)
- `frontend/src/app/app.routes.ts` (MapHome on `/`; RouteGrid on `/all`)
- `frontend/src/app/app.html` (`/all` link in nav)

---

# Phase 1 — Frontend refresh removal

Doing this first so subsequent test changes don't have to also delete refresh assertions. No backend dep.

---

### Task 1: Delete refresh methods from `RoutesService`

**Files:**
- Modify: `frontend/src/app/services/routes-service.ts`

- [ ] **Step 1: Replace file contents**

```ts
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RouteSummary, RouteDetail } from '../models/route-conditions';

@Injectable({ providedIn: 'root' })
export class RoutesService {
  private http = inject(HttpClient);

  list(): Observable<RouteSummary[]> {
    return this.http.get<RouteSummary[]>('/api/routes');
  }

  detail(slug: string): Observable<RouteDetail> {
    return this.http.get<RouteDetail>(`/api/routes/${slug}`);
  }
}
```

- [ ] **Step 2: Build TypeScript to catch unresolved imports**

Run: `cd frontend && npx tsc --noEmit`
Expected: PASS (with later steps failing TS until we update callers — that's OK, this step proves the service compiles in isolation)

(If errors mention `listRefresh`/`detailRefresh` from `route-grid` or `peak-detail`, that's expected — fixed in Tasks 2–3.)

---

### Task 2: Remove refresh from `RouteGrid`

**Files:**
- Modify: `frontend/src/app/components/route-grid/route-grid.ts`
- Modify: `frontend/src/app/components/route-grid/route-grid.html`
- Modify: `frontend/src/app/components/route-grid/route-grid.spec.ts`

- [ ] **Step 1: Update component class — drop refreshing signal + refresh()**

Replace `route-grid.ts` with:

```ts
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RoutesService } from '../../services/routes-service';
import { RouteSummary } from '../../models/route-conditions';
import { RouteCard } from '../route-card/route-card';

@Component({
  selector: 'app-route-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouteCard],
  templateUrl: './route-grid.html',
  styleUrl: './route-grid.scss',
})
export class RouteGrid implements OnInit {
  private service = inject(RoutesService);

  routes = signal<RouteSummary[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);
  query = signal('');
  lastFetchedAt = signal<number | null>(null);

  filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.routes();
    return this.routes().filter(r => r.mountain.toLowerCase().includes(q));
  });

  lastUpdatedLabel = computed(() => {
    const t = this.lastFetchedAt();
    return t === null ? null : relativeFromNow(t);
  });

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.service.list().subscribe({
      next: r => {
        this.routes.set(r);
        this.lastFetchedAt.set(Date.now());
        this.loading.set(false);
      },
      error: e => {
        this.error.set(e?.message ?? 'Could not reach the backend');
        this.loading.set(false);
      },
    });
  }

  onSearch(value: string) {
    this.query.set(value);
  }
}

function relativeFromNow(ts: number): string {
  const diffMin = Math.max(0, Math.round((Date.now() - ts) / 60000));
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const hrs = Math.round(diffMin / 60);
  return `${hrs}h ago`;
}
```

- [ ] **Step 2: Remove the refresh button + refresh-related blocks from template**

In `route-grid.html`, replace the `.refresh` div with just the "Updated Xm ago" label:

```html
<div class="refresh">
  @if (lastUpdatedLabel(); as label) {
    <span class="updated">Updated {{ label }}</span>
  }
</div>
```

(Drop the `<button class="refresh-btn">` element entirely.)

- [ ] **Step 3: Remove refresh-related cases from `route-grid.spec.ts`**

Open the file and delete any `it(...)` block that mentions `refresh`, `refreshing`, or `/api/routes/refresh`. Update any HTTP mocks that expected `/api/routes/refresh` URLs.

- [ ] **Step 4: Run tests**

Run: `cd frontend && npm test -- route-grid`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/services/routes-service.ts frontend/src/app/components/route-grid/
git commit -m "refactor(frontend): remove user-facing refresh button from RouteGrid"
```

---

### Task 3: Remove refresh from `PeakDetail`

**Files:**
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.ts`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.html`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`

- [ ] **Step 1: Delete `refreshing` signal and `refresh()` method**

In `peak-detail.ts`, remove the `refreshing = signal(false)` line and the entire `refresh()` method (lines ~76–92 in current file). Don't touch anything else.

- [ ] **Step 2: Remove the refresh button from the template**

In `peak-detail.html`, find and delete the refresh button element (a `<button>` referencing `refresh()` and `refreshing()`).

- [ ] **Step 3: Remove refresh-related test cases from `peak-detail.spec.ts`**

Delete any `it(...)` block mentioning `refresh` or `/api/routes/.../refresh`.

- [ ] **Step 4: Run tests**

Run: `cd frontend && npm test -- peak-detail`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/pages/peak-detail/
git commit -m "refactor(frontend): remove user-facing refresh button from PeakDetail"
```

---

# Phase 2 — Backend data model

---

### Task 4: Add `RangeEntity` class

**Files:**
- Create: `backend/RouteWeather.Data/Entities/RangeEntity.cs`

- [ ] **Step 1: Write the entity**

```csharp
using System.ComponentModel.DataAnnotations;

namespace RouteWeather.Data.Entities;

public class RangeEntity
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Slug { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(9)]
    public string Color { get; set; } = string.Empty;

    [Required]
    public string PerimeterGeoJson { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<RouteEntity> Routes { get; set; } = new List<RouteEntity>();
}
```

- [ ] **Step 2: Build Data project**

Run: `dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj`
Expected: PASS

---

### Task 5: Add `RangeId` + nav to `RouteEntity`

**Files:**
- Modify: `backend/RouteWeather.Data/Entities/RouteEntity.cs`

- [ ] **Step 1: Append the new fields at the end of the class**

```csharp
public int RangeId { get; set; }
public RangeEntity? Range { get; set; }
```

So the class becomes:

```csharp
using System.ComponentModel.DataAnnotations;

namespace RouteWeather.Data.Entities;

public class RouteEntity
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Mountain { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string RouteName { get; set; } = string.Empty;

    public int SummitElevationFt { get; set; }
    public double SummitLat { get; set; }
    public double SummitLon { get; set; }

    [MaxLength(20)]
    public string ClassDifficulty { get; set; } = string.Empty;

    [MaxLength(40)]
    public string SnotelStationTriplet { get; set; } = string.Empty;

    public int RangeId { get; set; }
    public RangeEntity? Range { get; set; }
}
```

- [ ] **Step 2: Update `RouteWeatherContext.cs` to register Ranges + index**

Replace contents with:

```csharp
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data;

public class RouteWeatherContext : DbContext
{
    public RouteWeatherContext(DbContextOptions<RouteWeatherContext> options) : base(options) { }

    public DbSet<RouteEntity> Routes => Set<RouteEntity>();
    public DbSet<RangeEntity> Ranges => Set<RangeEntity>();
    public DbSet<CachedForecastEntity> CachedForecasts => Set<CachedForecastEntity>();
    public DbSet<DailyApiCallEntity> DailyApiCalls => Set<DailyApiCallEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RouteEntity>()
            .HasIndex(r => r.Slug)
            .IsUnique();

        modelBuilder.Entity<RouteEntity>()
            .HasOne(r => r.Range)
            .WithMany(g => g.Routes)
            .HasForeignKey(r => r.RangeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RouteEntity>()
            .HasIndex(r => r.RangeId);

        modelBuilder.Entity<RangeEntity>()
            .HasIndex(g => g.Slug)
            .IsUnique();

        modelBuilder.Entity<CachedForecastEntity>()
            .HasIndex(c => new { c.RouteId, c.Source })
            .IsUnique();

        modelBuilder.Entity<DailyApiCallEntity>()
            .HasIndex(c => new { c.DateUtc, c.Source })
            .IsUnique();

        modelBuilder.Entity<DailyApiCallEntity>()
            .Property(c => c.Source)
            .HasMaxLength(60);
    }
}
```

- [ ] **Step 3: Build Data project**

Run: `dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj`
Expected: PASS

---

### Task 6: Hand-author the migration trio

Per project memory `feedback_handauthor_ef_migration.md`, the API typically holds the Core DLL lock, so `dotnet ef migrations add` doesn't work. Author the trio by hand.

**Files:**
- Create: `backend/RouteWeather.Data/Migrations/20260608120000_AddRanges.cs`
- Create: `backend/RouteWeather.Data/Migrations/20260608120000_AddRanges.Designer.cs`
- Modify: `backend/RouteWeather.Data/Migrations/RouteWeatherContextModelSnapshot.cs`

- [ ] **Step 1: Create migration `Up`/`Down` file**

Create `20260608120000_AddRanges.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    public partial class AddRanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ranges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    PerimeterGeoJson = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_Ranges", x => x.Id); });

            migrationBuilder.CreateIndex(
                name: "IX_Ranges_Slug",
                table: "Ranges",
                column: "Slug",
                unique: true);

            migrationBuilder.AddColumn<int>(
                name: "RangeId",
                table: "Routes",
                type: "INTEGER",
                nullable: true);

            // Seed the colorado-14ers range FIRST so the backfill UPDATE below can find it.
            migrationBuilder.Sql(@"
                INSERT INTO Ranges (Slug, Name, Color, PerimeterGeoJson, Description, DisplayOrder)
                VALUES ('colorado-14ers', 'Colorado 14ers', '#e8b04f',
                        '{""type"":""Polygon"",""coordinates"":[[[-108.0,40.5],[-105.4,40.5],[-105.0,39.8],[-105.0,38.4],[-105.5,37.0],[-107.0,37.0],[-108.2,37.5],[-108.5,38.5],[-108.3,39.5],[-108.0,40.5]]]}',
                        'The 58 peaks above 14,000 ft in Colorado.',
                        6);");

            // Backfill: every existing route is a Colorado 14er.
            migrationBuilder.Sql(@"
                UPDATE Routes
                SET RangeId = (SELECT Id FROM Ranges WHERE Slug = 'colorado-14ers');");

            // Now make RangeId non-nullable + add the FK.
            migrationBuilder.AlterColumn<int>(
                name: "RangeId",
                table: "Routes",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Routes_Ranges_RangeId",
                table: "Routes",
                column: "RangeId",
                principalTable: "Ranges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_Routes_RangeId",
                table: "Routes",
                column: "RangeId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Routes_Ranges_RangeId",
                table: "Routes");

            migrationBuilder.DropIndex(
                name: "IX_Routes_RangeId",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "RangeId",
                table: "Routes");

            migrationBuilder.DropTable(
                name: "Ranges");
        }
    }
}
```

(Note: only the `colorado-14ers` row is inserted in the migration. The other 5 ranges and the 29 new peaks are added by the `RouteSeeder` at app startup in Task 12 — that's idempotent and keeps the migration small.)

- [ ] **Step 2: Create the Designer file**

Create `20260608120000_AddRanges.Designer.cs`. This is a copy of `RouteWeatherContextModelSnapshot.cs`'s `BuildModel` output but expressed as a `ModelSnapshot` for this specific migration. Build it by copy-pasting the existing `RouteWeatherContextModelSnapshot.cs` content into this designer file, then adjusting:

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
    [Migration("20260608120000_AddRanges")]
    partial class AddRanges
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.8");

            // ... include ALL entity definitions from the updated snapshot we author in Step 3 below.
            // Copy them verbatim once Step 3 is written.
#pragma warning restore 612, 618
        }
    }
}
```

Once Step 3 is complete, copy the four `modelBuilder.Entity(...)` blocks from the updated `RouteWeatherContextModelSnapshot.cs` into this file's `BuildTargetModel` body.

- [ ] **Step 3: Update `RouteWeatherContextModelSnapshot.cs`**

Replace contents:

```csharp
// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RouteWeather.Data;

#nullable disable

namespace RouteWeather.Data.Migrations
{
    [DbContext(typeof(RouteWeatherContext))]
    partial class RouteWeatherContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.8");

            modelBuilder.Entity("RouteWeather.Data.Entities.CachedForecastEntity", b =>
                {
                    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                    b.Property<DateTime>("ExpiresAtUtc").HasColumnType("TEXT");
                    b.Property<DateTime>("FetchedAtUtc").HasColumnType("TEXT");
                    b.Property<string>("PayloadJson").IsRequired().HasColumnType("TEXT");
                    b.Property<int>("RouteId").HasColumnType("INTEGER");
                    b.Property<string>("Source").IsRequired().HasColumnType("TEXT");
                    b.HasKey("Id");
                    b.HasIndex("RouteId", "Source").IsUnique();
                    b.ToTable("CachedForecasts");
                });

            modelBuilder.Entity("RouteWeather.Data.Entities.DailyApiCallEntity", b =>
                {
                    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                    b.Property<int>("Count").HasColumnType("INTEGER");
                    b.Property<DateOnly>("DateUtc").HasColumnType("TEXT");
                    b.Property<string>("Source").IsRequired().HasMaxLength(60).HasColumnType("TEXT");
                    b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
                    b.HasKey("Id");
                    b.HasIndex("DateUtc", "Source").IsUnique();
                    b.ToTable("DailyApiCalls");
                });

            modelBuilder.Entity("RouteWeather.Data.Entities.RangeEntity", b =>
                {
                    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                    b.Property<string>("Slug").IsRequired().HasMaxLength(60).HasColumnType("TEXT");
                    b.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
                    b.Property<string>("Color").IsRequired().HasMaxLength(9).HasColumnType("TEXT");
                    b.Property<string>("PerimeterGeoJson").IsRequired().HasColumnType("TEXT");
                    b.Property<string>("Description").HasMaxLength(500).HasColumnType("TEXT");
                    b.Property<int>("DisplayOrder").HasColumnType("INTEGER");
                    b.HasKey("Id");
                    b.HasIndex("Slug").IsUnique();
                    b.ToTable("Ranges");
                });

            modelBuilder.Entity("RouteWeather.Data.Entities.RouteEntity", b =>
                {
                    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                    b.Property<string>("ClassDifficulty").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
                    b.Property<string>("Mountain").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
                    b.Property<int>("RangeId").HasColumnType("INTEGER");
                    b.Property<string>("RouteName").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
                    b.Property<string>("Slug").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
                    b.Property<string>("SnotelStationTriplet").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
                    b.Property<int>("SummitElevationFt").HasColumnType("INTEGER");
                    b.Property<double>("SummitLat").HasColumnType("REAL");
                    b.Property<double>("SummitLon").HasColumnType("REAL");
                    b.HasKey("Id");
                    b.HasIndex("RangeId");
                    b.HasIndex("Slug").IsUnique();
                    b.HasOne("RouteWeather.Data.Entities.RangeEntity", "Range")
                        .WithMany("Routes")
                        .HasForeignKey("RangeId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                    b.Navigation("Range");
                    b.ToTable("Routes");
                });

            modelBuilder.Entity("RouteWeather.Data.Entities.RangeEntity", b => { b.Navigation("Routes"); });
#pragma warning restore 612, 618
        }
    }
}
```

- [ ] **Step 4: Finish the Designer file**

Copy the four `modelBuilder.Entity` blocks from Step 3 into the `BuildTargetModel` body of `20260608120000_AddRanges.Designer.cs`.

- [ ] **Step 5: Build the Data project to verify the migration trio compiles**

Run: `dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj`
Expected: PASS

- [ ] **Step 6: Stop the running API (if any), delete the dev SQLite, restart, and confirm the migration runs**

Manual: stop the API terminal. Delete `backend/RouteWeather.API/routeweather.db*` files. Restart the API (`cd backend/RouteWeather.API && dotnet run`). Expected: clean startup, schema created with both `Routes` and `Ranges` tables, no migration errors. The single `colorado-14ers` row is inserted by the migration; the other 5 ranges and new peaks are seeded by `RouteSeeder` in Task 12.

(After verifying, leave the API stopped — subsequent tasks build the Core/Data projects directly.)

- [ ] **Step 7: Commit**

```bash
git add backend/RouteWeather.Data/Entities/RangeEntity.cs \
        backend/RouteWeather.Data/Entities/RouteEntity.cs \
        backend/RouteWeather.Data/RouteWeatherContext.cs \
        backend/RouteWeather.Data/Migrations/20260608120000_AddRanges.cs \
        backend/RouteWeather.Data/Migrations/20260608120000_AddRanges.Designer.cs \
        backend/RouteWeather.Data/Migrations/RouteWeatherContextModelSnapshot.cs
git commit -m "feat(data): add Range entity + FK on Route, hand-authored migration"
```

---

### Task 7: Create `RangeRepository`

**Files:**
- Create: `backend/RouteWeather.Data/Repositories/RangeRepository.cs`
- Create: `backend/RouteWeather.Core.Tests/Data/RangeRepositoryTests.cs`

- [ ] **Step 1: Write failing test first**

Create `backend/RouteWeather.Core.Tests/Data/RangeRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;
using Xunit;

namespace RouteWeather.Core.Tests.Data;

public class RangeRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_returns_ranges_ordered_by_displayOrder()
    {
        var factory = InMemoryFactory();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.Ranges.AddRange(
                new RangeEntity { Slug = "b", Name = "B", Color = "#fff", PerimeterGeoJson = "{}", DisplayOrder = 2 },
                new RangeEntity { Slug = "a", Name = "A", Color = "#fff", PerimeterGeoJson = "{}", DisplayOrder = 1 });
            await seed.SaveChangesAsync();
        }

        var repo = new RangeRepository(factory);
        var ranges = await repo.GetAllAsync();

        Assert.Equal(new[] { "a", "b" }, ranges.Select(r => r.Slug));
    }

    private static IDbContextFactory<RouteWeatherContext> InMemoryFactory()
    {
        var opts = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(opts);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<RouteWeatherContext>
    {
        private readonly DbContextOptions<RouteWeatherContext> _opts;
        public TestDbContextFactory(DbContextOptions<RouteWeatherContext> opts) => _opts = opts;
        public RouteWeatherContext CreateDbContext() => new(_opts);
    }
}
```

Note: if `Microsoft.EntityFrameworkCore.InMemory` isn't already a dep of `RouteWeather.Core.Tests.csproj`, add it: `dotnet add backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory`.

- [ ] **Step 2: Run test to verify it fails (`RangeRepository` not defined)**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter RangeRepositoryTests`
Expected: FAIL — `RangeRepository` type missing.

- [ ] **Step 3: Implement `RangeRepository`**

Create `backend/RouteWeather.Data/Repositories/RangeRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data.Repositories;

public class RangeRepository
{
    private readonly IDbContextFactory<RouteWeatherContext> _dbFactory;

    public RangeRepository(IDbContextFactory<RouteWeatherContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<List<RangeEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Ranges
            .AsNoTracking()
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 4: Re-run test — expect PASS**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter RangeRepositoryTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Data/Repositories/RangeRepository.cs \
        backend/RouteWeather.Core.Tests/Data/RangeRepositoryTests.cs \
        backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
git commit -m "feat(data): add RangeRepository"
```

---

### Task 8: Update `RouteRepository` to include `Range`

**Files:**
- Modify: `backend/RouteWeather.Data/Repositories/RouteRepository.cs`

- [ ] **Step 1: Add `.Include(r => r.Range)` to both queries**

Replace the file with:

```csharp
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data.Repositories;

public class RouteRepository
{
    private readonly IDbContextFactory<RouteWeatherContext> _dbFactory;

    public RouteRepository(IDbContextFactory<RouteWeatherContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<List<RouteEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Routes
            .AsNoTracking()
            .Include(r => r.Range)
            .OrderBy(r => r.Mountain)
            .ToListAsync(ct);
    }

    public async Task<RouteEntity?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Routes
            .AsNoTracking()
            .Include(r => r.Range)
            .FirstOrDefaultAsync(r => r.Slug == slug, ct);
    }
}
```

- [ ] **Step 2: Build Data project**

Run: `dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add backend/RouteWeather.Data/Repositories/RouteRepository.cs
git commit -m "feat(data): eager-load Range on route reads"
```

---

# Phase 3 — Backend API

---

### Task 9: Add `RangesController` with `GET /api/ranges`

**Files:**
- Create: `backend/RouteWeather.API/Controllers/RangesController.cs`
- Modify: `backend/RouteWeather.API/Program.cs`

- [ ] **Step 1: Create the controller**

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Controllers;

[ApiController]
[Route("api/ranges")]
public class RangesController : ControllerBase
{
    private const string CachePolicy = "public, max-age=3600, stale-while-revalidate=86400";

    private readonly RangeRepository _ranges;

    public RangesController(RangeRepository ranges) => _ranges = ranges;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var ranges = await _ranges.GetAllAsync(ct);
        var dto = ranges.Select(r => new
        {
            slug = r.Slug,
            name = r.Name,
            color = r.Color,
            description = r.Description,
            displayOrder = r.DisplayOrder,
            perimeterGeoJson = ParseGeoJson(r.PerimeterGeoJson),
        }).ToList();

        Response.Headers.CacheControl = CachePolicy;
        return Ok(dto);
    }

    private static object? ParseGeoJson(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<JsonElement>(raw);
}
```

- [ ] **Step 2: Register `RangeRepository` in DI**

In `backend/RouteWeather.API/Program.cs`, find the line `builder.Services.AddScoped<RouteRepository>();` and add immediately after it:

```csharp
builder.Services.AddScoped<RangeRepository>();
```

- [ ] **Step 3: Build the API project**

Run: `dotnet build backend/RouteWeather.API/RouteWeather.API.csproj`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add backend/RouteWeather.API/Controllers/RangesController.cs \
        backend/RouteWeather.API/Program.cs
git commit -m "feat(api): add GET /api/ranges endpoint"
```

---

### Task 10: Update `RoutesController` DTOs + remove refresh endpoints

**Files:**
- Modify: `backend/RouteWeather.API/Controllers/RoutesController.cs`

- [ ] **Step 1: Replace controller contents**

```csharp
using Microsoft.AspNetCore.Mvc;
using RouteWeather.API.Services;
using RouteWeather.Core.Models;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Controllers;

[ApiController]
[Route("api/routes")]
public class RoutesController : ControllerBase
{
    private const int MaxConcurrentFetches = 8;
    private const string CachedPolicy = "public, max-age=900, stale-while-revalidate=3600";

    private readonly RouteRepository _routes;
    private readonly ConditionsAggregator _aggregator;

    public RoutesController(RouteRepository routes, ConditionsAggregator aggregator)
    {
        _routes = routes;
        _aggregator = aggregator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var routes = await _routes.GetAllAsync(ct);
        using var gate = new SemaphoreSlim(MaxConcurrentFetches, MaxConcurrentFetches);

        var tasks = routes.Select(async r =>
        {
            await gate.WaitAsync(ct);
            try { return (Route: r, Conditions: await _aggregator.GetConditionsAsync(r, useCache: true, ct)); }
            finally { gate.Release(); }
        });

        var pairs = await Task.WhenAll(tasks);
        var dto = pairs.Select(p => ToSummary(p.Route, p.Conditions)).ToList();
        Response.Headers.CacheControl = CachedPolicy;
        return Ok(dto);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var route = await _routes.GetBySlugAsync(slug, ct);
        if (route is null) return NotFound();
        var conditions = await _aggregator.GetConditionsAsync(route, useCache: true, ct);
        Response.Headers.CacheControl = CachedPolicy;
        return Ok(ToDetail(route, conditions));
    }

    private static object ToSummary(RouteEntity route, RouteConditions c)
    {
        var window = c.WindowGrades?.Next24h;
        return new
        {
            slug = c.Route.Slug,
            mountain = c.Route.Mountain,
            routeName = c.Route.RouteName,
            summitElevationFt = c.Route.SummitElevationFt,
            classDifficulty = c.Route.ClassDifficulty,
            rangeSlug = route.Range?.Slug ?? string.Empty,
            rangeName = route.Range?.Name ?? string.Empty,
            grade = window?.Grade?.ToString(),
            overallScore = window?.OverallScore,
            drivers = window?.Drivers ?? Array.Empty<Driver>(),
            updatedAt = c.UpdatedAt,
            isStale = c.IsStale,
            consensus = SerializeConsensus(c.Consensus),
        };
    }

    private static object ToDetail(RouteEntity route, RouteConditions c) => new
    {
        slug = c.Route.Slug,
        mountain = c.Route.Mountain,
        routeName = c.Route.RouteName,
        summitElevationFt = c.Route.SummitElevationFt,
        summitLat = c.Route.SummitLat,
        summitLon = c.Route.SummitLon,
        classDifficulty = c.Route.ClassDifficulty,
        rangeSlug = route.Range?.Slug ?? string.Empty,
        rangeName = route.Range?.Name ?? string.Empty,
        grade = c.Grade?.ToString(),
        overallScore = c.OverallScore,
        drivers = c.Drivers,
        factors = c.Factors,
        rationale = c.Rationale,
        updatedAt = c.UpdatedAt,
        isStale = c.IsStale,
        forecastNext48h = c.Weather?.Next48Hours,
        snowpack = c.Snowpack,
        windowGrades = c.WindowGrades is null ? null : new
        {
            next12h = SerializeWindow(c.WindowGrades.Next12h),
            next24h = SerializeWindow(c.WindowGrades.Next24h),
            next48h = SerializeWindow(c.WindowGrades.Next48h),
        },
        sources = new
        {
            nws = new { fetchedAt = c.Sources.NwsFetchedAt },
            snotel = new { fetchedAt = c.Sources.SnotelFetchedAt },
        },
        consensus = SerializeConsensus(c.Consensus),
        perSourceForecast = c.PerSourceForecast?.Select(p => new
        {
            sourceName = p.SourceName,
            windMph = p.WindMph,
            tempF = p.TempF,
            precipitationProbabilityPct = p.PrecipitationProbabilityPct,
            fetchedAt = p.FetchedAt,
        }),
    };

    private static object? SerializeConsensus(ConsensusReport? r) => r is null ? null : new
    {
        level = r.Level.ToString().ToLowerInvariant(),
        worstFactor = r.WorstFactor,
        coefficientOfVariationByFactor = r.CoefficientOfVariationByFactor,
        sourcesReporting = r.SourcesReporting,
        sourcesAttempted = r.SourcesAttempted,
    };

    private static object SerializeWindow(WindowGrade w) => new
    {
        grade = w.Grade?.ToString(),
        overallScore = w.OverallScore,
        hoursCovered = w.HoursCovered,
        factors = w.Factors,
        drivers = w.Drivers,
        rationale = w.Rationale,
    };
}
```

Key diffs vs. the existing file:
- `GetAllRefresh` and `GetBySlugRefresh` actions removed.
- `RefreshPolicy` constant removed.
- `ToSummary`/`ToDetail` accept `RouteEntity` so they can read `route.Range`.
- DTOs include `rangeSlug` + `rangeName`.

- [ ] **Step 2: Build the API project**

Run: `dotnet build backend/RouteWeather.API/RouteWeather.API.csproj`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add backend/RouteWeather.API/Controllers/RoutesController.cs
git commit -m "feat(api): rangeSlug/rangeName in route DTOs; drop refresh endpoints"
```

---

# Phase 4 — Backend seeding

---

### Task 11: Extract range catalog constants

Goal: keep the seeder readable by extracting the 6 ranges into a private helper section. We don't need a public `RangeCatalog` class — only `RouteSeeder` uses this data.

**Files:**
- Modify: `backend/RouteWeather.Data/RouteSeeder.cs` (full rewrite — large but mechanical)

- [ ] **Step 1: Rewrite `RouteSeeder.cs`**

Replace the whole file with:

```csharp
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data;

public static class RouteSeeder
{
    public static async Task SeedAsync(RouteWeatherContext db, CancellationToken ct = default)
    {
        var ranges = await EnsureRangesAsync(db, ct);

        if (!await db.Routes.AnyAsync(ct))
        {
            db.Routes.AddRange(BuildRoutes(ranges));
            await db.SaveChangesAsync(ct);
            return;
        }

        // Routes already exist (the migration backfilled them to colorado-14ers). Add any peaks
        // from the catalog that aren't yet in the DB (the new 29). Existing CO 14ers stay tagged
        // to colorado-14ers by the migration.
        var existing = await db.Routes.Select(r => r.Slug).ToListAsync(ct);
        var existingSet = existing.ToHashSet();
        var toAdd = BuildRoutes(ranges).Where(r => !existingSet.Contains(r.Slug)).ToList();
        if (toAdd.Count == 0) return;

        db.Routes.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Dictionary<string, int>> EnsureRangesAsync(RouteWeatherContext db, CancellationToken ct)
    {
        var existing = await db.Ranges.ToListAsync(ct);
        var bySlug = existing.ToDictionary(r => r.Slug, r => r.Id);

        foreach (var range in RangeCatalog())
        {
            if (bySlug.ContainsKey(range.Slug)) continue;
            db.Ranges.Add(range);
            await db.SaveChangesAsync(ct);
            bySlug[range.Slug] = range.Id;
        }

        return bySlug;
    }

    private static IEnumerable<RangeEntity> RangeCatalog() => new[]
    {
        new RangeEntity
        {
            Slug = "cascades", Name = "Cascade Range", Color = "#5fa8d8",
            Description = "Volcanic peaks of the Pacific Northwest.",
            DisplayOrder = 1,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-121.5,48.9],[-121.0,48.5],[-120.9,47.3],[-121.5,46.4],[-121.8,45.4],[-122.0,44.8],[-122.2,43.5],[-121.9,42.5],[-122.3,41.3],[-122.6,40.5],[-123.0,41.0],[-122.5,42.5],[-122.4,43.7],[-122.3,45.0],[-122.0,46.2],[-121.9,47.5],[-121.7,48.8],[-121.5,48.9]]]}",
        },
        new RangeEntity
        {
            Slug = "sierra-nevada", Name = "Sierra Nevada", Color = "#d8a85f",
            Description = "The high granite range of eastern California.",
            DisplayOrder = 2,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-120.5,39.5],[-120.0,38.8],[-119.2,38.4],[-118.7,37.4],[-118.5,36.7],[-118.2,36.4],[-117.9,35.9],[-118.5,35.7],[-119.0,36.5],[-119.4,37.0],[-119.8,37.5],[-120.2,38.2],[-120.7,38.8],[-120.9,39.4],[-120.5,39.5]]]}",
        },
        new RangeEntity
        {
            Slug = "wind-river", Name = "Wind River Range", Color = "#7fc878",
            Description = "Remote granite spires and big glaciers in west-central Wyoming.",
            DisplayOrder = 3,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-110.0,43.5],[-109.5,43.2],[-109.0,42.8],[-108.7,42.5],[-108.9,42.3],[-109.4,42.7],[-109.9,43.1],[-110.2,43.4],[-110.0,43.5]]]}",
        },
        new RangeEntity
        {
            Slug = "sawtooth", Name = "Sawtooth Range", Color = "#c898d8",
            Description = "Compact granite range in central Idaho.",
            DisplayOrder = 4,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-115.2,44.4],[-114.7,44.4],[-114.6,43.9],[-114.9,43.7],[-115.2,43.9],[-115.2,44.4]]]}",
        },
        new RangeEntity
        {
            Slug = "wasatch", Name = "Wasatch Range", Color = "#f0a878",
            Description = "Northern Utah's front range.",
            DisplayOrder = 5,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-111.9,41.5],[-111.4,41.5],[-111.4,40.0],[-111.6,39.4],[-111.9,39.4],[-112.0,40.0],[-111.9,41.5]]]}",
        },
        new RangeEntity
        {
            Slug = "colorado-14ers", Name = "Colorado 14ers", Color = "#e8b04f",
            Description = "The 58 peaks above 14,000 ft in Colorado.",
            DisplayOrder = 6,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-108.0,40.5],[-105.4,40.5],[-105.0,39.8],[-105.0,38.4],[-105.5,37.0],[-107.0,37.0],[-108.2,37.5],[-108.5,38.5],[-108.3,39.5],[-108.0,40.5]]]}",
        },
    };

    private static IEnumerable<RouteEntity> BuildRoutes(IReadOnlyDictionary<string, int> rangeIds)
    {
        int co = rangeIds["colorado-14ers"];
        int ca = rangeIds["cascades"];
        int si = rangeIds["sierra-nevada"];
        int wr = rangeIds["wind-river"];
        int sa = rangeIds["sawtooth"];
        int wa = rangeIds["wasatch"];

        return Cascades(ca)
            .Concat(Sierras(si))
            .Concat(WindRiver(wr))
            .Concat(Sawtooth(sa))
            .Concat(Wasatch(wa))
            .Concat(Colorado14ers(co));
    }

    private static IEnumerable<RouteEntity> Cascades(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-rainier",        Mountain = "Mount Rainier",     RouteName = "Disappointment Cleaver", SummitElevationFt = 14411, SummitLat = 46.8523, SummitLon = -121.7603, ClassDifficulty = "4", SnotelStationTriplet = "679:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-hood",           Mountain = "Mount Hood",        RouteName = "South Side (Hogsback)",  SummitElevationFt = 11239, SummitLat = 45.3736, SummitLon = -121.6960, ClassDifficulty = "3", SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-adams",          Mountain = "Mount Adams",       RouteName = "South Spur",             SummitElevationFt = 12281, SummitLat = 46.2024, SummitLon = -121.4909, ClassDifficulty = "2", SnotelStationTriplet = "657:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-baker",          Mountain = "Mount Baker",       RouteName = "Coleman-Deming",         SummitElevationFt = 10781, SummitLat = 48.7768, SummitLon = -121.8145, ClassDifficulty = "3", SnotelStationTriplet = "999:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-shasta",         Mountain = "Mount Shasta",      RouteName = "Avalanche Gulch",        SummitElevationFt = 14179, SummitLat = 41.4099, SummitLon = -122.1949, ClassDifficulty = "3", SnotelStationTriplet = "1067:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "glacier-peak",         Mountain = "Glacier Peak",      RouteName = "Sitkum Glacier",         SummitElevationFt = 10541, SummitLat = 48.1112, SummitLon = -121.1130, ClassDifficulty = "3", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-st-helens",      Mountain = "Mount St. Helens",  RouteName = "Monitor Ridge",          SummitElevationFt = 8366,  SummitLat = 46.1912, SummitLon = -122.1944, ClassDifficulty = "2", SnotelStationTriplet = "999:WA:SNTL" },
    };

    private static IEnumerable<RouteEntity> Sierras(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-whitney",        Mountain = "Mount Whitney",     RouteName = "Mountaineer's Route",    SummitElevationFt = 14505, SummitLat = 36.5786, SummitLon = -118.2920, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-williamson",     Mountain = "Mount Williamson",  RouteName = "West Face",              SummitElevationFt = 14379, SummitLat = 36.6555, SummitLon = -118.3110, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-palisade",       Mountain = "North Palisade",    RouteName = "LeConte Route",          SummitElevationFt = 14248, SummitLat = 37.0944, SummitLon = -118.5145, ClassDifficulty = "4", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sill",           Mountain = "Mount Sill",        RouteName = "Swiss Arête",            SummitElevationFt = 14159, SummitLat = 37.1006, SummitLon = -118.5031, ClassDifficulty = "4", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-russell",        Mountain = "Mount Russell",     RouteName = "East Ridge",             SummitElevationFt = 14094, SummitLat = 36.5867, SummitLon = -118.2750, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-langley",        Mountain = "Mount Langley",     RouteName = "Old Army Pass",          SummitElevationFt = 14032, SummitLat = 36.5239, SummitLon = -118.2392, ClassDifficulty = "2", SnotelStationTriplet = "428:CA:SNTL" },
    };

    private static IEnumerable<RouteEntity> WindRiver(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "gannett-peak",         Mountain = "Gannett Peak",      RouteName = "Gooseneck Glacier",      SummitElevationFt = 13809, SummitLat = 43.1842, SummitLon = -109.6543, ClassDifficulty = "3", SnotelStationTriplet = "1010:WY:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "fremont-peak",         Mountain = "Fremont Peak",      RouteName = "Southwest Slopes",       SummitElevationFt = 13745, SummitLat = 43.1239, SummitLon = -109.6181, ClassDifficulty = "2", SnotelStationTriplet = "367:WY:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-helen",          Mountain = "Mount Helen",       RouteName = "East Ridge",             SummitElevationFt = 13620, SummitLat = 43.1572, SummitLon = -109.6431, ClassDifficulty = "3", SnotelStationTriplet = "1010:WY:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sacagawea",      Mountain = "Mount Sacagawea",   RouteName = "Northeast Ridge",        SummitElevationFt = 13569, SummitLat = 43.1497, SummitLon = -109.6203, ClassDifficulty = "2", SnotelStationTriplet = "1010:WY:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "wind-river-peak",      Mountain = "Wind River Peak",   RouteName = "Northwest Ridge",        SummitElevationFt = 13192, SummitLat = 42.7104, SummitLon = -109.1262, ClassDifficulty = "2", SnotelStationTriplet = "367:WY:SNTL" },
    };

    private static IEnumerable<RouteEntity> Sawtooth(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "thompson-peak",        Mountain = "Thompson Peak",     RouteName = "Southwest Slopes",       SummitElevationFt = 10751, SummitLat = 44.0925, SummitLon = -115.0050, ClassDifficulty = "3", SnotelStationTriplet = "837:ID:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-heyburn",        Mountain = "Mount Heyburn",     RouteName = "East Face Standard",     SummitElevationFt = 10229, SummitLat = 44.0697, SummitLon = -114.9483, ClassDifficulty = "4", SnotelStationTriplet = "837:ID:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-cramer",         Mountain = "Mount Cramer",      RouteName = "Southeast Ridge",        SummitElevationFt = 10716, SummitLat = 44.0411, SummitLon = -114.9347, ClassDifficulty = "3", SnotelStationTriplet = "837:ID:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "williams-peak",        Mountain = "Williams Peak",     RouteName = "South Slopes",           SummitElevationFt = 10635, SummitLat = 44.1283, SummitLon = -114.9961, ClassDifficulty = "3", SnotelStationTriplet = "837:ID:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "snowyside-peak",       Mountain = "Snowyside Peak",    RouteName = "Northeast Ridge",        SummitElevationFt = 10651, SummitLat = 43.9911, SummitLon = -114.8917, ClassDifficulty = "2", SnotelStationTriplet = "837:ID:SNTL" },
    };

    private static IEnumerable<RouteEntity> Wasatch(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-timpanogos",     Mountain = "Mount Timpanogos",  RouteName = "Aspen Grove",            SummitElevationFt = 11752, SummitLat = 40.3908, SummitLon = -111.6453, ClassDifficulty = "2", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-nebo",           Mountain = "Mount Nebo",        RouteName = "North Peak Standard",    SummitElevationFt = 11933, SummitLat = 39.8222, SummitLon = -111.7611, ClassDifficulty = "2", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "lone-peak",            Mountain = "Lone Peak",         RouteName = "NW Couloir",             SummitElevationFt = 11253, SummitLat = 40.5306, SummitLon = -111.7569, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "pfeifferhorn",         Mountain = "Pfeifferhorn",      RouteName = "Northeast Ridge",        SummitElevationFt = 11326, SummitLat = 40.5544, SummitLon = -111.7333, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-olympus",        Mountain = "Mount Olympus",     RouteName = "Standard",               SummitElevationFt = 9026,  SummitLat = 40.6364, SummitLon = -111.7325, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "box-elder-peak",       Mountain = "Box Elder Peak",    RouteName = "South Ridge",            SummitElevationFt = 11101, SummitLat = 40.4878, SummitLon = -111.7239, ClassDifficulty = "2", SnotelStationTriplet = "766:UT:SNTL" },
    };

    // The 58 Colorado 14ers — preserved exactly from the prior seeder, plus RangeId.
    private static IEnumerable<RouteEntity> Colorado14ers(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-elbert",           Mountain = "Mount Elbert",           RouteName = "Northeast Ridge",                SummitElevationFt = 14438, SummitLat = 39.1178, SummitLon = -106.4453, ClassDifficulty = "1", SnotelStationTriplet = "369:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-massive",          Mountain = "Mount Massive",          RouteName = "East Slopes",                    SummitElevationFt = 14428, SummitLat = 39.1873, SummitLon = -106.4756, ClassDifficulty = "2", SnotelStationTriplet = "1101:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-harvard",          Mountain = "Mount Harvard",          RouteName = "South Slopes",                   SummitElevationFt = 14421, SummitLat = 38.9244, SummitLon = -106.3206, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "blanca-peak",            Mountain = "Blanca Peak",            RouteName = "Northwest Ridge",                SummitElevationFt = 14345, SummitLat = 37.5775, SummitLon = -105.4856, ClassDifficulty = "2", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "la-plata-peak",          Mountain = "La Plata Peak",          RouteName = "Northwest Ridge",                SummitElevationFt = 14336, SummitLat = 39.0294, SummitLon = -106.4729, ClassDifficulty = "2", SnotelStationTriplet = "369:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "uncompahgre-peak",       Mountain = "Uncompahgre Peak",       RouteName = "South Ridge",                    SummitElevationFt = 14309, SummitLat = 38.0717, SummitLon = -107.4622, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "crestone-peak",          Mountain = "Crestone Peak",          RouteName = "South Face (Red Gully)",         SummitElevationFt = 14294, SummitLat = 37.9669, SummitLon = -105.5853, ClassDifficulty = "3", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-lincoln",          Mountain = "Mount Lincoln",          RouteName = "West Ridge (DeCaLiBron)",        SummitElevationFt = 14286, SummitLat = 39.3514, SummitLon = -106.1117, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "grays-peak",             Mountain = "Grays Peak",             RouteName = "North Slopes",                   SummitElevationFt = 14270, SummitLat = 39.6339, SummitLon = -105.8175, ClassDifficulty = "1", SnotelStationTriplet = "1187:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-antero",           Mountain = "Mount Antero",           RouteName = "West Slopes (Baldwin Gulch)",    SummitElevationFt = 14269, SummitLat = 38.6741, SummitLon = -106.2461, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "torreys-peak",           Mountain = "Torreys Peak",           RouteName = "South Slopes (via Grays)",       SummitElevationFt = 14267, SummitLat = 39.6428, SummitLon = -105.8211, ClassDifficulty = "2", SnotelStationTriplet = "1187:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "castle-peak",            Mountain = "Castle Peak",            RouteName = "Northeast Ridge",                SummitElevationFt = 14265, SummitLat = 39.0094, SummitLon = -106.8614, ClassDifficulty = "2", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "quandary-peak",          Mountain = "Quandary Peak",          RouteName = "East Ridge",                     SummitElevationFt = 14265, SummitLat = 39.3973, SummitLon = -106.1064, ClassDifficulty = "1", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-evans",            Mountain = "Mount Evans",            RouteName = "Northeast Face",                 SummitElevationFt = 14264, SummitLat = 39.5883, SummitLon = -105.6438, ClassDifficulty = "2", SnotelStationTriplet = "1187:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "longs-peak",             Mountain = "Longs Peak",             RouteName = "Keyhole",                        SummitElevationFt = 14255, SummitLat = 40.2549, SummitLon = -105.6160, ClassDifficulty = "3", SnotelStationTriplet = "1042:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-wilson",           Mountain = "Mount Wilson",           RouteName = "North Slopes",                   SummitElevationFt = 14246, SummitLat = 37.8389, SummitLon = -107.9911, ClassDifficulty = "4", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-cameron",          Mountain = "Mount Cameron",          RouteName = "DeCaLiBron via saddle",          SummitElevationFt = 14238, SummitLat = 39.3464, SummitLon = -106.1186, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-shavano",          Mountain = "Mount Shavano",          RouteName = "East Slopes (Angel of Shavano)", SummitElevationFt = 14229, SummitLat = 38.6192, SummitLon = -106.2253, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-belford",          Mountain = "Mount Belford",          RouteName = "Northwest Ridge",                SummitElevationFt = 14197, SummitLat = 38.9606, SummitLon = -106.3608, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "crestone-needle",        Mountain = "Crestone Needle",        RouteName = "South Face",                     SummitElevationFt = 14197, SummitLat = 37.9647, SummitLon = -105.5764, ClassDifficulty = "3", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-princeton",        Mountain = "Mount Princeton",        RouteName = "East Slopes",                    SummitElevationFt = 14197, SummitLat = 38.7492, SummitLon = -106.2425, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-yale",             Mountain = "Mount Yale",             RouteName = "Southwest Slopes",               SummitElevationFt = 14196, SummitLat = 38.8442, SummitLon = -106.3136, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-bross",            Mountain = "Mount Bross",            RouteName = "West Slopes (DeCaLiBron)",       SummitElevationFt = 14172, SummitLat = 39.3358, SummitLon = -106.1078, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "kit-carson-peak",        Mountain = "Kit Carson Peak",        RouteName = "North Ridge (via Challenger)",   SummitElevationFt = 14165, SummitLat = 37.9794, SummitLon = -105.6028, ClassDifficulty = "3", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "el-diente-peak",         Mountain = "El Diente Peak",         RouteName = "North Slopes",                   SummitElevationFt = 14159, SummitLat = 37.8394, SummitLon = -108.0050, ClassDifficulty = "3", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "maroon-peak",            Mountain = "Maroon Peak",            RouteName = "South Ridge",                    SummitElevationFt = 14156, SummitLat = 39.0708, SummitLon = -106.9889, ClassDifficulty = "4", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "tabeguache-peak",        Mountain = "Tabeguache Peak",        RouteName = "West Ridge (via Shavano)",       SummitElevationFt = 14155, SummitLat = 38.6258, SummitLon = -106.2386, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-oxford",           Mountain = "Mount Oxford",           RouteName = "West Ridge (via Belford)",       SummitElevationFt = 14153, SummitLat = 38.9647, SummitLon = -106.3389, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sneffels",         Mountain = "Mount Sneffels",         RouteName = "Southwest Ridge (Lavender Col)", SummitElevationFt = 14150, SummitLat = 38.0036, SummitLon = -107.7925, ClassDifficulty = "3", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-democrat",         Mountain = "Mount Democrat",         RouteName = "East Slopes (DeCaLiBron)",       SummitElevationFt = 14148, SummitLat = 39.3394, SummitLon = -106.1397, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "capitol-peak",           Mountain = "Capitol Peak",           RouteName = "Northeast Ridge (Knife Edge)",   SummitElevationFt = 14130, SummitLat = 39.1503, SummitLon = -107.0830, ClassDifficulty = "4", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "pikes-peak",             Mountain = "Pikes Peak",             RouteName = "Barr Trail",                     SummitElevationFt = 14110, SummitLat = 38.8409, SummitLon = -105.0442, ClassDifficulty = "1", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "snowmass-mountain",      Mountain = "Snowmass Mountain",      RouteName = "East Slopes",                    SummitElevationFt = 14092, SummitLat = 39.1186, SummitLon = -107.0664, ClassDifficulty = "3", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-eolus",            Mountain = "Mount Eolus",            RouteName = "Northeast Ridge",                SummitElevationFt = 14083, SummitLat = 37.6219, SummitLon = -107.6225, ClassDifficulty = "3", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "windom-peak",            Mountain = "Windom Peak",            RouteName = "West Ridge",                     SummitElevationFt = 14082, SummitLat = 37.6214, SummitLon = -107.5917, ClassDifficulty = "2", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "challenger-point",       Mountain = "Challenger Point",       RouteName = "North Slopes",                   SummitElevationFt = 14081, SummitLat = 37.9803, SummitLon = -105.6064, ClassDifficulty = "2", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-columbia",         Mountain = "Mount Columbia",         RouteName = "West Slopes",                    SummitElevationFt = 14077, SummitLat = 38.9039, SummitLon = -106.2972, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "missouri-mountain",      Mountain = "Missouri Mountain",      RouteName = "Northwest Ridge",                SummitElevationFt = 14074, SummitLat = 38.9478, SummitLon = -106.3789, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "humboldt-peak",          Mountain = "Humboldt Peak",          RouteName = "West Ridge",                     SummitElevationFt = 14070, SummitLat = 37.9764, SummitLon = -105.5550, ClassDifficulty = "2", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-bierstadt",        Mountain = "Mount Bierstadt",        RouteName = "West Slopes",                    SummitElevationFt = 14065, SummitLat = 39.5828, SummitLon = -105.6685, ClassDifficulty = "2", SnotelStationTriplet = "1187:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "conundrum-peak",         Mountain = "Conundrum Peak",         RouteName = "Northeast Ridge (via Castle)",   SummitElevationFt = 14060, SummitLat = 39.0064, SummitLon = -106.8675, ClassDifficulty = "2", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sunlight-peak",          Mountain = "Sunlight Peak",          RouteName = "South Face",                     SummitElevationFt = 14059, SummitLat = 37.6275, SummitLon = -107.5950, ClassDifficulty = "4", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "handies-peak",           Mountain = "Handies Peak",           RouteName = "American Basin",                 SummitElevationFt = 14048, SummitLat = 37.9131, SummitLon = -107.5042, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "culebra-peak",           Mountain = "Culebra Peak",           RouteName = "Northwest Ridge",                SummitElevationFt = 14047, SummitLat = 37.1225, SummitLon = -105.1856, ClassDifficulty = "2", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "ellingwood-point",       Mountain = "Ellingwood Point",       RouteName = "South Face (via Blanca)",        SummitElevationFt = 14042, SummitLat = 37.5822, SummitLon = -105.4925, ClassDifficulty = "2", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-lindsey",          Mountain = "Mount Lindsey",          RouteName = "Northwest Ridge",                SummitElevationFt = 14042, SummitLat = 37.5836, SummitLon = -105.4456, ClassDifficulty = "2", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-eolus",            Mountain = "North Eolus",            RouteName = "Eolus Ridge",                    SummitElevationFt = 14039, SummitLat = 37.6228, SummitLon = -107.6233, ClassDifficulty = "3", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "little-bear-peak",       Mountain = "Little Bear Peak",       RouteName = "West Ridge (Hourglass)",         SummitElevationFt = 14037, SummitLat = 37.5667, SummitLon = -105.4972, ClassDifficulty = "4", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sherman",          Mountain = "Mount Sherman",          RouteName = "Southwest Ridge",                SummitElevationFt = 14036, SummitLat = 39.2253, SummitLon = -106.1697, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "redcloud-peak",          Mountain = "Redcloud Peak",          RouteName = "Northeast Ridge",                SummitElevationFt = 14034, SummitLat = 37.9408, SummitLon = -107.4214, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "pyramid-peak",           Mountain = "Pyramid Peak",           RouteName = "Northeast Ridge",                SummitElevationFt = 14018, SummitLat = 39.0716, SummitLon = -106.9501, ClassDifficulty = "4", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "wilson-peak",            Mountain = "Wilson Peak",            RouteName = "West Ridge",                     SummitElevationFt = 14017, SummitLat = 37.8597, SummitLon = -107.9847, ClassDifficulty = "3", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "wetterhorn-peak",        Mountain = "Wetterhorn Peak",        RouteName = "Southeast Ridge",                SummitElevationFt = 14015, SummitLat = 38.0606, SummitLon = -107.5106, ClassDifficulty = "3", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-maroon-peak",      Mountain = "North Maroon Peak",      RouteName = "Northeast Ridge",                SummitElevationFt = 14014, SummitLat = 39.0758, SummitLon = -106.9883, ClassDifficulty = "4", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "san-luis-peak",          Mountain = "San Luis Peak",          RouteName = "Northeast Ridge",                SummitElevationFt = 14014, SummitLat = 37.9869, SummitLon = -106.9311, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-of-the-holy-cross", Mountain = "Mount of the Holy Cross", RouteName = "North Ridge",                  SummitElevationFt = 14005, SummitLat = 39.4669, SummitLon = -106.4814, ClassDifficulty = "2", SnotelStationTriplet = "1101:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "huron-peak",             Mountain = "Huron Peak",             RouteName = "Northwest Slopes",               SummitElevationFt = 14003, SummitLat = 38.9453, SummitLon = -106.4378, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sunshine-peak",          Mountain = "Sunshine Peak",          RouteName = "North Slopes (via Redcloud)",    SummitElevationFt = 14001, SummitLat = 37.9258, SummitLon = -107.4256, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
    };
}
```

- [ ] **Step 2: Build Data project**

Run: `dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj`
Expected: PASS

---

### Task 12: Add seeding tests

**Files:**
- Create: `backend/RouteWeather.Core.Tests/Data/RangeSeedingTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using Xunit;

namespace RouteWeather.Core.Tests.Data;

public class RangeSeedingTests
{
    [Fact]
    public async Task Seeds_all_six_ranges_and_eighty_seven_routes()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        Assert.Equal(6, await db.Ranges.CountAsync());
        Assert.Equal(87, await db.Routes.CountAsync());

        var coloradoId = await db.Ranges.Where(r => r.Slug == "colorado-14ers").Select(r => r.Id).SingleAsync();
        Assert.Equal(58, await db.Routes.CountAsync(r => r.RangeId == coloradoId));

        Assert.All(await db.Routes.ToListAsync(), r => Assert.NotEqual(0, r.RangeId));
    }

    [Fact]
    public async Task Every_range_has_valid_GeoJSON_polygon_and_hex_color()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var ranges = await db.Ranges.ToListAsync();
        Assert.NotEmpty(ranges);

        foreach (var r in ranges)
        {
            Assert.Matches("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", r.Color);

            using var doc = JsonDocument.Parse(r.PerimeterGeoJson);
            Assert.Equal("Polygon", doc.RootElement.GetProperty("type").GetString());
            var coords = doc.RootElement.GetProperty("coordinates");
            Assert.True(coords.GetArrayLength() >= 1);
            Assert.True(coords[0].GetArrayLength() >= 4); // a closed polygon needs >=4 points
        }
    }

    [Fact]
    public async Task SeedAsync_is_idempotent()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);
        await RouteSeeder.SeedAsync(db);

        Assert.Equal(6, await db.Ranges.CountAsync());
        Assert.Equal(87, await db.Routes.CountAsync());
    }

    private static RouteWeatherContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RouteWeatherContext(opts);
    }
}
```

- [ ] **Step 2: Run tests — expect PASS**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter RangeSeedingTests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add backend/RouteWeather.Data/RouteSeeder.cs backend/RouteWeather.Core.Tests/Data/RangeSeedingTests.cs
git commit -m "feat(data): seed 6 ranges + 29 new peaks (total 87 routes)"
```

---

# Phase 5 — Frontend models + services

---

### Task 13: Add `RangeMeta` model

**Files:**
- Create: `frontend/src/app/models/range.ts`

- [ ] **Step 1: Write the model**

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

If `GeoJSON` is unknown, install types: `npm install --save-dev @types/geojson` (from inside `frontend/`).

- [ ] **Step 2: Build TS**

Run: `cd frontend && npx tsc --noEmit`
Expected: PASS

---

### Task 14: Add `rangeSlug` + `rangeName` to `RouteSummary` and `RouteDetail`

**Files:**
- Modify: `frontend/src/app/models/route-conditions.ts`

- [ ] **Step 1: Add the two fields to both interfaces**

In `RouteSummary` (append after `classDifficulty`):

```ts
rangeSlug: string;
rangeName: string;
```

`RouteDetail extends RouteSummary` so the fields propagate automatically — don't add them twice.

- [ ] **Step 2: Update `RouteCard` spec fixture**

In `frontend/src/app/components/route-card/route-card.spec.ts`, update the inline `summary()` helper to include both fields:

```ts
function summary(slug: string): RouteSummary {
  return {
    slug,
    mountain: 'Test Mountain',
    routeName: 'Standard',
    summitElevationFt: 14000,
    classDifficulty: '3',
    rangeSlug: 'colorado-14ers',
    rangeName: 'Colorado 14ers',
    grade: 'B',
    overallScore: 85,
    drivers: [],
    updatedAt: new Date().toISOString(),
    isStale: false,
    consensus: null,
  };
}
```

- [ ] **Step 3: Find and fix any other inline `RouteSummary` builders**

Run: `cd frontend && grep -rn "RouteSummary" src/app --include="*.spec.ts"`
For each match, add `rangeSlug` and `rangeName` fields to the builder. Per `.claude/rules/testing.md`, TypeScript catches omissions, but inline literals can drift.

- [ ] **Step 4: Run all specs**

Run: `cd frontend && npm test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/models/route-conditions.ts \
        frontend/src/app/models/range.ts \
        frontend/src/app/components/route-card/route-card.spec.ts
git commit -m "feat(frontend): rangeSlug/rangeName on RouteSummary; RangeMeta model"
```

---

### Task 15: Add `RangesService` + spec

**Files:**
- Create: `frontend/src/app/services/ranges-service.ts`
- Create: `frontend/src/app/services/ranges-service.spec.ts`

- [ ] **Step 1: Write failing test**

```ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RangesService } from './ranges-service';
import { RangeMeta } from '../models/range';

describe('RangesService', () => {
  let service: RangesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RangesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('GETs /api/ranges and returns the catalog', () => {
    const expected: RangeMeta[] = [{
      slug: 'cascades',
      name: 'Cascade Range',
      color: '#5fa8d8',
      description: 'PNW volcanoes',
      displayOrder: 1,
      perimeterGeoJson: { type: 'Polygon', coordinates: [[[0, 0], [1, 0], [1, 1], [0, 0]]] },
    }];

    let received: RangeMeta[] | null = null;
    service.list().subscribe(r => received = r);

    const req = httpMock.expectOne('/api/ranges');
    expect(req.request.method).toBe('GET');
    req.flush(expected);

    expect(received).toEqual(expected);
  });
});
```

- [ ] **Step 2: Run test — expect FAIL (`RangesService` not defined)**

Run: `cd frontend && npm test -- ranges-service`
Expected: FAIL

- [ ] **Step 3: Implement the service**

```ts
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RangeMeta } from '../models/range';

@Injectable({ providedIn: 'root' })
export class RangesService {
  private http = inject(HttpClient);

  list(): Observable<RangeMeta[]> {
    return this.http.get<RangeMeta[]>('/api/ranges');
  }
}
```

- [ ] **Step 4: Re-run test — expect PASS**

Run: `cd frontend && npm test -- ranges-service`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/services/ranges-service.ts \
        frontend/src/app/services/ranges-service.spec.ts
git commit -m "feat(frontend): RangesService for GET /api/ranges"
```

---

# Phase 6 — Existing component updates

---

### Task 16: Add range chip to `RouteCard`

**Files:**
- Modify: `frontend/src/app/components/route-card/route-card.html`
- Modify: `frontend/src/app/components/route-card/route-card.scss`
- Modify: `frontend/src/app/components/route-card/route-card.spec.ts`

- [ ] **Step 1: Add a failing assertion to the spec**

Append to `route-card.spec.ts`:

```ts
it('shows the range name as a chip', () => {
  const fixture = TestBed.createComponent(RouteCard);
  fixture.componentRef.setInput('route', { ...summary('foo'), rangeName: 'Cascades' });
  fixture.detectChanges();

  const chip = (fixture.nativeElement as HTMLElement).querySelector('.range-chip');
  expect(chip?.textContent?.trim()).toBe('Cascades');
});
```

- [ ] **Step 2: Run spec — expect FAIL (no `.range-chip` in DOM)**

Run: `cd frontend && npm test -- route-card`
Expected: FAIL

- [ ] **Step 3: Add the chip to the template**

In `route-card.html`, locate where `classDifficulty` is rendered (it's currently rendered alongside elevation). Add adjacent:

```html
@if (route().rangeName) {
  <span class="range-chip">{{ route().rangeName }}</span>
}
```

- [ ] **Step 4: Add minimal SCSS**

Append to `route-card.scss`:

```scss
.range-chip {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.7rem;
  background: rgba(95, 168, 216, 0.18);
  color: #cfe5f5;
  letter-spacing: 0.04em;
}
```

- [ ] **Step 5: Run spec — expect PASS**

Run: `cd frontend && npm test -- route-card`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/components/route-card/
git commit -m "feat(frontend): range chip on RouteCard"
```

---

### Task 17: Add range chip to `PeakDetail`

**Files:**
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.html`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`

- [ ] **Step 1: Add a failing assertion**

Append to `peak-detail.spec.ts`:

```ts
it('shows the range name on the detail header', () => {
  // (Within the existing test harness setup for PeakDetail — adapt to existing patterns.)
  // After loading detail with rangeName='Wind River Range', expect:
  //   const el = fixture.nativeElement.querySelector('.range-chip');
  //   expect(el?.textContent?.trim()).toBe('Wind River Range');
});
```

Use whatever HTTP-mocked detail-load pattern the existing spec uses; fold the chip assertion into a new test or extend an existing one.

- [ ] **Step 2: Run spec — expect FAIL**

Run: `cd frontend && npm test -- peak-detail`
Expected: FAIL

- [ ] **Step 3: Add chip to template**

In `peak-detail.html`, near the existing class-difficulty rendering:

```html
@if (detail()?.rangeName) {
  <span class="range-chip">{{ detail()!.rangeName }}</span>
}
```

Reuse the `.range-chip` SCSS by importing it from a shared style module, OR copy the same SCSS rule into `peak-detail.scss`. The existing project has not consolidated chips into a shared style file; keep things consistent and copy the rule.

- [ ] **Step 4: Run spec — expect PASS**

Run: `cd frontend && npm test -- peak-detail`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/pages/peak-detail/
git commit -m "feat(frontend): range chip on PeakDetail"
```

---

### Task 18: Group `RouteGrid` (now on `/all`) by range

**Files:**
- Modify: `frontend/src/app/components/route-grid/route-grid.ts`
- Modify: `frontend/src/app/components/route-grid/route-grid.html`
- Modify: `frontend/src/app/components/route-grid/route-grid.scss`
- Modify: `frontend/src/app/components/route-grid/route-grid.spec.ts`

- [ ] **Step 1: Add failing spec — grouped output**

Append:

```ts
it('groups peaks by rangeName, in the order they first appear', () => {
  // Mock the routes list so the API returns peaks across two ranges.
  // Assert that the rendered DOM contains a section with header 'Cascades' before 'Colorado 14ers',
  // and that each section's <app-route-card> elements correspond to that group's peaks.
});
```

Implement against the existing HTTP-mocked load pattern in the spec.

- [ ] **Step 2: Run spec — expect FAIL**

Run: `cd frontend && npm test -- route-grid`
Expected: FAIL

- [ ] **Step 3: Compute groups in the component**

In `route-grid.ts`, replace the `filtered` computed and add:

```ts
interface RangeGroup {
  slug: string;
  name: string;
  routes: RouteSummary[];
}

groups = computed<RangeGroup[]>(() => {
  const q = this.query().trim().toLowerCase();
  const filtered = q
    ? this.routes().filter(r => r.mountain.toLowerCase().includes(q))
    : this.routes();

  const bySlug = new Map<string, RangeGroup>();
  for (const r of filtered) {
    const key = r.rangeSlug || '';
    let g = bySlug.get(key);
    if (!g) {
      g = { slug: key, name: r.rangeName || 'Other', routes: [] };
      bySlug.set(key, g);
    }
    g.routes.push(r);
  }
  return Array.from(bySlug.values());
});
```

Drop the old `filtered` computed.

- [ ] **Step 4: Update the template to render grouped sections**

In `route-grid.html`, replace the `.grid` block with:

```html
@for (g of groups(); track g.slug) {
  <details class="range-group" [attr.data-range]="g.slug" [open]="g.slug === 'colorado-14ers'">
    <summary class="range-group-header">
      <span class="range-group-name">{{ g.name }}</span>
      <span class="range-group-count">{{ g.routes.length }}</span>
    </summary>
    <div class="grid">
      @for (r of g.routes; track r.slug) {
        <app-route-card [route]="r" />
      }
    </div>
  </details>
} @empty {
  <p class="status">No peaks match "{{ query().trim() }}".</p>
}
```

- [ ] **Step 5: Add SCSS for the group**

Append to `route-grid.scss`:

```scss
.range-group {
  margin-bottom: 16px;
}
.range-group-header {
  cursor: pointer;
  padding: 12px 16px;
  font-size: 1.05rem;
  font-weight: 600;
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}
.range-group-count {
  font-size: 0.85rem;
  color: #9fb5d5;
}
```

- [ ] **Step 6: Run spec — expect PASS**

Run: `cd frontend && npm test -- route-grid`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add frontend/src/app/components/route-grid/
git commit -m "feat(frontend): group /all peaks by range with collapsible sections"
```

---

# Phase 7 — Routing + new MapHome

---

### Task 19: Add Leaflet dependencies + global CSS

**Files:**
- Modify: `frontend/package.json` (via npm install)
- Modify: `frontend/src/styles.scss`

- [ ] **Step 1: Install dependencies**

```bash
cd frontend
npm install leaflet leaflet.markercluster
npm install --save-dev @types/leaflet @types/leaflet.markercluster
```

- [ ] **Step 2: Import Leaflet CSS globally**

At the top of `frontend/src/styles.scss`, add:

```scss
@import 'leaflet/dist/leaflet.css';
@import 'leaflet.markercluster/dist/MarkerCluster.css';
@import 'leaflet.markercluster/dist/MarkerCluster.Default.css';
```

(If Angular's build complains about CSS imports from SCSS, an alternative is to add them to `frontend/angular.json` under `architect.build.options.styles`. Try the SCSS path first.)

- [ ] **Step 3: Build to confirm CSS resolves**

Run: `cd frontend && npm run build`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/src/styles.scss
git commit -m "build(frontend): add leaflet + markercluster deps"
```

---

### Task 20: Add `/all` route and update `MapHome` placeholder

**Files:**
- Modify: `frontend/src/app/app.routes.ts`
- Modify: `frontend/src/app/app.html`
- Create: `frontend/src/app/pages/map-home/map-home.ts` (initial empty shell)
- Create: `frontend/src/app/pages/map-home/map-home.html`
- Create: `frontend/src/app/pages/map-home/map-home.scss`

- [ ] **Step 1: Create the MapHome component shell**

`map-home.ts`:

```ts
import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-map-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map-home.html',
  styleUrl: './map-home.scss',
})
export class MapHome {}
```

`map-home.html`:

```html
<section class="map-home">
  <div id="map" class="map-container"></div>
</section>
```

`map-home.scss`:

```scss
.map-home {
  position: relative;
  min-height: calc(100vh - 120px);
}
.map-container {
  width: 100%;
  height: calc(100vh - 120px);
  background: #0a1525;
  border-radius: 8px;
}
```

- [ ] **Step 2: Wire routes**

Replace `app.routes.ts`:

```ts
import { Routes } from '@angular/router';
import { MapHome } from './pages/map-home/map-home';
import { RouteGrid } from './components/route-grid/route-grid';
import { PeakDetail } from './pages/peak-detail/peak-detail';
import { About } from './pages/about/about';

export const routes: Routes = [
  { path: '', component: MapHome },
  { path: 'all', component: RouteGrid },
  { path: 'peak/:slug', component: PeakDetail },
  { path: 'about', component: About },
  { path: '**', redirectTo: '' },
];
```

- [ ] **Step 3: Add `/all` link to the header**

In `app.html`, change the nav block to:

```html
<nav class="hero-nav">
  <a routerLink="/all" class="about-link">All peaks</a>
  <a routerLink="/about" class="about-link">About</a>
</nav>
```

- [ ] **Step 4: Run frontend build + check `/` renders the new placeholder**

Run: `cd frontend && npm run build`
Expected: PASS

Manual: load `http://localhost:4200/`, confirm the map container area renders (empty for now), `/all` shows the route grid, `/peak/longs-peak` still works.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/app.routes.ts frontend/src/app/app.html frontend/src/app/pages/map-home/
git commit -m "feat(frontend): MapHome shell on /, RouteGrid moved to /all"
```

---

### Task 21: Implement `MapHome` — data loading + Leaflet init

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts`
- Create: `frontend/src/app/pages/map-home/map-home.spec.ts`

- [ ] **Step 1: Write failing data-loading test**

```ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MapHome } from './map-home';

describe('MapHome', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MapHome],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests routes and ranges on init', () => {
    TestBed.createComponent(MapHome).detectChanges();
    httpMock.expectOne('/api/routes').flush([]);
    httpMock.expectOne('/api/ranges').flush([]);
  });

  it('exposes an error signal when either request fails', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();
    httpMock.expectOne('/api/routes').error(new ProgressEvent('boom'));
    httpMock.expectOne('/api/ranges').flush([]);
    expect(fixture.componentInstance.error()).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run spec — expect FAIL**

Run: `cd frontend && npm test -- map-home`
Expected: FAIL

- [ ] **Step 3: Implement MapHome with data loading**

Replace `map-home.ts`:

```ts
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { forkJoin } from 'rxjs';
import { Router } from '@angular/router';
import { RoutesService } from '../../services/routes-service';
import { RangesService } from '../../services/ranges-service';
import { RouteSummary } from '../../models/route-conditions';
import { RangeMeta } from '../../models/range';

@Component({
  selector: 'app-map-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map-home.html',
  styleUrl: './map-home.scss',
})
export class MapHome implements OnDestroy {
  private routesSvc = inject(RoutesService);
  private rangesSvc = inject(RangesService);
  private router = inject(Router);

  mapContainer = viewChild<ElementRef<HTMLDivElement>>('mapEl');

  routes = signal<RouteSummary[]>([]);
  ranges = signal<RangeMeta[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  lastFetchedAt = signal<number | null>(null);

  lastUpdatedLabel = computed(() => {
    const t = this.lastFetchedAt();
    if (t === null) return null;
    const diffMin = Math.max(0, Math.round((Date.now() - t) / 60000));
    if (diffMin < 1) return 'just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    return `${Math.round(diffMin / 60)}h ago`;
  });

  private map: any | null = null;
  private layers: any[] = [];

  constructor() {
    forkJoin([this.routesSvc.list(), this.rangesSvc.list()]).subscribe({
      next: ([routes, ranges]) => {
        this.routes.set(routes);
        this.ranges.set(ranges);
        this.lastFetchedAt.set(Date.now());
        this.loading.set(false);
      },
      error: e => {
        this.error.set(e?.message ?? 'Could not load conditions');
        this.loading.set(false);
      },
    });

    afterNextRender(() => this.initMap());
  }

  ngOnDestroy() {
    if (this.map) { this.map.remove(); this.map = null; }
  }

  private async initMap() {
    const el = this.mapContainer()?.nativeElement;
    if (!el) return;

    const L = await import('leaflet');
    await import('leaflet.markercluster');

    this.map = L.map(el, {
      center: [41.5, -113],
      zoom: 5,
      minZoom: 4,
      maxZoom: 12,
      maxBounds: [[28, -130], [52, -100]],
      scrollWheelZoom: true,
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 12,
    }).addTo(this.map);
  }
}
```

Update `map-home.html` to expose a viewChild ref:

```html
<section class="map-home">
  @if (lastUpdatedLabel(); as label) {
    <div class="updated-chip">Updated {{ label }}</div>
  }
  <div #mapEl class="map-container"></div>
  @if (error()) {
    <p class="status error">{{ error() }}</p>
  }
</section>
```

Update SCSS:

```scss
.map-home {
  position: relative;
  min-height: calc(100vh - 120px);
}
.map-container {
  width: 100%;
  height: calc(100vh - 120px);
  background: #0a1525;
  border-radius: 8px;
}
.updated-chip {
  position: absolute;
  top: 12px;
  right: 12px;
  z-index: 1000;
  background: rgba(10, 21, 37, 0.85);
  color: #cfd9e8;
  padding: 6px 10px;
  border-radius: 6px;
  font-size: 0.85rem;
}
```

- [ ] **Step 4: Run spec — expect PASS**

Run: `cd frontend && npm test -- map-home`
Expected: PASS

(Leaflet itself isn't exercised by the spec — `afterNextRender` doesn't fire in test env without explicit triggering. The data-loading code path is what we verify here.)

- [ ] **Step 5: Manual smoke test**

Manual: navigate to `http://localhost:4200/`. Expect a dark map of the western US to render with Carto Dark Matter tiles. No markers or polygons yet — those come next.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/pages/map-home/
git commit -m "feat(frontend): MapHome data loading + Leaflet base map"
```

---

### Task 22: MapHome — render range polygons + labels

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts`

- [ ] **Step 1: Add `renderLayers` and call it when both data arrays are present**

In `map-home.ts`, add a private method:

```ts
private async renderLayers() {
  if (!this.map || this.ranges().length === 0) return;
  const L = await import('leaflet');

  for (const layer of this.layers) this.map.removeLayer(layer);
  this.layers = [];

  for (const range of this.ranges()) {
    const poly = L.geoJSON(range.perimeterGeoJson as any, {
      style: {
        color: range.color,
        weight: 1.5,
        dashArray: '4,3',
        fillColor: range.color,
        fillOpacity: 0.22,
        interactive: false,
      },
    });
    poly.addTo(this.map);
    this.layers.push(poly);

    // Centroid label
    const centroid = polygonCentroid(range.perimeterGeoJson.coordinates[0] as number[][]);
    const label = L.marker([centroid[1], centroid[0]], {
      icon: L.divIcon({
        className: 'range-label',
        html: `<span>${range.name.toUpperCase()}</span>`,
      }),
      interactive: false,
    }).addTo(this.map);
    this.layers.push(label);
  }
}

// Polygon centroid via signed-area formula.
function polygonCentroid(ring: number[][]): [number, number] {
  let twiceArea = 0, cx = 0, cy = 0;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const [x0, y0] = ring[j];
    const [x1, y1] = ring[i];
    const f = x0 * y1 - x1 * y0;
    twiceArea += f;
    cx += (x0 + x1) * f;
    cy += (y0 + y1) * f;
  }
  const area = twiceArea / 2;
  return area === 0 ? ring[0] as [number, number] : [cx / (6 * area), cy / (6 * area)];
}
```

Move `polygonCentroid` to module scope (outside the class).

Update the data-load subscribe to call `renderLayers()`:

```ts
next: ([routes, ranges]) => {
  this.routes.set(routes);
  this.ranges.set(ranges);
  this.lastFetchedAt.set(Date.now());
  this.loading.set(false);
  this.renderLayers();
},
```

And at the end of `initMap()`:

```ts
this.renderLayers();
```

(So polygons render whether the map or the data finishes first.)

- [ ] **Step 2: Add label SCSS**

Append to global `styles.scss` (Leaflet's divIcons need global CSS because they live outside Angular's view encapsulation):

```scss
.range-label {
  background: transparent;
  border: none;
  pointer-events: none;
  font-family: system-ui, sans-serif;
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.15em;
  color: rgba(207, 229, 245, 0.75);
  text-shadow: 0 1px 2px rgba(0,0,0,0.6);
  white-space: nowrap;
}
```

- [ ] **Step 3: Manual smoke test**

Manual: reload `/`. Expect 6 tinted range polygons with bold uppercase labels in their interior.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/app/pages/map-home/map-home.ts frontend/src/styles.scss
git commit -m "feat(frontend): render range polygons + labels on MapHome"
```

---

### Task 23: MapHome — render peak markers with clustering for CO 14ers

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts`
- Modify: `frontend/src/styles.scss`

- [ ] **Step 1: Extend `renderLayers` to add markers**

Append to the loop body after the label marker, or add a new `renderMarkers` method called after `renderLayers`:

```ts
private async renderMarkers() {
  if (!this.map || this.routes().length === 0) return;
  const L = await import('leaflet') as any;

  const coCluster = L.markerClusterGroup({
    disableClusteringAtZoom: 8,
    showCoverageOnHover: false,
    spiderfyOnMaxZoom: true,
    maxClusterRadius: 60,
  });

  for (const route of this.routes()) {
    if (route.grade === null) continue; // skip if we have no grade yet
    const detail = (route as any).summitLat
      ? [(route as any).summitLat, (route as any).summitLon]
      : null;
    // RouteSummary doesn't include summitLat — we need a coordinate source.
    // PLAN ADJUSTMENT: see Step 2 below for adding lat/lon to summary.
  }
}
```

**Plan adjustment:** `RouteSummary` doesn't carry `summitLat`/`summitLon`. We need them for marker placement. Two options: add them to the summary DTO (small, no downside since each route is already serialized once per fetch), or add a second `/api/routes/coordinates` endpoint.

**Decision:** add `summitLat` + `summitLon` to `RouteSummary` (and to the backend `ToSummary`). One-line change each side; cleaner than a second endpoint.

- [ ] **Step 2: Add summitLat/summitLon to backend `ToSummary`**

In `backend/RouteWeather.API/Controllers/RoutesController.cs`, inside `ToSummary`:

```csharp
summitLat = c.Route.SummitLat,
summitLon = c.Route.SummitLon,
```

- [ ] **Step 3: Add fields to frontend `RouteSummary`**

In `frontend/src/app/models/route-conditions.ts`, append to `RouteSummary`:

```ts
summitLat: number;
summitLon: number;
```

Update every inline `RouteSummary` builder in specs to set these fields (use `summitLat: 39.0, summitLon: -106.0` as plausible defaults). Run `grep -rn "RouteSummary" frontend/src --include="*.spec.ts"` and patch each.

- [ ] **Step 4: Now finish `renderMarkers`**

```ts
private async renderMarkers() {
  if (!this.map || this.routes().length === 0) return;
  const L = await import('leaflet') as any;

  const cluster = L.markerClusterGroup({
    disableClusteringAtZoom: 8,
    showCoverageOnHover: false,
    spiderfyOnMaxZoom: true,
    maxClusterRadius: 60,
  });
  let usedCluster = false;

  for (const route of this.routes()) {
    if (!route.summitLat || !route.summitLon) continue;

    const icon = L.divIcon({
      className: 'peak-marker',
      html: `<span class="dot grade-${(route.grade ?? 'x').toLowerCase()}"></span>`,
      iconSize: [28, 28],
      iconAnchor: [14, 14],
    });

    const marker = L.marker([route.summitLat, route.summitLon], { icon, title: route.mountain });
    marker.bindPopup(this.popupHtml(route), { className: 'peak-popup' });

    if (route.rangeSlug === 'colorado-14ers') {
      cluster.addLayer(marker);
      usedCluster = true;
    } else {
      marker.addTo(this.map);
      this.layers.push(marker);
    }
  }

  if (usedCluster) {
    cluster.addTo(this.map);
    this.layers.push(cluster);
  }
}

private popupHtml(route: RouteSummary): string {
  const grade = route.grade ?? '?';
  const drivers = (route.drivers ?? []).slice(0, 2)
    .map(d => `<div class="popup-driver popup-driver-${d.severity}">${escapeHtml(d.label)}</div>`)
    .join('');
  return `
    <div class="popup-name">${escapeHtml(route.mountain)}</div>
    <div class="popup-sub">${route.summitElevationFt.toLocaleString()} ft · Class ${escapeHtml(route.classDifficulty)}</div>
    <div class="popup-grade grade-${grade.toLowerCase()}">${grade}</div>
    ${drivers}
    <a class="popup-cta" data-peak="${escapeHtml(route.slug)}" href="/peak/${escapeHtml(route.slug)}">View full forecast →</a>
  `;
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]!));
}
```

Move `escapeHtml` to module scope.

Update the data-load callback to also call `renderMarkers()` after `renderLayers()`.

- [ ] **Step 5: Add marker + popup CSS to `styles.scss`**

```scss
.peak-marker .dot {
  display: block;
  margin: 7px;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  border: 2px solid #ffffff;
  box-shadow: 0 0 0 1px rgba(0,0,0,0.4);
}
.peak-marker .dot.grade-a { background: #3ecf78; }
.peak-marker .dot.grade-b { background: #a8d050; }
.peak-marker .dot.grade-c { background: #e8c850; }
.peak-marker .dot.grade-d { background: #e88848; }
.peak-marker .dot.grade-f { background: #d04848; }
.peak-marker .dot.grade-x { background: #6f7a8e; }

.peak-popup .popup-name { font-weight: 700; font-size: 1.05rem; color: #f5f8fb; margin-bottom: 2px; }
.peak-popup .popup-sub  { font-size: 0.8rem;  color: #9fb5d5; margin-bottom: 8px; }
.peak-popup .popup-grade {
  display: inline-flex; align-items: center; justify-content: center;
  width: 36px; height: 36px; border-radius: 6px;
  font-weight: 800; font-size: 1.4rem; color: #0a1525; margin-bottom: 6px;
}
.peak-popup .popup-grade.grade-a { background: #3ecf78; }
.peak-popup .popup-grade.grade-b { background: #a8d050; }
.peak-popup .popup-grade.grade-c { background: #e8c850; }
.peak-popup .popup-grade.grade-d { background: #e88848; }
.peak-popup .popup-grade.grade-f { background: #d04848; }
.peak-popup .popup-driver { font-size: 0.8rem; padding: 2px 0; }
.peak-popup .popup-driver-positive { color: #3ecf78; }
.peak-popup .popup-driver-negative { color: #e88848; }
.peak-popup .popup-driver-neutral { color: #9fb5d5; }
.peak-popup .popup-cta {
  display: block; margin-top: 8px; padding: 6px 10px; border-radius: 5px;
  background: #3a5a8a; color: #fff; text-align: center; text-decoration: none; font-weight: 600;
}
.leaflet-popup-content-wrapper { background: #0a1525; color: #cfd9e8; }
.leaflet-popup-tip { background: #0a1525; }
```

- [ ] **Step 6: Manual smoke test**

Manual: reload `/`. Expect color-coded dots across all 6 ranges. Click a Cascades dot — popup with grade + drivers + CTA. Zoom out: CO peaks collapse into a cluster bubble. Zoom past level 8: cluster splits.

- [ ] **Step 7: Commit**

```bash
git add backend/RouteWeather.API/Controllers/RoutesController.cs \
        frontend/src/app/models/route-conditions.ts \
        frontend/src/app/pages/map-home/map-home.ts \
        frontend/src/styles.scss \
        frontend/src/app/components/route-card/route-card.spec.ts
git commit -m "feat(frontend): peak markers with grade-colored dots; cluster CO 14ers"
```

---

### Task 24: MapHome — SPA-routed popup CTA

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts`

- [ ] **Step 1: Add delegated click handler in `initMap`**

After the `tileLayer(...).addTo(this.map);` line in `initMap()`:

```ts
this.map.getContainer().addEventListener('click', (e: MouseEvent) => {
  const target = e.target as HTMLElement;
  const cta = target.closest('a.popup-cta[data-peak]') as HTMLAnchorElement | null;
  if (!cta) return;
  // Let middle-click and modified clicks behave as native anchors.
  if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  e.preventDefault();
  const slug = cta.getAttribute('data-peak')!;
  this.map.closePopup();
  this.router.navigate(['/peak', slug]);
});
```

- [ ] **Step 2: Manual smoke test**

Manual: click a popup CTA. Expect SPA navigation to `/peak/<slug>` — no full reload. Middle-click should open in a new tab.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/app/pages/map-home/map-home.ts
git commit -m "feat(frontend): SPA-route popup CTA via delegated click handler"
```

---

### Task 25: MapHome — search overlay

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts`
- Modify: `frontend/src/app/pages/map-home/map-home.html`
- Modify: `frontend/src/app/pages/map-home/map-home.scss`

- [ ] **Step 1: Add search signal + filtered routes computed**

In `map-home.ts`:

```ts
searchQuery = signal('');

searchResults = computed(() => {
  const q = this.searchQuery().trim().toLowerCase();
  if (q.length < 2) return [];
  return this.routes()
    .filter(r => r.mountain.toLowerCase().includes(q))
    .slice(0, 8);
});

onSearch(value: string) { this.searchQuery.set(value); }

private markerBySlug = new Map<string, any>();
```

In `renderMarkers`, after creating each marker:

```ts
this.markerBySlug.set(route.slug, marker);
```

And add:

```ts
focusPeak(slug: string) {
  const m = this.markerBySlug.get(slug);
  if (!m || !this.map) return;
  this.map.setView(m.getLatLng(), Math.max(this.map.getZoom(), 8), { animate: true });
  m.openPopup();
  this.searchQuery.set('');
}
```

- [ ] **Step 2: Template — search box + result list**

In `map-home.html`, add inside the `.map-home` section before the map div:

```html
<div class="search-overlay">
  <input
    type="search"
    class="search-input"
    placeholder="Find a peak..."
    [value]="searchQuery()"
    (input)="onSearch($any($event.target).value)"
    aria-label="Search peaks"
  />
  @if (searchResults().length > 0) {
    <ul class="search-results">
      @for (r of searchResults(); track r.slug) {
        <li>
          <button type="button" (click)="focusPeak(r.slug)">
            <span class="result-name">{{ r.mountain }}</span>
            <span class="result-range">{{ r.rangeName }}</span>
          </button>
        </li>
      }
    </ul>
  }
</div>
```

- [ ] **Step 3: SCSS**

Append to `map-home.scss`:

```scss
.search-overlay {
  position: absolute;
  top: 12px;
  left: 12px;
  z-index: 1000;
  background: rgba(10, 21, 37, 0.85);
  border-radius: 6px;
  padding: 6px;
  min-width: 220px;
}
.search-input {
  width: 100%;
  background: transparent;
  border: 1px solid rgba(95, 168, 216, 0.4);
  border-radius: 4px;
  padding: 6px 8px;
  color: #cfd9e8;
}
.search-results {
  list-style: none;
  padding: 4px 0 0;
  margin: 0;
  max-height: 220px;
  overflow: auto;
}
.search-results button {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  width: 100%;
  background: transparent;
  border: none;
  padding: 6px 8px;
  cursor: pointer;
  color: #cfd9e8;
  text-align: left;

  &:hover { background: rgba(95, 168, 216, 0.15); }
}
.result-name { font-weight: 600; }
.result-range { font-size: 0.75rem; color: #9fb5d5; }

@media (max-width: 480px) {
  .search-overlay {
    min-width: 0;
    padding: 4px;
  }
  .search-input { font-size: 0.85rem; }
}
```

- [ ] **Step 4: Manual smoke test**

Manual: type "rainier" — see one result; click — map pans and opens Rainier's popup.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/pages/map-home/
git commit -m "feat(frontend): search overlay on MapHome"
```

---

# Phase 8 — Final verification

---

### Task 26: Manual verification walkthrough

Per `feedback_plan_question_interactions.md` and the spec's "Manual verification before merging" section, walk the full interaction surface end-to-end. Don't skip — the user explicitly called out that hidden contradictions ambush manual testing.

- [ ] **Step 1: Hard-reload `/`**

Verify: map renders with Carto Dark Matter tiles, 6 tinted range polygons with uppercase labels, color-coded peak dots across all ranges.

- [ ] **Step 2: Zoom interaction**

At zoom 4–7: Colorado 14ers collapse into a single cluster bubble (e.g., "58"). All other ranges' dots remain individual.
At zoom 8+: cluster splits into individual CO dots.

- [ ] **Step 3: Popup**

Click any peak dot. Popup opens with: mountain name, elevation + class, grade letter in colored badge, top 2 drivers, "View full forecast →" CTA. Click CTA — SPA-navigate to `/peak/<slug>` (no full reload). Middle-click CTA — opens in new tab.

- [ ] **Step 4: Search overlay**

Type "rainier" — autocomplete shows 1 result. Click — map pans to Rainier, popup opens.
Type something matching nothing — no results list shown.

- [ ] **Step 5: `/all`**

Visit `/all`. Expect: 6 collapsible `<details>` sections (CO 14ers expanded by default, others collapsed). Counts in each header match expected (CO=58, others 5–7). Each section's cards render with range chip.

- [ ] **Step 6: Search on `/all`**

Type "whitney" in `/all` search. Only the Sierra Nevada section shows (with a single match). Other sections collapse to zero (or hide entirely if empty).

- [ ] **Step 7: Peak detail**

Visit `/peak/mount-rainier`. Page renders with range chip ("Cascade Range") visible. No refresh button anywhere on the page.

- [ ] **Step 8: Refresh endpoint deleted**

Run: `curl -i http://localhost:5150/api/routes/refresh`
Expected: HTTP 404.

- [ ] **Step 9: Mobile**

Open browser devtools, set viewport to 390×844 (iPhone). Visit `/`. Map readable. Popover legible. Tap a dot — popup opens, doesn't overflow viewport. Search overlay still usable.

- [ ] **Step 10: API responses**

Run: `curl http://localhost:5150/api/ranges | jq 'length'`
Expected: `6`.

Run: `curl http://localhost:5150/api/routes | jq 'length'`
Expected: `87`.

Run: `curl http://localhost:5150/api/routes | jq '.[0].rangeSlug'`
Expected: a string, e.g. `"cascades"` (depending on alphabetical order by Mountain — `Box Elder Peak` would be first in the seeder sort).

- [ ] **Step 11: Open the PR**

```bash
git push -u origin feature/multi-range-map-spec
gh pr create --base dev --title "feat: multi-range map homepage with 5 new ranges" --body "$(cat <<'EOF'
## Summary
- Adds 5 Western US ranges (Cascades, Sierra Nevada, Wind River, Sawtooth, Wasatch) with 29 curated peaks
- Replaces homepage with interactive Leaflet map (Carto Dark Matter tiles), range perimeter polygons, color-coded peak markers, popups with grade + drivers + CTA
- Moves the flat grid to /all with collapsible range groupings
- Removes user-facing refresh affordances (button + endpoints) — server-side cache is the only refresh mechanism

## Test plan
- [ ] /  — map loads with 6 ranges, ~87 peak dots, CO 14ers cluster
- [ ] Click dot → popup → "View full forecast" → /peak/:slug
- [ ] Search overlay finds peaks across ranges
- [ ] /all groups peaks by range, CO 14ers expanded
- [ ] /peak/:slug shows range chip; no refresh button
- [ ] /api/ranges returns 6, /api/routes returns 87, /api/routes/refresh → 404

Spec: docs/superpowers/specs/2026-06-08-multi-range-map-design.md
EOF
)"
```

- [ ] **Step 12: After merge — update memory**

Two memory entries need updating after merge. Add them to `wrap-up` notes:
- `project_two_tier_cache_architecture.md` — strike the "/refresh bypasses tiers 1+2 only" sentence; the 3-tier cache stays but nothing bypasses it now.
- Add a new memory entry capturing: range data model (DB-stored GeoJSON in `Ranges.PerimeterGeoJson`), Leaflet + Carto Dark Matter tile choice, popup-with-CTA pattern with delegated click handler for SPA routing.

---

## Self-review notes

**Spec coverage:**
- ✅ Range entity + FK on Route (Tasks 4, 5)
- ✅ Hand-authored migration trio (Task 6)
- ✅ 29 new peaks + retag of 58 14ers (Task 11)
- ✅ GET /api/ranges (Task 9)
- ✅ rangeSlug/rangeName on DTOs (Task 10; summitLat/Lon added in Task 23 step 2)
- ✅ Refresh endpoints removed (Task 10)
- ✅ Refresh UI affordances removed (Tasks 1, 2, 3)
- ✅ MapHome on `/`, RouteGrid on `/all` (Task 20)
- ✅ Carto Dark Matter + Leaflet (Task 21)
- ✅ Range polygons + labels (Task 22)
- ✅ Peak markers + CO 14er clustering (Task 23)
- ✅ Popup + SPA-routed CTA (Tasks 23, 24)
- ✅ Search overlay (Task 25)
- ✅ Range chip on RouteCard + PeakDetail (Tasks 16, 17)
- ✅ /all grouped by range (Task 18)
- ✅ Tests (Tasks 7, 12, 15, 16, 17, 18, 21)
- ✅ Manual verification (Task 26)

**Plan adjustment recorded:** Task 23 step 2 adds `summitLat`/`summitLon` to `RouteSummary` (and the backend DTO). The spec did not explicitly include these in the summary DTO; this is needed for marker placement. Added to Step 3 of Task 14 as a follow-up via cross-spec amendment. Justification: cleaner than introducing a second coordinates endpoint, and the data is already loaded server-side.
