# Cold-Start Warm Loop + Serve-Stale Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A cold-start visitor sees graded (stale-marked) markers in ~2–3s instead of 60–90s, by moving all upstream weather fetches into a background warmer loop and serving last-known data (≤24h) to user requests.

**Architecture:** Invert the fetch path: user GETs become pure reads (memory cache → SQLite last-known rows), a new `ConditionsWarmerService` BackgroundService re-aggregates all routes every 10 minutes, and `fly.toml` keeps one machine always on. Frontend silently refetches while responses are stale and renders null-grade routes as ghost markers.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core + SQLite (`IDbContextFactory`), xUnit + EF InMemory, Angular 21 zoneless + Vitest/jsdom, Fly.io.

**Spec:** `docs/superpowers/specs/2026-06-09-cold-start-performance-design.md` (approved)

---

## Environment ground rules (read first)

- **Never run `dotnet run` or `npm start`.** The user keeps both servers in their own terminals.
- **Backend builds/tests fail with file-lock errors if the local API is running** (it locks the built DLLs). Before the first `dotnet build`/`dotnet test` step, ask the user to stop the API. Do not kill the process yourself.
- Frontend tests: just `npm test` from `frontend/` (Vitest runs once and exits). Do NOT pass `--watch=false` or `--browsers=...` — those are Karma flags and will error.
- Shell is PowerShell. For multiline commit messages use a single-quoted here-string (`@'...'@` with the closing `'@` at column 0).
- Commit with selective `git add` (never `git add -A`; the repo's gitignore does not exclude local plan files).
- All paths below are relative to the repo root `C:\Users\pgowd\Documents\Big_Route_Weather`.

## File structure

**New backend files:**

| File | Responsibility |
|---|---|
| `backend/RouteWeather.API/Options/WarmerOptions.cs` | Config: Enabled / IntervalMinutes / ServeStaleMaxHours |
| `backend/RouteWeather.API/Services/IConditionsAggregator.cs` | `FetchMode` enum, `RouteConditionsPair` record, aggregator interface (testability seam) |
| `backend/RouteWeather.API/Services/ConditionsWarmerService.cs` | BackgroundService warm loop |
| `backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj` | New xUnit test project for API-layer code |
| `backend/RouteWeather.API.Tests/TestDbContextFactory.cs` | `IDbContextFactory` over EF InMemory |
| `backend/RouteWeather.API.Tests/TestData.cs` | Route/snapshot/conditions fixture builders |
| `backend/RouteWeather.API.Tests/Fakes.cs` | Fake forecast/snowpack sources (counting), fake aggregator |
| `backend/RouteWeather.API.Tests/ForecastCacheRepositoryTests.cs` | Bulk-read repo tests |
| `backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs` | CacheOnly/ReadThrough behavior tests |
| `backend/RouteWeather.API.Tests/RoutesControllerTests.cs` | Cache-header rule tests |
| `backend/RouteWeather.API.Tests/ConditionsWarmerServiceTests.cs` | Warm-cycle tests |
| `backend/RouteWeather.API.Tests/WarmerOptionsTests.cs` | Option defaults |

**Modified:** `ConditionsAggregator.cs`, `RoutesController.cs`, `Program.cs`, `ForecastCacheRepository.cs`, `appsettings.json`, `fly.toml`, `frontend/src/app/pages/map-home/map-home.ts`, `frontend/src/app/pages/map-home/map-home.spec.ts`.

**Not touched:** peak-detail page, `/api/routes/positions`, `/api/ranges`, grading code, Pages Function proxy.

---

### Task 1: Scaffold the API test project

**Files:**
- Create: `backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
- Create: `backend/RouteWeather.API.Tests/TestDbContextFactory.cs`
- Create: `backend/RouteWeather.API.Tests/TestData.cs`
- Create: `backend/RouteWeather.API.Tests/Fakes.cs`
- Modify: `backend/RouteWeather.slnx` (via `dotnet sln add`)

- [ ] **Step 1: Create the csproj** (mirrors `RouteWeather.Core.Tests` + API reference + ASP.NET framework reference for `DefaultHttpContext` in controller tests)

`backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.8" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RouteWeather.API\RouteWeather.API.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add the project to the solution**

Run: `dotnet sln backend/RouteWeather.slnx add backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 3: Write the test DbContext factory**

`backend/RouteWeather.API.Tests/TestDbContextFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;

namespace RouteWeather.API.Tests;

/// IDbContextFactory over EF InMemory so repositories run unmodified in tests.
/// Each test should use a unique dbName to stay isolated.
public sealed class TestDbContextFactory : IDbContextFactory<RouteWeatherContext>
{
    private readonly DbContextOptions<RouteWeatherContext> _options;

    public TestDbContextFactory(string dbName)
    {
        _options = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
    }

    public RouteWeatherContext CreateDbContext() => new(_options);

    public Task<RouteWeatherContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult(CreateDbContext());
}
```

- [ ] **Step 4: Write the shared fixture builders**

`backend/RouteWeather.API.Tests/TestData.cs`:
```csharp
using RouteWeather.Core.Models;
using RouteWeather.Data;
using RouteWeather.Data.Entities;

namespace RouteWeather.API.Tests;

public static class TestData
{
    public static RouteEntity Route(int id = 1, string slug = "mt-test", string mountain = "Mt Test") => new()
    {
        Id = id,
        Slug = slug,
        Mountain = mountain,
        RouteName = "SW Ridge",
        SummitElevationFt = 12000,
        SummitLat = 43.5,
        SummitLon = -110.8,
        ClassDifficulty = "2",
        SnotelStationTriplet = "999:WY:SNTL",
        RangeId = 1,
    };

    public static RangeEntity Range(int id = 1, string slug = "test-range") => new()
    {
        Id = id,
        Slug = slug,
        Name = "Test Range",
        Color = "#ff0000",
        PerimeterGeoJson = "{}",
        DisplayOrder = 1,
    };

    /// Benign weather that grades well, with enough hourly data for window grades.
    public static WeatherSnapshot Snapshot() => new(
        WindMph: 5,
        TempF: 30,
        PrecipitationProbabilityPct: 10,
        Next48Hours: Enumerable.Range(0, 48)
            .Select(i => new HourlyForecast(DateTimeOffset.UtcNow.AddHours(i), 30, 5, 10, "Clear"))
            .ToList());

    /// Minimal RouteConditions for controller tests (grade present, no weather detail).
    public static RouteConditions Conditions(RouteEntity r, bool isStale) => new(
        new RouteWeather.Core.Models.Route(
            r.Slug, r.Mountain, r.RouteName, r.SummitElevationFt,
            r.SummitLat, r.SummitLon, r.ClassDifficulty, r.SnotelStationTriplet),
        Grade.B,
        85,
        Array.Empty<Driver>(),
        Array.Empty<FactorScore>(),
        "test",
        DateTimeOffset.UtcNow,
        isStale,
        null,
        null,
        null,
        new SourceFreshness(null, null),
        null,
        null);

    public static async Task SeedRoutesAsync(TestDbContextFactory factory, params RouteEntity[] routes)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!db.Ranges.Any()) db.Ranges.Add(Range());
        db.Routes.AddRange(routes);
        await db.SaveChangesAsync();
    }
}
```

> Note: if the `RouteConditions` positional construction fails to compile because `Driver`/`FactorScore` live in a different namespace than `RouteWeather.Core.Models`, check their actual namespace with Grep and adjust the usings — do not change the record itself.

- [ ] **Step 5: Write the fake sources**

`backend/RouteWeather.API.Tests/Fakes.cs`:
```csharp
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;

namespace RouteWeather.API.Tests;

/// Counting fake — FetchCount > 0 in a CacheOnly test means the inversion is broken.
public sealed class FakeForecastSource : IForecastSource
{
    public string Name { get; init; } = "NWS";
    public IReadOnlySet<string> ActiveFactors { get; init; } = ForecastFactors.All;
    public int FetchCount;
    public Func<WeatherSnapshot?> OnFetch { get; set; } = () => TestData.Snapshot();

    public Task<WeatherSnapshot?> FetchAsync(double lat, double lon, CancellationToken ct)
    {
        FetchCount++;
        return Task.FromResult(OnFetch());
    }
}

public sealed class FakeSnowpackSource : ISnowpackSource
{
    public string Name { get; init; } = "SNOTEL";
    public int FetchCount;
    public Func<SnowpackSnapshot?> OnFetch { get; set; } = () => null;

    public Task<SnowpackSnapshot?> FetchAsync(string stationTriplet, CancellationToken ct)
    {
        FetchCount++;
        return Task.FromResult(OnFetch());
    }
}
```

(`FakeConditionsAggregator` is added in Task 5 once `IConditionsAggregator` exists.)

- [ ] **Step 6: Build the test project to verify the scaffold compiles**

Ask the user to stop the local API if it is running, then:

Run: `dotnet build backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: `Build succeeded` (warnings about no tests are fine).

If `TestData.Conditions` fails to compile, fix the using/namespace per the note in Step 4 before proceeding.

- [ ] **Step 7: Commit**

```powershell
git add backend/RouteWeather.API.Tests/ backend/RouteWeather.slnx
git commit -m @'
test(backend): scaffold RouteWeather.API.Tests with InMemory factory and fakes

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: Bulk reads on ForecastCacheRepository

The cache-only path needs last-known rows ignoring TTL: one query per single route, one query for all routes.

**Files:**
- Modify: `backend/RouteWeather.Data/Repositories/ForecastCacheRepository.cs`
- Test: `backend/RouteWeather.API.Tests/ForecastCacheRepositoryTests.cs`

- [ ] **Step 1: Write the failing tests**

`backend/RouteWeather.API.Tests/ForecastCacheRepositoryTests.cs`:
```csharp
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class ForecastCacheRepositoryTests
{
    private static async Task AddRowAsync(TestDbContextFactory factory, int routeId, string source, DateTime expiresAtUtc)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.CachedForecasts.Add(new CachedForecastEntity
        {
            RouteId = routeId,
            Source = source,
            PayloadJson = "{}",
            FetchedAtUtc = DateTime.UtcNow.AddHours(-2),
            ExpiresAtUtc = expiresAtUtc,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetForRouteAsync_ReturnsOnlyThatRoutesRows_IncludingExpired()
    {
        var factory = new TestDbContextFactory(nameof(GetForRouteAsync_ReturnsOnlyThatRoutesRows_IncludingExpired));
        await AddRowAsync(factory, routeId: 1, "NWS", DateTime.UtcNow.AddHours(-1));   // expired
        await AddRowAsync(factory, routeId: 1, "SNOTEL", DateTime.UtcNow.AddHours(1)); // fresh
        await AddRowAsync(factory, routeId: 2, "NWS", DateTime.UtcNow.AddHours(1));    // other route

        var repo = new ForecastCacheRepository(factory);
        var rows = await repo.GetForRouteAsync(1);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.RouteId));
    }

    [Fact]
    public async Task GetAllLatestAsync_ReturnsAllRowsAcrossRoutes()
    {
        var factory = new TestDbContextFactory(nameof(GetAllLatestAsync_ReturnsAllRowsAcrossRoutes));
        await AddRowAsync(factory, routeId: 1, "NWS", DateTime.UtcNow.AddHours(-1));
        await AddRowAsync(factory, routeId: 2, "NWS", DateTime.UtcNow.AddHours(1));
        await AddRowAsync(factory, routeId: 2, "SNOTEL", DateTime.UtcNow.AddHours(1));

        var repo = new ForecastCacheRepository(factory);
        var rows = await repo.GetAllLatestAsync();

        Assert.Equal(3, rows.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: compile error — `ForecastCacheRepository` does not contain `GetForRouteAsync` / `GetAllLatestAsync`.

- [ ] **Step 3: Implement the two methods**

Append inside the `ForecastCacheRepository` class (after `GetAsync`, before `UpsertAsync`):
```csharp
    /// Last-known rows for one route, ignoring TTL — the cache-only read path
    /// decides staleness itself.
    public async Task<List<CachedForecastEntity>> GetForRouteAsync(int routeId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CachedForecasts.AsNoTracking()
            .Where(c => c.RouteId == routeId)
            .ToListAsync(ct);
    }

    /// All last-known rows in one query (87 routes × 6 sources ≈ 522 rows),
    /// so a cold GET /api/routes costs 1 query instead of 522.
    public async Task<List<CachedForecastEntity>> GetAllLatestAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CachedForecasts.AsNoTracking().ToListAsync(ct);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```powershell
git add backend/RouteWeather.Data/Repositories/ForecastCacheRepository.cs backend/RouteWeather.API.Tests/ForecastCacheRepositoryTests.cs
git commit -m @'
feat(backend): bulk last-known reads on ForecastCacheRepository

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: WarmerOptions + configuration

**Files:**
- Create: `backend/RouteWeather.API/Options/WarmerOptions.cs`
- Modify: `backend/RouteWeather.API/appsettings.json`
- Modify: `backend/RouteWeather.API/Program.cs`
- Test: `backend/RouteWeather.API.Tests/WarmerOptionsTests.cs`

- [ ] **Step 1: Write the failing test**

`backend/RouteWeather.API.Tests/WarmerOptionsTests.cs`:
```csharp
using RouteWeather.API.Options;

namespace RouteWeather.API.Tests;

public class WarmerOptionsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var opts = new WarmerOptions();
        Assert.True(opts.Enabled);
        Assert.Equal(10, opts.IntervalMinutes);
        Assert.Equal(24, opts.ServeStaleMaxHours);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: compile error — `WarmerOptions` not found.

- [ ] **Step 3: Implement the options class**

`backend/RouteWeather.API/Options/WarmerOptions.cs`:
```csharp
namespace RouteWeather.API.Options;

public class WarmerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
    public int ServeStaleMaxHours { get; set; } = 24;
}
```

- [ ] **Step 4: Add the config section and binding**

In `backend/RouteWeather.API/appsettings.json`, after the closing `}` of `"ForecastSources"` (add a comma after it), insert:
```json
  "Warmer": {
    "Enabled": true,
    "IntervalMinutes": 10,
    "ServeStaleMaxHours": 24
  }
```

In `backend/RouteWeather.API/Program.cs`, directly below the line
`builder.Services.Configure<ForecastSourcesOptions>(builder.Configuration.GetSection("ForecastSources"));` add:
```csharp
builder.Services.Configure<WarmerOptions>(builder.Configuration.GetSection("Warmer"));
```

Do NOT add a Warmer override to `appsettings.Development.json` — the warmer runs in dev too (a fresh local DB never acquires data otherwise, since user reads no longer fetch upstream).

- [ ] **Step 5: Run the tests to verify pass**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add backend/RouteWeather.API/Options/WarmerOptions.cs backend/RouteWeather.API/appsettings.json backend/RouteWeather.API/Program.cs backend/RouteWeather.API.Tests/WarmerOptionsTests.cs
git commit -m @'
feat(backend): add WarmerOptions config section

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: ConditionsAggregator — FetchMode split (the inversion core)

Adds `FetchMode.CacheOnly` (never upstream) and `FetchMode.ReadThrough` (always recompute, as the warmer needs), extracts result-building into `BuildConditions`, and changes memory TTLs (ReadThrough 30m, CacheOnly 2m). The old `bool useCache` overload is kept as a delegating wrapper so `RoutesController` keeps compiling and behaving as today until Task 5 flips it.

**Files:**
- Create: `backend/RouteWeather.API/Services/IConditionsAggregator.cs`
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs` (full replacement below)
- Modify: `backend/RouteWeather.API/Program.cs`
- Test: `backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs`

- [ ] **Step 1: Write the failing tests**

`backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs`:
```csharp
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RouteWeather.API.Options;
using RouteWeather.API.Services;
using RouteWeather.Core.Grading;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class ConditionsAggregatorTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed class Harness
    {
        public TestDbContextFactory DbFactory { get; }
        public FakeForecastSource Forecast { get; } = new();
        public FakeSnowpackSource Snowpack { get; } = new();
        public MemoryCache Memory { get; } = new(new MemoryCacheOptions());
        public RouteEntity Route { get; } = TestData.Route();
        public ConditionsAggregator Aggregator { get; }

        public Harness(string dbName)
        {
            DbFactory = new TestDbContextFactory(dbName);
            TestData.SeedRoutesAsync(DbFactory, Route).GetAwaiter().GetResult();

            var sourceOptions = new ForecastSourcesOptions
            {
                Sources =
                [
                    new SourceOptions { Name = "NWS", Enabled = true, Weight = 1.0, CacheTtlMinutes = 60 },
                    new SourceOptions { Name = "SNOTEL", Enabled = true, Weight = 1.0, CacheTtlMinutes = 60 },
                ],
            };

            Aggregator = new ConditionsAggregator(
                new[] { Forecast },
                new[] { Snowpack },
                new ForecastCacheRepository(DbFactory),
                Microsoft.Extensions.Options.Options.Create(sourceOptions),
                Microsoft.Extensions.Options.Options.Create(new WarmerOptions()),
                new ConsensusCalculator(0.25, 0.50),
                Memory,
                NullLogger<ConditionsAggregator>.Instance);
        }

        public async Task AddForecastRowAsync(DateTime fetchedAtUtc, DateTime expiresAtUtc)
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            db.CachedForecasts.Add(new CachedForecastEntity
            {
                RouteId = Route.Id,
                Source = "NWS",
                PayloadJson = JsonSerializer.Serialize(TestData.Snapshot(), JsonOpts),
                FetchedAtUtc = fetchedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CacheOnly_NoRows_ReturnsNullGrade_AndNeverCallsUpstream()
    {
        var h = new Harness(nameof(CacheOnly_NoRows_ReturnsNullGrade_AndNeverCallsUpstream));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.Null(conditions.Grade);
        Assert.Equal(0, h.Forecast.FetchCount);
        Assert.Equal(0, h.Snowpack.FetchCount);
    }

    [Fact]
    public async Task CacheOnly_ExpiredRowWithin24h_ServesGradeMarkedStale_WithoutUpstream()
    {
        var h = new Harness(nameof(CacheOnly_ExpiredRowWithin24h_ServesGradeMarkedStale_WithoutUpstream));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddHours(-2),
            expiresAtUtc: DateTime.UtcNow.AddHours(-1)); // expired but well inside 24h

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.Grade);
        Assert.True(conditions.IsStale);
        Assert.Equal(0, h.Forecast.FetchCount);
    }

    [Fact]
    public async Task CacheOnly_RowOlderThan24h_IsTreatedAsMissing()
    {
        var h = new Harness(nameof(CacheOnly_RowOlderThan24h_IsTreatedAsMissing));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddHours(-25),
            expiresAtUtc: DateTime.UtcNow.AddHours(-24));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.Null(conditions.Grade);
        Assert.Equal(0, h.Forecast.FetchCount);
    }

    [Fact]
    public async Task CacheOnly_FreshRow_IsNotStale()
    {
        var h = new Harness(nameof(CacheOnly_FreshRow_IsNotStale));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.Grade);
        Assert.False(conditions.IsStale);
    }

    [Fact]
    public async Task CacheOnly_MemoryHit_SkipsSqlite()
    {
        var h = new Harness(nameof(CacheOnly_MemoryHit_SkipsSqlite));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));

        var first = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);
        Assert.NotNull(first.Grade);

        // Wipe SQLite; a memory hit must still serve the conditions.
        await using (var db = await h.DbFactory.CreateDbContextAsync())
        {
            db.CachedForecasts.RemoveRange(db.CachedForecasts);
            await db.SaveChangesAsync();
        }

        var second = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);
        Assert.NotNull(second.Grade);
    }

    [Fact]
    public async Task ReadThrough_FetchesUpstream_ThenCacheOnlyServesFromMemory()
    {
        var h = new Harness(nameof(ReadThrough_FetchesUpstream_ThenCacheOnlyServesFromMemory));

        var warmed = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.ReadThrough);
        Assert.NotNull(warmed.Grade);
        Assert.False(warmed.IsStale);
        Assert.Equal(1, h.Forecast.FetchCount);

        var read = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);
        Assert.NotNull(read.Grade);
        Assert.Equal(1, h.Forecast.FetchCount); // no additional upstream call
    }

    [Fact]
    public async Task GetManyCacheOnly_MixedRoutes_GradesOnlyThoseWithData()
    {
        var h = new Harness(nameof(GetManyCacheOnly_MixedRoutes_GradesOnlyThoseWithData));
        var bare = TestData.Route(id: 2, slug: "mt-bare", mountain: "Mt Bare");
        await TestData.SeedRoutesAsync(h.DbFactory, bare);
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50)); // row belongs to h.Route (id 1)

        var pairs = await h.Aggregator.GetManyCacheOnlyAsync(new[] { h.Route, bare });

        Assert.Equal(2, pairs.Count);
        Assert.NotNull(pairs.Single(p => p.Route.Slug == "mt-test").Conditions.Grade);
        Assert.Null(pairs.Single(p => p.Route.Slug == "mt-bare").Conditions.Grade);
        Assert.Equal(0, h.Forecast.FetchCount);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: compile errors — `FetchMode` not found, no matching `GetConditionsAsync` overload, no `GetManyCacheOnlyAsync`, aggregator ctor mismatch.

- [ ] **Step 3: Create the interface file**

`backend/RouteWeather.API/Services/IConditionsAggregator.cs`:
```csharp
using RouteWeather.Core.Models;
using RouteWeather.Data.Entities;

namespace RouteWeather.API.Services;

public enum FetchMode
{
    /// Serve from memory cache or last-known SQLite rows; never call upstream sources.
    CacheOnly,

    /// Recompute: respect per-source SQLite TTLs, fetch expired sources upstream,
    /// overwrite the memory cache. Used by the warmer only.
    ReadThrough,
}

public sealed record RouteConditionsPair(RouteEntity Route, RouteConditions Conditions);

public interface IConditionsAggregator
{
    Task<RouteConditions> GetConditionsAsync(RouteEntity routeEntity, FetchMode mode, CancellationToken ct = default);

    Task<IReadOnlyList<RouteConditionsPair>> GetManyCacheOnlyAsync(IReadOnlyList<RouteEntity> routes, CancellationToken ct = default);
}
```

- [ ] **Step 4: Replace ConditionsAggregator.cs**

Full new content of `backend/RouteWeather.API/Services/ConditionsAggregator.cs` (the `FetchForecastAsync`, `FetchSnowpackAsync`, `Deserialize`, `MaxOf`, and result-record members are unchanged from the current file; `BuildConditions` is the extracted body of the old lines 75–147 with the memory-cache `Set` moved out to callers and a `forceStale` input):
```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RouteWeather.API.Options;
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

public class ConditionsAggregator : IConditionsAggregator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    // The warmer overwrites entries every cycle (default 10m). If it stalls, entries
    // expire at 30m and reads degrade to SQLite last-known instead of frozen data.
    private static readonly TimeSpan ReadThroughCacheTtl = TimeSpan.FromMinutes(30);
    // Cache-only results may be stale; keep them only briefly so the warmer's
    // fresh write supersedes them quickly.
    private static readonly TimeSpan CacheOnlyCacheTtl = TimeSpan.FromMinutes(2);

    private readonly IReadOnlyList<IForecastSource> _forecastSources;
    private readonly IReadOnlyList<ISnowpackSource> _snowpackSources;
    private readonly ForecastCacheRepository _cache;
    private readonly ForecastSourcesOptions _options;
    private readonly TimeSpan _serveStaleMax;
    private readonly ConsensusCalculator _consensus;
    private readonly IMemoryCache _conditionsCache;
    private readonly ILogger<ConditionsAggregator> _logger;

    public ConditionsAggregator(
        IEnumerable<IForecastSource> forecastSources,
        IEnumerable<ISnowpackSource> snowpackSources,
        ForecastCacheRepository cache,
        IOptions<ForecastSourcesOptions> options,
        IOptions<WarmerOptions> warmerOptions,
        ConsensusCalculator consensus,
        IMemoryCache conditionsCache,
        ILogger<ConditionsAggregator> logger)
    {
        _forecastSources = forecastSources.ToArray();
        _snowpackSources = snowpackSources.ToArray();
        _cache = cache;
        _options = options.Value;
        _serveStaleMax = TimeSpan.FromHours(warmerOptions.Value.ServeStaleMaxHours);
        _consensus = consensus;
        _conditionsCache = conditionsCache;
        _logger = logger;
    }

    // Legacy entry point; controllers move off it in the next commit.
    public async Task<RouteConditions> GetConditionsAsync(
        RouteEntity routeEntity,
        bool useCache = true,
        CancellationToken ct = default)
    {
        if (useCache
            && _conditionsCache.TryGetValue(ConditionsCacheKey(routeEntity.Slug), out RouteConditions? cached)
            && cached is not null)
        {
            return cached;
        }
        return await GetConditionsAsync(routeEntity, FetchMode.ReadThrough, ct);
    }

    public async Task<RouteConditions> GetConditionsAsync(
        RouteEntity routeEntity,
        FetchMode mode,
        CancellationToken ct = default)
    {
        var cacheKey = ConditionsCacheKey(routeEntity.Slug);

        if (mode == FetchMode.CacheOnly)
        {
            // No per-slug gate here: reads must never queue behind a warmer
            // aggregation that is mid-flight on upstream fetches.
            if (_conditionsCache.TryGetValue(cacheKey, out RouteConditions? cached) && cached is not null)
            {
                return cached;
            }
            var rows = await _cache.GetForRouteAsync(routeEntity.Id, ct);
            return BuildFromCachedRows(routeEntity, rows);
        }

        var gate = Gates.GetOrAdd(routeEntity.Slug, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var forecastFetches = _forecastSources
                .Select(s => FetchForecastAsync(routeEntity, s, ct))
                .ToArray();
            var snowpackFetches = _snowpackSources
                .Select(s => FetchSnowpackAsync(routeEntity, s, ct))
                .ToArray();

            await Task.WhenAll(forecastFetches.Cast<Task>().Concat(snowpackFetches));

            var conditions = BuildConditions(
                routeEntity,
                forecastFetches.Select(t => t.Result).ToList(),
                snowpackFetches.Select(t => t.Result).ToList(),
                forceStale: false);

            if (conditions.Grade is not null)
            {
                _conditionsCache.Set(cacheKey, conditions, ReadThroughCacheTtl);
            }

            return conditions;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<RouteConditionsPair>> GetManyCacheOnlyAsync(
        IReadOnlyList<RouteEntity> routes,
        CancellationToken ct = default)
    {
        var anyMiss = routes.Any(r =>
            !_conditionsCache.TryGetValue(ConditionsCacheKey(r.Slug), out RouteConditions? c) || c is null);
        var rowsByRoute = anyMiss
            ? (await _cache.GetAllLatestAsync(ct)).ToLookup(r => r.RouteId)
            : null;

        var results = new List<RouteConditionsPair>(routes.Count);
        foreach (var route in routes)
        {
            if (_conditionsCache.TryGetValue(ConditionsCacheKey(route.Slug), out RouteConditions? cached) && cached is not null)
            {
                results.Add(new RouteConditionsPair(route, cached));
            }
            else
            {
                results.Add(new RouteConditionsPair(route, BuildFromCachedRows(route, rowsByRoute![route.Id].ToList())));
            }
        }
        return results;
    }

    private RouteConditions BuildFromCachedRows(RouteEntity routeEntity, IReadOnlyList<CachedForecastEntity> rows)
    {
        var nowUtc = DateTime.UtcNow;
        var cutoffUtc = nowUtc - _serveStaleMax;

        var forecastResults = _forecastSources.Select(s =>
        {
            var row = rows.FirstOrDefault(r => string.Equals(r.Source, s.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null || row.FetchedAtUtc < cutoffUtc)
            {
                return new SourceFetchResult(s.Name, null, null, true, s.ActiveFactors);
            }
            return new SourceFetchResult(s.Name, Deserialize<WeatherSnapshot>(row.PayloadJson),
                new DateTimeOffset(row.FetchedAtUtc, TimeSpan.Zero), row.ExpiresAtUtc <= nowUtc, s.ActiveFactors);
        }).ToList();

        var snowpackResults = _snowpackSources.Select(s =>
        {
            var row = rows.FirstOrDefault(r => string.Equals(r.Source, s.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null || row.FetchedAtUtc < cutoffUtc)
            {
                return new SnowpackFetchResult(s.Name, null, null, true);
            }
            return new SnowpackFetchResult(s.Name, Deserialize<SnowpackSnapshot>(row.PayloadJson),
                new DateTimeOffset(row.FetchedAtUtc, TimeSpan.Zero), row.ExpiresAtUtc <= nowUtc);
        }).ToList();

        // Stale = any *served* row past its per-source TTL. Rows missing entirely
        // (never fetched, or beyond the 24h cap) flow through the standard
        // "source absent" semantics instead, so a chronically failing source
        // cannot mark every response stale forever.
        var forceStale = forecastResults.Any(r => r.Snapshot is not null && r.IsStale)
                         || snowpackResults.Any(r => r.Snapshot is not null && r.IsStale);

        var conditions = BuildConditions(routeEntity, forecastResults, snowpackResults, forceStale);

        var cacheKey = ConditionsCacheKey(routeEntity.Slug);
        if (conditions.Grade is not null && !_conditionsCache.TryGetValue(cacheKey, out _))
        {
            _conditionsCache.Set(cacheKey, conditions, CacheOnlyCacheTtl);
        }
        return conditions;
    }

    private RouteConditions BuildConditions(
        RouteEntity routeEntity,
        List<SourceFetchResult> forecastResults,
        List<SnowpackFetchResult> snowpackResults,
        bool forceStale)
    {
        var liveForecasts = forecastResults.Where(r => r.Snapshot is not null).ToList();
        var consensusInputs = liveForecasts
            .Select(r => new ConsensusInput(
                new SourceSnapshot(r.SourceName, r.Snapshot!, r.FetchedAt ?? DateTimeOffset.UtcNow, r.ActiveFactors),
                _options.WeightFor(r.SourceName)))
            .ToList();

        var ensemble = _consensus.Compute(consensusInputs, _forecastSources.Count);
        var blendedWeather = ensemble.Blended;
        var snowpack = snowpackResults.FirstOrDefault(r => r.Snapshot is not null).Snapshot;

        var result = GradeCalculator.Compute(blendedWeather, snowpack);

        var weatherFetched = liveForecasts.Count == 0 ? null : liveForecasts.Max(r => r.FetchedAt);
        var snowpackFetched = snowpackResults.Where(r => r.Snapshot is not null).Select(r => r.FetchedAt).FirstOrDefault();

        var updatedAt = MaxOf(weatherFetched, snowpackFetched) ?? DateTimeOffset.UtcNow;
        var isStale = forceStale
                      || (forecastResults.Any(r => r.IsStale) && liveForecasts.Count == 0)
                      || snowpackResults.Any(r => r.IsStale && snowpack is not null);

        var windowGrades = blendedWeather is null && snowpack is null
            ? null
            : WindowGradeCalculator.Compute(blendedWeather, snowpack);

        var route = new Core.Models.Route(
            routeEntity.Slug,
            routeEntity.Mountain,
            routeEntity.RouteName,
            routeEntity.SummitElevationFt,
            routeEntity.SummitLat,
            routeEntity.SummitLon,
            routeEntity.ClassDifficulty,
            routeEntity.SnotelStationTriplet);

        var nwsResult = forecastResults.FirstOrDefault(r => r.SourceName == "NWS");
        var sourceFreshness = new SourceFreshness(
            nwsResult.FetchedAt ?? weatherFetched,
            snowpackFetched);

        var perSourceForecast = liveForecasts
            .Select(r => new PerSourceForecast(
                r.SourceName,
                r.Snapshot!.WindMph,
                r.Snapshot.TempF,
                r.ActiveFactors.Contains(ForecastFactors.Precipitation) ? r.Snapshot.PrecipitationProbabilityPct : (int?)null,
                r.FetchedAt ?? DateTimeOffset.UtcNow))
            .ToList();

        return new RouteConditions(
            route,
            blendedWeather is null && snowpack is null ? null : result.Grade,
            blendedWeather is null && snowpack is null ? null : result.OverallScore,
            result.Drivers,
            result.Factors,
            result.Rationale,
            updatedAt,
            isStale,
            blendedWeather,
            snowpack,
            windowGrades,
            sourceFreshness,
            ensemble.Consensus,
            perSourceForecast.Count == 0 ? null : perSourceForecast);
    }

    private static string ConditionsCacheKey(string slug) => $"conditions:{slug}";

    private async Task<SourceFetchResult> FetchForecastAsync(RouteEntity route, IForecastSource source, CancellationToken ct)
    {
        var ttl = _options.TtlFor(source.Name);
        var nowUtc = DateTime.UtcNow;
        var cached = await _cache.GetAsync(route.Id, source.Name, ct);

        if (cached is not null && cached.ExpiresAtUtc > nowUtc)
        {
            return new SourceFetchResult(source.Name, Deserialize<WeatherSnapshot>(cached.PayloadJson),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), false, source.ActiveFactors);
        }

        WeatherSnapshot? fresh = null;
        try
        {
            fresh = await source.FetchAsync(route.SummitLat, route.SummitLon, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Forecast source {Source} threw for {Slug}", source.Name, route.Slug);
        }

        if (fresh is not null)
        {
            await _cache.UpsertAsync(route.Id, source.Name, JsonSerializer.Serialize(fresh, JsonOpts), nowUtc.Add(ttl), ct);
            return new SourceFetchResult(source.Name, fresh, DateTimeOffset.UtcNow, false, source.ActiveFactors);
        }

        if (cached is not null)
        {
            _logger.LogInformation("Serving stale {Source} data for {Slug}", source.Name, route.Slug);
            return new SourceFetchResult(source.Name, Deserialize<WeatherSnapshot>(cached.PayloadJson),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), true, source.ActiveFactors);
        }

        return new SourceFetchResult(source.Name, null, null, true, source.ActiveFactors);
    }

    private async Task<SnowpackFetchResult> FetchSnowpackAsync(RouteEntity route, ISnowpackSource source, CancellationToken ct)
    {
        var ttl = _options.TtlFor(source.Name);
        var nowUtc = DateTime.UtcNow;
        var cached = await _cache.GetAsync(route.Id, source.Name, ct);

        if (cached is not null && cached.ExpiresAtUtc > nowUtc)
        {
            return new SnowpackFetchResult(source.Name, Deserialize<SnowpackSnapshot>(cached.PayloadJson),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), false);
        }

        SnowpackSnapshot? fresh = null;
        try
        {
            fresh = await source.FetchAsync(route.SnotelStationTriplet, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snowpack source {Source} threw for {Slug}", source.Name, route.Slug);
        }

        if (fresh is not null)
        {
            await _cache.UpsertAsync(route.Id, source.Name, JsonSerializer.Serialize(fresh, JsonOpts), nowUtc.Add(ttl), ct);
            return new SnowpackFetchResult(source.Name, fresh, DateTimeOffset.UtcNow, false);
        }

        if (cached is not null)
        {
            _logger.LogInformation("Serving stale {Source} data for {Slug}", source.Name, route.Slug);
            return new SnowpackFetchResult(source.Name, Deserialize<SnowpackSnapshot>(cached.PayloadJson),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), true);
        }

        return new SnowpackFetchResult(source.Name, null, null, true);
    }

    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOpts);

    private static DateTimeOffset? MaxOf(DateTimeOffset? a, DateTimeOffset? b) =>
        (a, b) switch
        {
            (null, null) => null,
            (null, _) => b,
            (_, null) => a,
            _ => a > b ? a : b,
        };

    private record struct SourceFetchResult(string SourceName, WeatherSnapshot? Snapshot, DateTimeOffset? FetchedAt, bool IsStale, IReadOnlySet<string> ActiveFactors);
    private record struct SnowpackFetchResult(string SourceName, SnowpackSnapshot? Snapshot, DateTimeOffset? FetchedAt, bool IsStale);
}
```

- [ ] **Step 5: Register the interface in Program.cs**

In `backend/RouteWeather.API/Program.cs`, replace the line
`builder.Services.AddScoped<ConditionsAggregator>();` with:
```csharp
builder.Services.AddScoped<ConditionsAggregator>();
builder.Services.AddScoped<IConditionsAggregator>(sp => sp.GetRequiredService<ConditionsAggregator>());
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: PASS (all aggregator tests plus earlier tasks' tests).

Also run the existing Core tests to confirm nothing regressed:
Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add backend/RouteWeather.API/Services/IConditionsAggregator.cs backend/RouteWeather.API/Services/ConditionsAggregator.cs backend/RouteWeather.API/Program.cs backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs
git commit -m @'
feat(backend): FetchMode split on ConditionsAggregator (CacheOnly vs ReadThrough)

CacheOnly serves memory or last-known SQLite rows (<=24h) and can never
reach upstream sources. ReadThrough (warmer-only) recomputes and writes
the memory cache with a 30m TTL.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 5: Controllers go CacheOnly + conditional cache headers

This is the commit where user-facing behavior flips: GETs stop fetching upstream.

**Files:**
- Modify: `backend/RouteWeather.API/Controllers/RoutesController.cs`
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs` (delete legacy bool overload)
- Modify: `backend/RouteWeather.API.Tests/Fakes.cs` (add `FakeConditionsAggregator`)
- Test: `backend/RouteWeather.API.Tests/RoutesControllerTests.cs`

- [ ] **Step 1: Add the fake aggregator to Fakes.cs**

Append to `backend/RouteWeather.API.Tests/Fakes.cs` (add `using RouteWeather.API.Services;`, `using RouteWeather.Data.Entities;` to the top of the file):
```csharp
public sealed class FakeConditionsAggregator : IConditionsAggregator
{
    public Func<RouteEntity, Core.Models.RouteConditions> OnGet { get; set; } =
        r => TestData.Conditions(r, isStale: false);
    public int Calls;
    public List<FetchMode> ModesSeen { get; } = new();

    public Task<Core.Models.RouteConditions> GetConditionsAsync(RouteEntity routeEntity, FetchMode mode, CancellationToken ct = default)
    {
        Calls++;
        ModesSeen.Add(mode);
        return Task.FromResult(OnGet(routeEntity));
    }

    public Task<IReadOnlyList<RouteConditionsPair>> GetManyCacheOnlyAsync(IReadOnlyList<RouteEntity> routes, CancellationToken ct = default)
    {
        Calls++;
        IReadOnlyList<RouteConditionsPair> pairs = routes.Select(r => new RouteConditionsPair(r, OnGet(r))).ToList();
        return Task.FromResult(pairs);
    }
}
```
(If `Core.Models.RouteConditions` doesn't resolve, fully qualify as `RouteWeather.Core.Models.RouteConditions`.)

- [ ] **Step 2: Write the failing controller tests**

`backend/RouteWeather.API.Tests/RoutesControllerTests.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RouteWeather.API.Controllers;
using RouteWeather.API.Services;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class RoutesControllerTests
{
    private static RoutesController Build(TestDbContextFactory dbFactory, IConditionsAggregator aggregator) =>
        new(new RouteRepository(dbFactory), aggregator)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task GetAll_AllFresh_KeepsEdgeCachePolicy()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_AllFresh_KeepsEdgeCachePolicy));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        var result = await controller.GetAll(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("public, max-age=900, stale-while-revalidate=3600",
            controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetAll_AnyStale_SendsNoCache()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_AnyStale_SendsNoCache));
        await TestData.SeedRoutesAsync(dbFactory,
            TestData.Route(id: 1, slug: "mt-fresh", mountain: "Mt Fresh"),
            TestData.Route(id: 2, slug: "mt-stale", mountain: "Mt Stale"));
        var fake = new FakeConditionsAggregator
        {
            OnGet = r => TestData.Conditions(r, isStale: r.Slug == "mt-stale"),
        };
        var controller = Build(dbFactory, fake);

        await controller.GetAll(CancellationToken.None);

        Assert.Equal("no-cache", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetBySlug_Stale_SendsNoCache()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_Stale_SendsNoCache));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var fake = new FakeConditionsAggregator { OnGet = r => TestData.Conditions(r, isStale: true) };
        var controller = Build(dbFactory, fake);

        var result = await controller.GetBySlug("mt-test", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("no-cache", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(FetchMode.CacheOnly, fake.ModesSeen.Single());
    }

    [Fact]
    public async Task GetBySlug_Fresh_KeepsEdgeCachePolicy()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_Fresh_KeepsEdgeCachePolicy));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        await controller.GetBySlug("mt-test", CancellationToken.None);

        Assert.Equal("public, max-age=900, stale-while-revalidate=3600",
            controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetBySlug_UnknownSlug_ReturnsNotFound()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_UnknownSlug_ReturnsNotFound));
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        var result = await controller.GetBySlug("nope", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: compile error — `RoutesController` ctor takes `ConditionsAggregator` (concrete), not `IConditionsAggregator`.

- [ ] **Step 4: Rewrite the controller's read path**

In `backend/RouteWeather.API/Controllers/RoutesController.cs`:

Replace the constants/fields/ctor block (currently lines 13–24) with:
```csharp
    private const string CachedPolicy = "public, max-age=900, stale-while-revalidate=3600";
    private const string PositionsCachePolicy = "public, max-age=86400, stale-while-revalidate=604800";
    // Stale payloads must not be browser-cached for 15 minutes, or the
    // frontend's recovery refetch would be served the same stale bytes.
    private const string NoCachePolicy = "no-cache";

    private readonly RouteRepository _routes;
    private readonly IConditionsAggregator _aggregator;

    public RoutesController(RouteRepository routes, IConditionsAggregator aggregator)
    {
        _routes = routes;
        _aggregator = aggregator;
    }
```

Replace the `GetAll` method with:
```csharp
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var routes = await _routes.GetAllAsync(ct);
        var pairs = await _aggregator.GetManyCacheOnlyAsync(routes, ct);
        var dto = pairs.Select(p => ToSummary(p.Route, p.Conditions)).ToList();
        Response.Headers.CacheControl = pairs.Any(p => p.Conditions.IsStale) ? NoCachePolicy : CachedPolicy;
        return Ok(dto);
    }
```

Replace the `GetBySlug` method with:
```csharp
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var route = await _routes.GetBySlugAsync(slug, ct);
        if (route is null) return NotFound();
        var conditions = await _aggregator.GetConditionsAsync(route, FetchMode.CacheOnly, ct);
        Response.Headers.CacheControl = conditions.IsStale ? NoCachePolicy : CachedPolicy;
        return Ok(ToDetail(route, conditions));
    }
```

Remove the now-unused `MaxConcurrentFetches` constant (the 8-wide gate moves to the warmer in Task 6). `GetPositions`, `ToSummary`, `ToDetail`, `SerializeConsensus`, `SerializeWindow` stay unchanged. Add `using RouteWeather.API.Services;` if not present (it is — keep it).

- [ ] **Step 5: Delete the legacy bool overload**

In `backend/RouteWeather.API/Services/ConditionsAggregator.cs`, delete the entire
`public async Task<RouteConditions> GetConditionsAsync(RouteEntity routeEntity, bool useCache = true, ...)` method (marked "Legacy entry point" in Task 4). Nothing references it anymore.

- [ ] **Step 6: Run all backend tests**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: PASS.
Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add backend/RouteWeather.API/Controllers/RoutesController.cs backend/RouteWeather.API/Services/ConditionsAggregator.cs backend/RouteWeather.API.Tests/Fakes.cs backend/RouteWeather.API.Tests/RoutesControllerTests.cs
git commit -m @'
feat(backend): user GETs serve cache-only with no-cache header on stale data

GET /api/routes and /api/routes/{slug} never trigger upstream fetches;
stale responses opt out of the 15m edge/browser cache so the frontend
recovery refetch can observe fresh data.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 6: ConditionsWarmerService

**Files:**
- Create: `backend/RouteWeather.API/Services/ConditionsWarmerService.cs`
- Modify: `backend/RouteWeather.API/Program.cs`
- Modify: `backend/RouteWeather.API/RouteWeather.API.csproj` (InternalsVisibleTo for the tests)
- Test: `backend/RouteWeather.API.Tests/ConditionsWarmerServiceTests.cs`

- [ ] **Step 0: Expose internals to the test project**

The tests drive `RunCycleAsync`, which is `internal`. In `backend/RouteWeather.API/RouteWeather.API.csproj` add inside the `<Project>` element (as its own `ItemGroup`):
```xml
  <ItemGroup>
    <InternalsVisibleTo Include="RouteWeather.API.Tests" />
  </ItemGroup>
```

- [ ] **Step 1: Write the failing tests**

`backend/RouteWeather.API.Tests/ConditionsWarmerServiceTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RouteWeather.API.Options;
using RouteWeather.API.Services;
using RouteWeather.Data;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class ConditionsWarmerServiceTests
{
    private static (IServiceScopeFactory ScopeFactory, FakeConditionsAggregator Fake) BuildScope(TestDbContextFactory dbFactory)
    {
        var fake = new FakeConditionsAggregator();
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<RouteWeatherContext>>(dbFactory);
        services.AddScoped<RouteRepository>();
        services.AddSingleton<IConditionsAggregator>(fake);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), fake);
    }

    // Fully qualified on purpose: inside namespace RouteWeather.API.Tests, a bare
    // `Options` resolves to the RouteWeather.API.Options NAMESPACE (enclosing-namespace
    // lookup beats using directives) and fails to compile.
    private static ConditionsWarmerService BuildWarmer(IServiceScopeFactory scopeFactory, WarmerOptions? options = null) =>
        new(scopeFactory, Microsoft.Extensions.Options.Options.Create(options ?? new WarmerOptions()),
            NullLogger<ConditionsWarmerService>.Instance);

    [Fact]
    public async Task RunCycle_WarmsEveryRoute_InReadThroughMode()
    {
        var dbFactory = new TestDbContextFactory(nameof(RunCycle_WarmsEveryRoute_InReadThroughMode));
        await TestData.SeedRoutesAsync(dbFactory,
            TestData.Route(id: 1, slug: "mt-a", mountain: "Mt A"),
            TestData.Route(id: 2, slug: "mt-b", mountain: "Mt B"),
            TestData.Route(id: 3, slug: "mt-c", mountain: "Mt C"));
        var (scopeFactory, fake) = BuildScope(dbFactory);
        var warmer = BuildWarmer(scopeFactory);

        await warmer.RunCycleAsync(CancellationToken.None);

        Assert.Equal(3, fake.Calls);
        Assert.All(fake.ModesSeen, m => Assert.Equal(FetchMode.ReadThrough, m));
    }

    [Fact]
    public async Task RunCycle_OneRouteThrows_OthersStillWarm_NoExceptionEscapes()
    {
        var dbFactory = new TestDbContextFactory(nameof(RunCycle_OneRouteThrows_OthersStillWarm_NoExceptionEscapes));
        await TestData.SeedRoutesAsync(dbFactory,
            TestData.Route(id: 1, slug: "mt-good", mountain: "Mt Good"),
            TestData.Route(id: 2, slug: "mt-bad", mountain: "Mt Bad"),
            TestData.Route(id: 3, slug: "mt-fine", mountain: "Mt Fine"));
        var (scopeFactory, fake) = BuildScope(dbFactory);
        fake.OnGet = r => r.Slug == "mt-bad"
            ? throw new InvalidOperationException("boom")
            : TestData.Conditions(r, isStale: false);
        var warmer = BuildWarmer(scopeFactory);

        await warmer.RunCycleAsync(CancellationToken.None); // must not throw

        Assert.Equal(3, fake.Calls); // all three attempted
    }

    [Fact]
    public async Task Disabled_RunsNoCycles()
    {
        var dbFactory = new TestDbContextFactory(nameof(Disabled_RunsNoCycles));
        var (scopeFactory, fake) = BuildScope(dbFactory);
        var warmer = BuildWarmer(scopeFactory, new WarmerOptions { Enabled = false });

        await warmer.StartAsync(CancellationToken.None);
        await (warmer.ExecuteTask ?? Task.CompletedTask);
        await warmer.StopAsync(CancellationToken.None);

        Assert.Equal(0, fake.Calls);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: compile error — `ConditionsWarmerService` not found.

- [ ] **Step 3: Implement the warmer**

`backend/RouteWeather.API/Services/ConditionsWarmerService.cs`:
```csharp
using Microsoft.Extensions.Options;
using RouteWeather.API.Options;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

/// Owns all upstream weather fetching: re-aggregates every route on startup and
/// on a fixed interval so user requests are pure cache reads. The single-loop
/// do/while makes cycles non-reentrant — a slow cycle delays the next tick.
public class ConditionsWarmerService : BackgroundService
{
    private const int MaxConcurrentFetches = 8;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WarmerOptions _options;
    private readonly ILogger<ConditionsWarmerService> _logger;

    public ConditionsWarmerService(
        IServiceScopeFactory scopeFactory,
        IOptions<WarmerOptions> options,
        ILogger<ConditionsWarmerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Conditions warmer disabled by configuration");
            return;
        }

        // Yield so host startup is never blocked by the first (long) cycle.
        await Task.Yield();

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));
        do
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Warm cycle failed; retrying on next tick");
            }
        }
        while (await WaitForNextTickSafeAsync(timer, ct));
    }

    internal async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var routes = await scope.ServiceProvider.GetRequiredService<RouteRepository>().GetAllAsync(ct);
        var aggregator = scope.ServiceProvider.GetRequiredService<IConditionsAggregator>();

        using var gate = new SemaphoreSlim(MaxConcurrentFetches, MaxConcurrentFetches);
        var tasks = routes.Select(async route =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await aggregator.GetConditionsAsync(route, FetchMode.ReadThrough, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown — let the cycle wind down quietly.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Warm fetch failed for {Slug}", route.Slug);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);

        _logger.LogInformation("Warm cycle completed for {Count} routes", routes.Count);
    }

    private static async Task<bool> WaitForNextTickSafeAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Register the hosted service**

In `backend/RouteWeather.API/Program.cs`, directly below
`builder.Services.AddHostedService<DailyCallPersistenceService>();` add:
```csharp
builder.Services.AddHostedService<ConditionsWarmerService>();
```

- [ ] **Step 5: Run all backend tests**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add backend/RouteWeather.API/Services/ConditionsWarmerService.cs backend/RouteWeather.API/Program.cs backend/RouteWeather.API/RouteWeather.API.csproj backend/RouteWeather.API.Tests/ConditionsWarmerServiceTests.cs
git commit -m @'
feat(backend): ConditionsWarmerService background warm loop

Re-aggregates all routes on startup and every IntervalMinutes (default 10),
8-wide, with per-route and per-cycle error containment. The warmer is now
the only code path that reaches upstream weather APIs.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 7: fly.toml — always-on machine

**Files:**
- Modify: `backend/fly.toml`

- [ ] **Step 1: Set min_machines_running**

In `backend/fly.toml` change `min_machines_running = 0` to `min_machines_running = 1`. Leave `auto_stop_machines` and `auto_start_machines` as they are (auto-stop never goes below the minimum).

- [ ] **Step 2: Commit**

```powershell
git add backend/fly.toml
git commit -m @'
feat(infra): keep one Fly machine always running for the warm loop

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

(Takes effect on the next `flyctl deploy`, which CI runs on merge. Accepted cost ~$2–3/month per the approved spec.)

---

### Task 8: Frontend — marker layer split + ghost rendering for null grades

Two birds: (a) `renderMarkers()` currently pushes markers into `this.layers` and never clears them, so a second call (which the Task 9 refetch loop will make) duplicates every marker — markers move to their own cleared-per-render list; (b) `grade: null` routes currently render as an interactive `grade-x` dot — they must render ghost-style.

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts`
- Test: `frontend/src/app/pages/map-home/map-home.spec.ts`

- [ ] **Step 1: Write the failing tests**

Append to `frontend/src/app/pages/map-home/map-home.spec.ts` (inside the file, as a new top-level `describe`; add `markerIconSpec` to the existing import from `./map-home`):
```ts
describe('markerIconSpec', () => {
  it('renders graded routes as interactive grade dots', () => {
    const spec = markerIconSpec({ grade: 'B' });
    expect(spec.className).toBe('peak-marker');
    expect(spec.dotClass).toBe('grade-b');
    expect(spec.interactive).toBe(true);
  });

  it('renders null-grade routes as non-interactive ghost markers', () => {
    const spec = markerIconSpec({ grade: null });
    expect(spec.className).toBe('peak-marker peak-marker-ghost');
    expect(spec.dotClass).toBe('grade-ghost');
    expect(spec.interactive).toBe(false);
  });
});
```

- [ ] **Step 2: Run to verify they fail**

Run (from `frontend/`): `npm test`
Expected: FAIL — `markerIconSpec` is not exported.

- [ ] **Step 3: Implement**

In `frontend/src/app/pages/map-home/map-home.ts`:

(a) Add the exported helper near the other module-level functions (next to `popupHtml`):
```ts
export interface MarkerIconSpec {
  className: string;
  dotClass: string;
  interactive: boolean;
}

// Null grade = no usable data (<=24h) on the backend; show the same ghost
// treatment as the pre-data positions markers instead of a broken grade dot.
export function markerIconSpec(route: Pick<RouteSummary, 'grade'>): MarkerIconSpec {
  if (route.grade == null) {
    return { className: 'peak-marker peak-marker-ghost', dotClass: 'grade-ghost', interactive: false };
  }
  return { className: 'peak-marker', dotClass: `grade-${route.grade.toLowerCase()}`, interactive: true };
}
```

(b) Add a dedicated marker layer list next to the existing layer fields:
```ts
  private map: any | null = null;
  private layers: any[] = [];
  private ghostLayers: any[] = [];
  private markerLayers: any[] = [];
```

(c) Replace the body of `renderMarkers()` with:
```ts
  private async renderMarkers() {
    if (!this.map || this.routes().length === 0) return;
    const L = await loadLeaflet();
    await import('leaflet.markercluster');
    // markercluster augments window.L (which loadLeaflet returns), so L.markerClusterGroup exists here.
    const Lcluster = L as any;

    for (const layer of this.ghostLayers) this.map.removeLayer(layer);
    this.ghostLayers = [];

    // Re-renders (stale-recovery refetch) must replace markers, not stack them.
    for (const layer of this.markerLayers) this.map.removeLayer(layer);
    this.markerLayers = [];

    this.markerBySlug.clear();

    const cluster = Lcluster.markerClusterGroup({
      disableClusteringAtZoom: 8,
      showCoverageOnHover: false,
      spiderfyOnMaxZoom: true,
      maxClusterRadius: 60,
    });
    let usedCluster = false;

    for (const route of this.routes()) {
      if (route.summitLat == null || route.summitLon == null) continue;

      const spec = markerIconSpec(route);
      const icon = L.divIcon({
        className: spec.className,
        html: `<span class="dot ${spec.dotClass}"></span>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14],
      });

      if (!spec.interactive) {
        const ghost = L.marker([route.summitLat, route.summitLon], { icon, interactive: false });
        ghost.addTo(this.map);
        this.markerLayers.push(ghost);
        continue;
      }

      const marker = L.marker([route.summitLat, route.summitLon], { icon, title: route.mountain });
      marker.bindPopup(popupHtml(route), { className: 'peak-popup' });
      this.markerBySlug.set(route.slug, marker);

      if (route.rangeSlug === 'colorado-14ers') {
        cluster.addLayer(marker);
        usedCluster = true;
      } else {
        marker.addTo(this.map);
        this.markerLayers.push(marker);
      }
    }

    if (usedCluster) {
      cluster.addTo(this.map);
      this.markerLayers.push(cluster);
    }
  }
```
(The only behavioral changes from the current body: clearing/using `markerLayers` instead of pushing to `this.layers`, and the `spec`-driven icon + ghost branch.)

- [ ] **Step 4: Run the tests**

Run (from `frontend/`): `npm test`
Expected: PASS (new specs + all existing map-home specs).

- [ ] **Step 5: Commit**

```powershell
git add frontend/src/app/pages/map-home/map-home.ts frontend/src/app/pages/map-home/map-home.spec.ts
git commit -m @'
feat(frontend): ghost markers for null-grade routes; idempotent marker renders

renderMarkers now clears its own layer list before painting, so repeated
renders (needed for stale-recovery refetches) replace markers instead of
stacking duplicates.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 9: Frontend — silent stale-recovery refetch loop

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts`
- Test: `frontend/src/app/pages/map-home/map-home.spec.ts`

- [ ] **Step 1: Write the failing tests**

Append a new `describe` block inside the existing top-level `describe('MapHome', ...)` in `map-home.spec.ts`, and add the fixture helper + `vi` import. The HTTP test plumbing already exists at the top of that file and applies to nested describes — `beforeEach` configures TestBed with `provideHttpClient()` + `provideHttpClientTesting()` and assigns `httpMock = TestBed.inject(HttpTestingController)`, and `afterEach(() => httpMock.verify())` asserts no unexpected/pending requests (current `map-home.spec.ts:10-18`). Do not duplicate any of it. At the top of the file add:
```ts
import { vi } from 'vitest';
import { RouteSummary } from '../../models/route-conditions';
```

Add the fixture helper just below `flushAll` (every `RouteSummary` field present, per `.claude/rules/testing.md`):
```ts
  function summary(overrides: Partial<RouteSummary> = {}): RouteSummary {
    return {
      slug: 'mt-x',
      mountain: 'Mt X',
      routeName: 'SW Ridge',
      summitElevationFt: 12000,
      classDifficulty: '2',
      rangeSlug: 'r',
      rangeName: 'R',
      summitLat: 40,
      summitLon: -105,
      grade: 'A',
      overallScore: 92,
      drivers: [],
      updatedAt: new Date().toISOString(),
      isStale: false,
      consensus: null,
      ...overrides,
    };
  }
```

Then the new block:
```ts
  describe('stale recovery refetch', () => {
    // Only fake the timer APIs the loop uses; faking microtasks/rAF would
    // interfere with zoneless change detection.
    beforeEach(() => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }));
    afterEach(() => vi.useRealTimers());

    function init(routes: RouteSummary[]) {
      const fixture = TestBed.createComponent(MapHome);
      fixture.detectChanges();
      httpMock.expectOne('/api/ranges').flush([]);
      httpMock.expectOne('/api/routes/positions').flush([]);
      httpMock.expectOne('/api/routes').flush(routes);
      fixture.detectChanges();
      return fixture;
    }

    it('silently refetches 60s after a stale response and stops once fresh', () => {
      const fixture = init([summary({ isStale: true })]);
      expect(fixture.componentInstance.loading()).toBe(false);
      expect(fixture.nativeElement.querySelector('.loading-chip')).toBeNull();

      vi.advanceTimersByTime(60_000);
      httpMock.expectOne('/api/routes').flush([summary({ isStale: false })]);
      fixture.detectChanges();

      expect(fixture.componentInstance.loading()).toBe(false);
      expect(fixture.nativeElement.querySelector('.loading-chip')).toBeNull();

      // Fresh data — no further polling (httpMock.verify in afterEach enforces it).
      vi.advanceTimersByTime(180_000);
    });

    it('keeps polling while stale, capped at 5 attempts', () => {
      init([summary({ isStale: true })]);

      for (let attempt = 0; attempt < 5; attempt++) {
        vi.advanceTimersByTime(60_000);
        httpMock.expectOne('/api/routes').flush([summary({ isStale: true })]);
      }

      // Attempt budget exhausted — no sixth request.
      vi.advanceTimersByTime(180_000);
    });

    it('stays silent when a refetch fails, then keeps trying', () => {
      const fixture = init([summary({ isStale: true })]);

      vi.advanceTimersByTime(60_000);
      httpMock.expectOne('/api/routes').error(new ProgressEvent('boom'), { status: 500, statusText: 'boom' });
      fixture.detectChanges();

      expect(fixture.componentInstance.error()).toBeNull();
      expect(fixture.nativeElement.querySelector('.map-error-overlay')).toBeNull();

      vi.advanceTimersByTime(60_000);
      httpMock.expectOne('/api/routes').flush([summary({ isStale: false })]);
    });

    it('does not schedule a refetch when the first response is fresh', () => {
      init([summary({ isStale: false })]);
      vi.advanceTimersByTime(180_000);
      // httpMock.verify in afterEach asserts no request fired.
    });

    it('cancels the pending refetch when the component is destroyed', () => {
      const fixture = init([summary({ isStale: true })]);
      fixture.destroy();
      vi.advanceTimersByTime(180_000);
      // httpMock.verify in afterEach asserts no request fired after destroy.
    });
  });
```

- [ ] **Step 2: Run to verify they fail**

Run (from `frontend/`): `npm test`
Expected: FAIL — the 60s advance produces no `/api/routes` request (`expectOne` finds none).

- [ ] **Step 3: Implement the loop**

In `frontend/src/app/pages/map-home/map-home.ts`:

(a) Add constants and state to the class (near the other private fields):
```ts
  private static readonly STALE_REFETCH_DELAY_MS = 60_000;
  private static readonly STALE_REFETCH_MAX_ATTEMPTS = 5;
  private staleRefetchTimer: ReturnType<typeof setTimeout> | null = null;
  private staleRefetchAttempts = 0;
```

(b) In `fetchRoutes()` add `this.scheduleStaleRefetch();` immediately after `this.renderMarkers();`.

(c) In `retryRoutes()` add `this.staleRefetchAttempts = 0;` as the first line (a manual retry restores the attempt budget).

(d) In `ngOnDestroy()` add timer cleanup:
```ts
  ngOnDestroy() {
    if (this.staleRefetchTimer !== null) { clearTimeout(this.staleRefetchTimer); this.staleRefetchTimer = null; }
    if (this.map) { this.map.remove(); this.map = null; }
  }
```

(e) Add the two private methods after `retryRoutes()`:
```ts
  // Backend serves last-known data marked isStale while its warmer catches up
  // (typically <= one 10-min cycle). Quietly poll until fresh — no spinner,
  // no chip; grades are already on screen.
  private scheduleStaleRefetch() {
    if (this.staleRefetchTimer !== null) {
      clearTimeout(this.staleRefetchTimer);
      this.staleRefetchTimer = null;
    }
    if (!this.routes().some(r => r.isStale)) {
      this.staleRefetchAttempts = 0;
      return;
    }
    if (this.staleRefetchAttempts >= MapHome.STALE_REFETCH_MAX_ATTEMPTS) return;
    this.staleRefetchAttempts++;
    this.staleRefetchTimer = setTimeout(() => {
      this.staleRefetchTimer = null;
      this.refetchStaleRoutes();
    }, MapHome.STALE_REFETCH_DELAY_MS);
  }

  private refetchStaleRoutes() {
    this.routesSvc.list().subscribe({
      next: routes => {
        this.routes.set(routes);
        this.lastFetchedAt.set(Date.now());
        this.renderMarkers();
        this.scheduleStaleRefetch();
      },
      error: e => {
        console.warn('stale refetch failed', e);
        this.scheduleStaleRefetch();
      },
    });
  }
```

- [ ] **Step 4: Run the tests**

Run (from `frontend/`): `npm test`
Expected: PASS (all map-home specs, old and new).

- [ ] **Step 5: Commit**

```powershell
git add frontend/src/app/pages/map-home/map-home.ts frontend/src/app/pages/map-home/map-home.spec.ts
git commit -m @'
feat(frontend): silent stale-recovery refetch on the map

While /api/routes responses carry isStale summaries, quietly refetch every
60s (max 5 attempts, reset by manual retry) until the backend warmer has
fresh grades. No new UI; markers update in place.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 10: Full verification

- [ ] **Step 1: Backend — full test run** (API must be stopped)

Run: `dotnet test backend/RouteWeather.slnx`
Expected: PASS — both test projects (Core.Tests and API.Tests), zero failures.
(If `dotnet test` doesn't accept the `.slnx`, run the two csproj test commands from Tasks 5–6 individually.)

- [ ] **Step 2: Frontend — tests and production build**

Run (from `frontend/`): `npm test`
Expected: PASS.

Run (from `frontend/`): `npm run build`
Expected: success, no bundle-budget errors (map-home gained ~60 lines of TS; no SCSS changes, so the peak-detail.scss budget is untouched).

- [ ] **Step 3: Working tree check**

Run: `git status --porcelain`
Expected: empty (everything committed; nothing stray — plans/specs under `docs/superpowers/` are intentionally tracked).

- [ ] **Step 4: Acceptance criteria recap (manual, on dev preview after PR merge)**

These cannot be verified locally; verify after the `dev` PR deploys:
1. Fresh deploy → first page load shows graded (stale-marked) markers in ~2–3s, not 60–90s.
2. Fresh grades replace stale within one warm cycle (≤ ~10–13 min) with no user action.
3. Force-cold test: `flyctl machine restart` → reload page → stale grades, then fresh.
4. `/api/diagnostics` daily call counts remain in the usual range (warmer respects source TTLs).
5. `flyctl status` shows one machine running after idle (min_machines_running = 1).

---

## Post-plan notes for the executor

- **There is no server-side `/refresh` endpoint** (verified by grep — zero matches in `backend/**/*.cs`). The spec's "/refresh keeps ReadThrough" line is vacuous: the `/all` page's Refresh button simply re-GETs `/api/routes`, which after Task 5 is a cache-only read like any other GET. Nothing to migrate; do not invent an endpoint.
- **DailyCallCounter** needs no changes — fake sources in tests bypass it, and in production the warmer's fetches go through the same clients that already count.
- **Do not** add retry/timeout logic to the Pages Function proxy or touch `frontend/functions/api/[[path]].ts` — explicitly out of scope (spec Non-Goals).
- **Peak-detail page**: zero changes. If a test there breaks, something leaked outside the intended surface.
- When done, follow the repo's branch policy: PR targets `dev`, verification happens on the Pages preview, then `dev` → `main`.
