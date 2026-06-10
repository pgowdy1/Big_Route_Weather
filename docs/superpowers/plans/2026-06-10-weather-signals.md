# Weather Signals Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add CAPE-led thunderstorm, gust, and precip-amount grading factors plus display-only context signals (cloud/visibility, AQI, daylight, feels-like), swapping NWS to its raw gridpoints endpoint with cached point lookups.

**Architecture:** New nullable hourly/headline fields flow `clients → WeatherSnapshot → consensus blend → window slices → gated factors`. Field *presence* gates participation (no silent default scores). NWS moves from the derived hourly-forecast endpoint to raw gridpoints (one call per refresh instead of two, more fields). AQI is a new warmer-fetched cached source exempt from `isStale`. Daylight is a pure Core calculation.

**Tech Stack:** ASP.NET Core (.NET 10), xUnit, Angular 21 zoneless signals, Vitest + jsdom, SQLite source cache.

**Spec:** `docs/superpowers/specs/2026-06-10-weather-signals-design.md`

---

## As-built deltas from the spec (discovered during planning)

1. **TTLs are already tiered.** `appsettings.json` already has all OpenMeteo models at `CacheTtlMinutes: 180` and NWS at 60. Because all four models share ONE HTTP request (`OpenMeteoClient.FetchAllModelsAsync`), effective HTTP frequency = the *minimum* TTL across OpenMeteo sources. The spec's "HRRR TTL 1h" would re-inflate call volume 3× — **do not lower HRRR's TTL**. No TTL changes in this plan; Task 17 records this in the spec.
2. **`weather_code` is added to the OpenMeteo fetch** (spec mentioned it only as a fallback). OpenMeteo snapshots currently carry `ShortForecast: ""` and the blend baseline is the highest-weight source (HRRR at 1.2), so the detail page's Conditions column can render blank today. Mapping WMO codes to text fixes that and gives `SnowRelevance.IsSnowExpected` signal from non-NWS sources.
3. **`IForecastSource.FetchAsync` signature changes** to take a `ForecastLocation` record (RouteId + lat/lon + elevation) — NWS needs the RouteId to key its persisted gridpoint row, OpenMeteo needs elevation for lapse-rate downscaling.
4. **Gridpoint mapping persists in the existing cache table** as source name `"NWS-Grid"` with a 365-day expiry — no schema migration, reuses `ForecastCacheRepository`.

## Conventions for every task

- Backend tests: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj` and `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`. **Always use explicit csproj paths** — a running API process locks DLLs when building from `backend/`.
- **Never run `dotnet run` or `npm start`** — the user keeps both servers in their own terminals.
- Frontend tests: `npm test` from `frontend/` (Vitest, runs once and exits; NO `--watch=false` or Karma flags).
- Commit after each task with the message given in its final step. Stage files explicitly (`git add <paths>`), never `git add -A` (plans/specs slip in).

## File structure (all tasks)

| File | Action | Responsibility |
|---|---|---|
| `backend/RouteWeather.Core/Models/WeatherSnapshot.cs` | Modify | +6 nullable hourly fields, +3 nullable headline fields |
| `backend/RouteWeather.Core/Models/RouteConditions.cs` | Modify | +`AirQuality`, +`Daylight`, +`AirQualityFetchedAt`, per-source gust/CAPE |
| `backend/RouteWeather.Core/Models/AirQualitySnapshot.cs` | Create | AQI value record |
| `backend/RouteWeather.Core/Sources/IForecastSource.cs` | Modify | `ForecastLocation` param |
| `backend/RouteWeather.Core/Sources/ForecastLocation.cs` | Create | RouteId/lat/lon/elevation carrier |
| `backend/RouteWeather.Core/Sources/IAirQualitySource.cs` | Create | AQI source contract |
| `backend/RouteWeather.Core/Sources/ForecastFactors.cs` | Modify | +`Gust`, +`Cape` CV keys |
| `backend/RouteWeather.Core/Grading/ThunderstormFactor.cs` | Create | CAPE gate/score/caps |
| `backend/RouteWeather.Core/Grading/GustFactor.cs` | Create | Gust gate/score/caps |
| `backend/RouteWeather.Core/Grading/PrecipitationFactor.cs` | Modify | amount dimension, `min()` composition |
| `backend/RouteWeather.Core/Grading/{Wind,Temperature,RecentSnow,Snowpack}Factor.cs` | Modify | weight rebalance only |
| `backend/RouteWeather.Core/Grading/GradeCalculator.cs` | Modify | add gated factors + driver labels |
| `backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs` | Modify | aggregate new headline fields |
| `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs` | Modify | presence-blend new fields, +2 CV entries |
| `backend/RouteWeather.Core/Services/SolarCalculator.cs` | Create | NOAA sunrise/sunset |
| `backend/RouteWeather.API/Services/OpenMeteoClient.cs` | Modify | +6 hourly vars, elevation param, WMO text |
| `backend/RouteWeather.API/Services/WmoWeatherText.cs` | Create | WMO code → short text |
| `backend/RouteWeather.API/Services/NwsGridpointParser.cs` | Create | pure gridpoint JSON → WeatherSnapshot |
| `backend/RouteWeather.API/Services/NwsClient.cs` | Modify | gridpoints swap + cached point lookup |
| `backend/RouteWeather.API/Services/AirQualityClient.cs` | Create | Open-Meteo air-quality fetch |
| `backend/RouteWeather.API/Services/ConditionsAggregator.cs` | Modify | ForecastLocation, AQI arm, daylight |
| `backend/RouteWeather.API/Services/OpenMeteoSources.cs` | Modify | pass ForecastLocation through |
| `backend/RouteWeather.API/Program.cs` | Modify | AQI HttpClient + DI |
| `backend/RouteWeather.API/appsettings.json` | Modify | AirQuality source entry |
| `backend/RouteWeather.API/Controllers/RoutesController.cs` | Modify | DTO additions |
| `frontend/src/app/models/route-conditions.ts` | Modify | mirror contract changes |
| `frontend/src/app/components/route-card/route-card.{html,scss}` | Modify | AQI chip |
| `frontend/src/app/pages/peak-detail/peak-detail.{ts,html,scss}` | Modify | Sky & Air tiles, table columns, 24h collapse |
| `frontend/angular.json` | Modify (if needed) | style budget bump |

---

### Task 1: Core contracts — new fields and `ForecastLocation`

Mechanical contract change; no new behavior. Nullable-with-default fields keep every existing constructor call site and old cached JSON payloads (missing props → null) compiling/deserializing.

**Files:**
- Modify: `backend/RouteWeather.Core/Models/WeatherSnapshot.cs`
- Create: `backend/RouteWeather.Core/Sources/ForecastLocation.cs`
- Modify: `backend/RouteWeather.Core/Sources/IForecastSource.cs`
- Modify: `backend/RouteWeather.API/Services/OpenMeteoSources.cs`
- Modify: `backend/RouteWeather.API/Services/NwsClient.cs` (signature only)
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs` (call site only)
- Modify: `backend/RouteWeather.API.Tests/Fakes.cs` (forecast fake signature)

- [ ] **Step 1: Extend the weather records**

Replace the full contents of `backend/RouteWeather.Core/Models/WeatherSnapshot.cs`:

```csharp
namespace RouteWeather.Core.Models;

public record WeatherSnapshot(
    double WindMph,
    double TempF,
    int PrecipitationProbabilityPct,
    IReadOnlyList<HourlyForecast> Next48Hours,
    double? MaxGustMph = null,
    double? MaxCapeJkg = null,
    double? PrecipAmountIn = null
);

public record HourlyForecast(
    DateTimeOffset Time,
    double TempF,
    double WindMph,
    int PrecipitationProbabilityPct,
    string ShortForecast,
    double? GustMph = null,
    double? CapeJkg = null,
    double? PrecipitationIn = null,
    int? CloudCoverPct = null,
    double? VisibilityMiles = null,
    double? ApparentTempF = null
);
```

- [ ] **Step 2: Create `ForecastLocation` and change the source interface**

Create `backend/RouteWeather.Core/Sources/ForecastLocation.cs`:

```csharp
namespace RouteWeather.Core.Sources;

public record ForecastLocation(int RouteId, double Lat, double Lon, int SummitElevationFt);
```

Replace `backend/RouteWeather.Core/Sources/IForecastSource.cs`:

```csharp
using RouteWeather.Core.Models;

namespace RouteWeather.Core.Sources;

public interface IForecastSource
{
    string Name { get; }

    IReadOnlySet<string> ActiveFactors { get; }

    Task<WeatherSnapshot?> FetchAsync(ForecastLocation location, CancellationToken ct);
}
```

- [ ] **Step 3: Update implementations and the aggregator call site**

In `backend/RouteWeather.API/Services/OpenMeteoSources.cs`, change `OpenMeteoModelSource.FetchAsync`:

```csharp
    public async Task<WeatherSnapshot?> FetchAsync(ForecastLocation location, CancellationToken ct)
    {
        var all = await _client.FetchAllModelsAsync(location.Lat, location.Lon, location.SummitElevationFt, ct);
        return all.TryGetValue(Name, out var snap) ? snap : null;
    }
```

In `backend/RouteWeather.API/Services/OpenMeteoClient.cs`, change `FetchAllModelsAsync` and `FetchImpl` signatures to accept elevation (threaded but unused until Task 8):

```csharp
    public Task<IReadOnlyDictionary<string, WeatherSnapshot>> FetchAllModelsAsync(double lat, double lon, int summitElevationFt, CancellationToken ct = default)
    {
        var key = $"{lat:F4},{lon:F4}";
        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<IReadOnlyDictionary<string, WeatherSnapshot>>>(() => FetchImpl(lat, lon, summitElevationFt, ct)));
        var task = lazy.Value;
        task.ContinueWith(_ => _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<IReadOnlyDictionary<string, WeatherSnapshot>>>>(key, lazy)), TaskScheduler.Default);
        return task;
    }

    private async Task<IReadOnlyDictionary<string, WeatherSnapshot>> FetchImpl(double lat, double lon, int summitElevationFt, CancellationToken ct)
```

In `backend/RouteWeather.API/Services/NwsClient.cs`, change only the method signature (body keeps using the lat/lon locals for now):

```csharp
    public async Task<WeatherSnapshot?> FetchAsync(ForecastLocation location, CancellationToken ct = default)
    {
        var lat = location.Lat;
        var lon = location.Lon;
```

In `backend/RouteWeather.API/Services/ConditionsAggregator.cs`, in `FetchForecastAsync`, replace the fetch line:

```csharp
            fresh = await source.FetchAsync(
                new ForecastLocation(route.Id, route.SummitLat, route.SummitLon, route.SummitElevationFt), ct);
```

- [ ] **Step 4: Update the forecast fake in API tests**

In `backend/RouteWeather.API.Tests/Fakes.cs`, find the `IForecastSource` fake and change its `FetchAsync` to the new signature, e.g.:

```csharp
    public Task<WeatherSnapshot?> FetchAsync(ForecastLocation location, CancellationToken ct) =>
        Task.FromResult(Result);
```

(Match the fake's actual field/property names — only the parameter list changes.)

- [ ] **Step 5: Build and run all backend tests**

Run:
```bash
dotnet build backend/RouteWeather.Core/RouteWeather.Core.csproj
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```
Expected: build clean, all existing tests PASS (fields are additive defaults).

- [ ] **Step 6: Commit**

```bash
git add backend/RouteWeather.Core/Models/WeatherSnapshot.cs backend/RouteWeather.Core/Sources/ForecastLocation.cs backend/RouteWeather.Core/Sources/IForecastSource.cs backend/RouteWeather.API/Services/OpenMeteoSources.cs backend/RouteWeather.API/Services/OpenMeteoClient.cs backend/RouteWeather.API/Services/NwsClient.cs backend/RouteWeather.API/Services/ConditionsAggregator.cs backend/RouteWeather.API.Tests/Fakes.cs
git commit -m "feat(backend): nullable weather fields + ForecastLocation on source contract"
```

---

### Task 2: ThunderstormFactor

**Files:**
- Create: `backend/RouteWeather.Core/Grading/ThunderstormFactor.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/ThunderstormFactorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/RouteWeather.Core.Tests/Grading/ThunderstormFactorTests.cs`:

```csharp
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class ThunderstormFactorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(199, false)]
    [InlineData(200, true)]
    [InlineData(1500, true)]
    public void IsActive_gatesAtFloor(double cape, bool expected) =>
        Assert.Equal(expected, ThunderstormFactor.IsActive(cape));

    [Fact]
    public void Score_returns100_atOrBelowGoodThreshold()
    {
        Assert.Equal(100, ThunderstormFactor.Score(0));
        Assert.Equal(100, ThunderstormFactor.Score(200));
    }

    [Fact]
    public void Score_returns0_atOrAboveBadThreshold()
    {
        Assert.Equal(0, ThunderstormFactor.Score(2000));
        Assert.Equal(0, ThunderstormFactor.Score(4000));
    }

    [Fact]
    public void Score_isMonotonicallyDecreasing()
    {
        var prev = 101;
        for (var cape = 0; cape <= 3000; cape += 100)
        {
            var s = ThunderstormFactor.Score(cape);
            Assert.True(s <= prev, $"Score went up at {cape} J/kg: {prev} -> {s}");
            prev = s;
        }
    }

    [Theory]
    [InlineData(500, null)]
    [InlineData(999, null)]
    [InlineData(1000, Grade.C)]
    [InlineData(1999, Grade.C)]
    [InlineData(2000, Grade.D)]
    [InlineData(3500, Grade.D)]
    public void Cap_appliesAtCorrectThresholds(double cape, Grade? expected) =>
        Assert.Equal(expected, ThunderstormFactor.Cap(cape).Cap);

    [Fact]
    public void Cap_reasonMentionsCape() =>
        Assert.Contains("1200", ThunderstormFactor.Cap(1200).Reason);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter ThunderstormFactorTests`
Expected: FAIL to compile — `ThunderstormFactor` not defined.

- [ ] **Step 3: Implement**

Create `backend/RouteWeather.Core/Grading/ThunderstormFactor.cs`:

```csharp
using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class ThunderstormFactor
{
    public const double Weight = 0.20;

    // Calibrated for marine-influenced ranges where CAPE rarely exceeds ~1500 J/kg;
    // deliberately lower than Plains-storm conventions.
    public const double ActiveFloorJkg = 200;

    public static bool IsActive(double maxCapeJkg) => maxCapeJkg >= ActiveFloorJkg;

    public static int Score(double maxCapeJkg) =>
        ScoringMath.LinearBetween(maxCapeJkg, goodValue: 200, badValue: 2000);

    public static string Detail(double maxCapeJkg) =>
        $"Peak instability {maxCapeJkg:0} J/kg CAPE";

    public static (Grade? Cap, string Reason) Cap(double maxCapeJkg)
    {
        if (maxCapeJkg >= 2000) return (Grade.D, $"storm energy {maxCapeJkg:0} J/kg");
        if (maxCapeJkg >= 1000) return (Grade.C, $"storm energy {maxCapeJkg:0} J/kg");
        return (null, string.Empty);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter ThunderstormFactorTests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/ThunderstormFactor.cs backend/RouteWeather.Core.Tests/Grading/ThunderstormFactorTests.cs
git commit -m "feat(backend): ThunderstormFactor - CAPE-led gate, score, grade caps"
```

---

### Task 3: GustFactor

**Files:**
- Create: `backend/RouteWeather.Core/Grading/GustFactor.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/GustFactorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/RouteWeather.Core.Tests/Grading/GustFactorTests.cs`:

```csharp
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class GustFactorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(24.9, false)]
    [InlineData(25, true)]
    [InlineData(60, true)]
    public void IsActive_gatesAtFloor(double gust, bool expected) =>
        Assert.Equal(expected, GustFactor.IsActive(gust));

    [Fact]
    public void Score_returns100_atOrBelowGoodThreshold() =>
        Assert.Equal(100, GustFactor.Score(25));

    [Fact]
    public void Score_returns0_atOrAboveBadThreshold() =>
        Assert.Equal(0, GustFactor.Score(55));

    [Fact]
    public void Score_isMidwayBetweenThresholds() =>
        Assert.Equal(50, GustFactor.Score(40));

    [Theory]
    [InlineData(40, null)]
    [InlineData(45, null)]
    [InlineData(46, Grade.C)]
    [InlineData(55, Grade.C)]
    [InlineData(56, Grade.D)]
    [InlineData(70, Grade.D)]
    [InlineData(71, Grade.F)]
    public void Cap_appliesAtCorrectThresholds(double gust, Grade? expected) =>
        Assert.Equal(expected, GustFactor.Cap(gust).Cap);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter GustFactorTests`
Expected: FAIL to compile — `GustFactor` not defined.

- [ ] **Step 3: Implement**

Create `backend/RouteWeather.Core/Grading/GustFactor.cs`:

```csharp
using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class GustFactor
{
    public const double Weight = 0.10;

    // Below this, gusts are noise around sustained wind — an always-on twin of
    // WindFactor would permanently dilute every other weight.
    public const double ActiveFloorMph = 25;

    public static bool IsActive(double maxGustMph) => maxGustMph >= ActiveFloorMph;

    public static int Score(double maxGustMph) =>
        ScoringMath.LinearBetween(maxGustMph, goodValue: 25, badValue: 55);

    public static string Detail(double maxGustMph) =>
        $"Gusts to {maxGustMph:0} mph";

    public static (Grade? Cap, string Reason) Cap(double maxGustMph)
    {
        if (maxGustMph > 70) return (Grade.F, $"gusts to {maxGustMph:0} mph");
        if (maxGustMph > 55) return (Grade.D, $"gusts to {maxGustMph:0} mph");
        if (maxGustMph > 45) return (Grade.C, $"gusts to {maxGustMph:0} mph");
        return (null, string.Empty);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter GustFactorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/GustFactor.cs backend/RouteWeather.Core.Tests/Grading/GustFactorTests.cs
git commit -m "feat(backend): GustFactor - gated max-gust score and caps"
```

---

### Task 4: PrecipitationFactor amount upgrade

**Files:**
- Modify: `backend/RouteWeather.Core/Grading/PrecipitationFactor.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/PrecipitationFactorTests.cs` (add tests, keep existing)

- [ ] **Step 1: Write the failing tests**

Append to the class in `backend/RouteWeather.Core.Tests/Grading/PrecipitationFactorTests.cs`:

```csharp
    [Fact]
    public void Score_withNullAmount_equalsProbabilityScore() =>
        Assert.Equal(PrecipitationFactor.Score(40), PrecipitationFactor.Score(40, null, 24));

    [Fact]
    public void Score_withTraceAmount_belowEngageFloor_equalsProbabilityScore() =>
        Assert.Equal(PrecipitationFactor.Score(40), PrecipitationFactor.Score(40, 0.04, 24));

    [Fact]
    public void Score_takesWorseOfProbabilityAndAmount()
    {
        // 20% prob -> probScore 75. 0.5" in 24h (bad=1.0") -> amountScore 50. min() = 50.
        Assert.Equal(50, PrecipitationFactor.Score(20, 0.5, 24));
    }

    [Fact]
    public void Score_amountThresholdScalesByWindowHours()
    {
        // 0.5" over 12h (bad=0.5") -> amountScore 0; same 0.5" over 48h (bad=2.0") -> 75.
        Assert.Equal(0, PrecipitationFactor.Score(0, 0.5, 12));
        Assert.Equal(75, PrecipitationFactor.Score(0, 0.5, 48));
    }

    [Fact]
    public void Detail_mentionsAmount_onlyWhenEngaged()
    {
        Assert.Contains("0.5", PrecipitationFactor.Detail(40, 0.5));
        Assert.DoesNotContain("expected", PrecipitationFactor.Detail(40, null));
        Assert.Equal(PrecipitationFactor.Detail(40, null), PrecipitationFactor.Detail(40, 0.01));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter PrecipitationFactorTests`
Expected: FAIL to compile — no 3-arg `Score`, no 2-arg `Detail`.

- [ ] **Step 3: Implement**

Replace `backend/RouteWeather.Core/Grading/PrecipitationFactor.cs` (note: `Weight` stays `0.20` until Task 5's rebalance):

```csharp
using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class PrecipitationFactor
{
    public const double Weight = 0.20;

    // Trace forecasts below this don't drag the score.
    public const double AmountEngageFloorIn = 0.05;
    // The amount bad-threshold normalized to a 24h window; scales linearly by hours.
    public const double BadAmountInPer24h = 1.0;

    public static int Score(int precipProbabilityPct) =>
        ScoringMath.LinearBetween(precipProbabilityPct, goodValue: 0, badValue: 80);

    public static int Score(int precipProbabilityPct, double? amountIn, int windowHours)
    {
        var probScore = Score(precipProbabilityPct);
        if (amountIn is null || amountIn.Value < AmountEngageFloorIn || windowHours <= 0)
            return probScore;

        var badAmount = BadAmountInPer24h * windowHours / 24.0;
        var amountScore = ScoringMath.LinearBetween(amountIn.Value, goodValue: 0, badValue: badAmount);
        return Math.Min(probScore, amountScore);
    }

    public static string Detail(int precipProbabilityPct) =>
        $"{precipProbabilityPct}% chance of precip";

    public static string Detail(int precipProbabilityPct, double? amountIn) =>
        amountIn is not null && amountIn.Value >= AmountEngageFloorIn
            ? $"{precipProbabilityPct}% chance of precip, ~{amountIn.Value:0.0#}\" expected"
            : Detail(precipProbabilityPct);

    public static (Grade? Cap, string Reason) Cap(int precipProbabilityPct)
    {
        if (precipProbabilityPct > 90) return (Grade.F, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 70) return (Grade.D, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 50) return (Grade.C, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 30) return (Grade.B, $"{precipProbabilityPct}% chance of precip");
        return (null, string.Empty);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter PrecipitationFactorTests`
Expected: PASS (new and pre-existing).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/PrecipitationFactor.cs backend/RouteWeather.Core.Tests/Grading/PrecipitationFactorTests.cs
git commit -m "feat(backend): precip amount dimension with min() composition and window scaling"
```

---

### Task 5: GradeCalculator integration + weight rebalance

**Files:**
- Modify: `backend/RouteWeather.Core/Grading/GradeCalculator.cs`
- Modify: `backend/RouteWeather.Core/Grading/WindFactor.cs` (`Weight = 0.20`)
- Modify: `backend/RouteWeather.Core/Grading/TemperatureFactor.cs` (`Weight = 0.12`)
- Modify: `backend/RouteWeather.Core/Grading/PrecipitationFactor.cs` (`Weight = 0.18`)
- Modify: `backend/RouteWeather.Core/Grading/RecentSnowFactor.cs` (`Weight = 0.10`)
- Modify: `backend/RouteWeather.Core/Grading/SnowpackFactor.cs` (`Weight = 0.10`)
- Test: `backend/RouteWeather.Core.Tests/Grading/GradeCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `backend/RouteWeather.Core.Tests/Grading/GradeCalculatorTests.cs` (helper builds may exist in the file — reuse them if so; otherwise this snapshot builder works):

```csharp
    private static WeatherSnapshot Weather(
        double wind = 5, double temp = 50, int precip = 0,
        double? gust = null, double? cape = null, double? amountIn = null)
    {
        var hours = Enumerable.Range(0, 24)
            .Select(i => new HourlyForecast(
                DateTimeOffset.UtcNow.AddHours(i), temp, wind, precip, "Sunny"))
            .ToList();
        return new WeatherSnapshot(wind, temp, precip, hours,
            MaxGustMph: gust, MaxCapeJkg: cape, PrecipAmountIn: amountIn);
    }

    [Fact]
    public void NoCapeData_addsNoThunderstormFactor()
    {
        var result = GradeCalculator.Compute(Weather(), null);
        Assert.DoesNotContain(result.Factors, f => f.Name == "Thunderstorm");
    }

    [Fact]
    public void LowCape_thunderstormFactorPresentButInactive()
    {
        var result = GradeCalculator.Compute(Weather(cape: 100), null);
        var f = Assert.Single(result.Factors, x => x.Name == "Thunderstorm");
        Assert.False(f.IsActive);
    }

    [Fact]
    public void HighCape_capsGradeAndLeadsDrivers()
    {
        var result = GradeCalculator.Compute(Weather(cape: 2200), null);
        Assert.Equal(Grade.D, result.Grade);
        Assert.Equal("Storm risk", result.Drivers[0].Label);
        Assert.Equal("negative", result.Drivers[0].Severity);
    }

    [Fact]
    public void SmallGust_gustFactorPresentButInactive()
    {
        var result = GradeCalculator.Compute(Weather(gust: 15), null);
        var f = Assert.Single(result.Factors, x => x.Name == "Gusts");
        Assert.False(f.IsActive);
    }

    [Fact]
    public void StrongGust_activeAndCaps()
    {
        var result = GradeCalculator.Compute(Weather(gust: 60), null);
        var f = Assert.Single(result.Factors, x => x.Name == "Gusts");
        Assert.True(f.IsActive);
        Assert.Equal(Grade.D, result.Grade);
    }

    [Fact]
    public void PrecipAmount_dragsPrecipitationScore()
    {
        // 20% prob alone -> 75; with 1.0" in a 24h window -> amountScore 0 -> min 0.
        var result = GradeCalculator.Compute(Weather(precip: 20, amountIn: 1.0), null);
        var f = Assert.Single(result.Factors, x => x.Name == "Precipitation");
        Assert.Equal(0, f.Score);
        Assert.Contains("expected", f.Detail);
    }

    [Fact]
    public void Weights_matchRebalancedValues()
    {
        Assert.Equal(0.20, WindFactor.Weight);
        Assert.Equal(0.12, TemperatureFactor.Weight);
        Assert.Equal(0.18, PrecipitationFactor.Weight);
        Assert.Equal(0.20, ThunderstormFactor.Weight);
        Assert.Equal(0.10, GustFactor.Weight);
        Assert.Equal(0.10, RecentSnowFactor.Weight);
        Assert.Equal(0.10, SnowpackFactor.Weight);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter GradeCalculatorTests`
Expected: FAIL — no Thunderstorm/Gusts factors, old weights.

- [ ] **Step 3: Implement**

Change the `Weight` const in each factor file: Wind `0.20`, Temperature `0.12`, Precipitation `0.18`, RecentSnow `0.10`, Snowpack `0.10`.

In `GradeCalculator.Compute`, replace the existing Precipitation block and append the two new blocks inside `if (weather is not null) { ... }`:

```csharp
            var windowHours = weather.Next48Hours.Count;
            factors.Add(new FactorScore(
                "Precipitation",
                PrecipitationFactor.Score(weather.PrecipitationProbabilityPct, weather.PrecipAmountIn, windowHours),
                PrecipitationFactor.Weight,
                PrecipitationFactor.Detail(weather.PrecipitationProbabilityPct, weather.PrecipAmountIn)));
            AddCap(capCandidates, "Precipitation", PrecipitationFactor.Cap(weather.PrecipitationProbabilityPct));

            if (weather.MaxCapeJkg is double cape)
            {
                var capeActive = ThunderstormFactor.IsActive(cape);
                factors.Add(new FactorScore(
                    "Thunderstorm",
                    ThunderstormFactor.Score(cape),
                    ThunderstormFactor.Weight,
                    capeActive ? ThunderstormFactor.Detail(cape) : "No meaningful storm energy in window",
                    IsActive: capeActive));
                if (capeActive)
                    AddCap(capCandidates, "Thunderstorm", ThunderstormFactor.Cap(cape));
            }

            if (weather.MaxGustMph is double gust)
            {
                var gustActive = GustFactor.IsActive(gust);
                factors.Add(new FactorScore(
                    "Gusts",
                    GustFactor.Score(gust),
                    GustFactor.Weight,
                    gustActive ? GustFactor.Detail(gust) : "Gusts close to sustained wind",
                    IsActive: gustActive));
                if (gustActive)
                    AddCap(capCandidates, "Gusts", GustFactor.Cap(gust));
            }
```

Extend `LabelFor` with the new factor names (three-way on severity):

```csharp
    private static string LabelFor(FactorScore f, string severity) => f.Name switch
    {
        "Wind" => severity == "negative" ? "High winds" : "Calm winds",
        "Temperature" => severity == "negative" ? "Extreme temps" : "Comfortable temps",
        "Precipitation" => severity == "negative" ? "Wet weather" : "Clear skies",
        "Thunderstorm" => severity == "negative" ? "Storm risk" : severity == "neutral" ? "Some instability" : "Low storm risk",
        "Gusts" => severity == "negative" ? "Strong gusts" : severity == "neutral" ? "Gusty" : "Manageable gusts",
        "Recent snow" => severity == "negative" ? "Fresh snow on rock" : "Dry rock",
        "Snowpack" => severity == "negative" ? "Out-of-season snowpack" : "Typical snowpack",
        _ => f.Name,
    };
```

- [ ] **Step 4: Run the full Core test suite — fix weight-dependent assertions**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj`
Expected: new tests PASS. Pre-existing `GradeCalculatorTests` / `WindowGradeCalculatorTests` that assert exact `OverallScore` values will shift because weights changed (e.g., old `wind .25/temp .15/precip .20`). Recompute each expected value by hand using the new weights and update the assertion — do NOT loosen assertions to ranges.

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading backend/RouteWeather.Core.Tests/Grading
git commit -m "feat(backend): gated Thunderstorm + Gusts factors in GradeCalculator; weight rebalance"
```

---

### Task 6: WindowGradeCalculator headline aggregation

**Files:**
- Modify: `backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/WindowGradeCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `WindowGradeCalculatorTests.cs`:

```csharp
    [Fact]
    public void Aggregate_computesNewHeadlines_fromSlice()
    {
        var hours = new List<HourlyForecast>();
        for (var i = 0; i < 12; i++)
        {
            hours.Add(new HourlyForecast(
                DateTimeOffset.UtcNow.AddHours(i), 50, 5, 0, "Sunny",
                GustMph: 10 + i,          // max 21
                CapeJkg: 100 * i,         // max 1100 in slice
                PrecipitationIn: 0.1));   // sum 1.2
        }
        var weather = new WeatherSnapshot(5, 50, 0, hours);

        var grades = WindowGradeCalculator.Compute(weather, null);
        var factors12 = grades.Next12h.Factors;

        Assert.Contains(factors12, f => f.Name == "Thunderstorm" && f.IsActive);
        Assert.Contains(factors12, f => f.Name == "Gusts" && !f.IsActive); // 21 < 25
        var precip = Assert.Single(factors12, f => f.Name == "Precipitation");
        Assert.Equal(0, precip.Score); // 1.2" >= 0.5" bad threshold for 12h
    }

    [Fact]
    public void Aggregate_allNullNewFields_addsNoNewFactors()
    {
        var hours = Enumerable.Range(0, 12)
            .Select(i => new HourlyForecast(DateTimeOffset.UtcNow.AddHours(i), 50, 5, 0, "Sunny"))
            .ToList();
        var weather = new WeatherSnapshot(5, 50, 0, hours);

        var grades = WindowGradeCalculator.Compute(weather, null);

        Assert.DoesNotContain(grades.Next12h.Factors, f => f.Name == "Thunderstorm");
        Assert.DoesNotContain(grades.Next12h.Factors, f => f.Name == "Gusts");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter WindowGradeCalculatorTests`
Expected: FAIL — `Aggregate` drops the new fields, so no Thunderstorm factor appears.

- [ ] **Step 3: Implement**

Replace `Aggregate` in `WindowGradeCalculator.cs`:

```csharp
    private static WeatherSnapshot Aggregate(IReadOnlyList<HourlyForecast> slice)
    {
        var gusts = slice.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var capes = slice.Where(h => h.CapeJkg.HasValue).Select(h => h.CapeJkg!.Value).ToList();
        var amounts = slice.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new(
            WindMph: slice.Max(h => h.WindMph),
            TempF: slice.Min(h => h.TempF),
            PrecipitationProbabilityPct: slice.Max(h => h.PrecipitationProbabilityPct),
            Next48Hours: slice,
            MaxGustMph: gusts.Count == 0 ? null : gusts.Max(),
            MaxCapeJkg: capes.Count == 0 ? null : capes.Max(),
            PrecipAmountIn: amounts.Count == 0 ? null : amounts.Sum());
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter WindowGradeCalculatorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs backend/RouteWeather.Core.Tests/Grading/WindowGradeCalculatorTests.cs
git commit -m "feat(backend): per-window gust/CAPE/precip-amount aggregation"
```

---

### Task 7: ConsensusCalculator presence-blending + new CV entries

New fields blend across sources by **value presence**, not `ActiveFactors` membership (a source that doesn't report CAPE simply contributes null). CV entries for Gusts/Thunderstorm appear only when ≥2 sources report values.

**Files:**
- Modify: `backend/RouteWeather.Core/Sources/ForecastFactors.cs`
- Modify: `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `ConsensusCalculatorTests.cs` (reuse the file's existing snapshot/input helpers for the base fields; the key is constructing snapshots with/without headline values):

```csharp
    private static ConsensusInput InputWith(
        string name, double wind, double temp, int precip,
        double? gust = null, double? cape = null, double weight = 1.0)
    {
        var hours = new List<HourlyForecast>
        {
            new(DateTimeOffset.Parse("2026-06-10T12:00:00Z"), temp, wind, precip, "Sunny",
                GustMph: gust, CapeJkg: cape),
        };
        var snap = new WeatherSnapshot(wind, temp, precip, hours,
            MaxGustMph: gust, MaxCapeJkg: cape);
        return new ConsensusInput(
            new SourceSnapshot(name, snap, DateTimeOffset.UtcNow, ForecastFactors.All), weight);
    }

    [Fact]
    public void Blend_newHeadlines_usePresenceWeightedMean()
    {
        var calc = new ConsensusCalculator();
        var result = calc.Compute(new[]
        {
            InputWith("A", 10, 50, 0, gust: 30, cape: 400),
            InputWith("B", 10, 50, 0, gust: 40, cape: 800),
            InputWith("C", 10, 50, 0, gust: null, cape: null), // contributes nothing to new fields
        }, 3);

        Assert.NotNull(result.Blended);
        Assert.Equal(35, result.Blended!.MaxGustMph!.Value, 0);
        Assert.Equal(600, result.Blended.MaxCapeJkg!.Value, 0);
    }

    [Fact]
    public void Blend_allNullNewFields_staysNull()
    {
        var calc = new ConsensusCalculator();
        var result = calc.Compute(new[]
        {
            InputWith("A", 10, 50, 0),
            InputWith("B", 12, 50, 0),
        }, 2);

        Assert.Null(result.Blended!.MaxGustMph);
        Assert.Null(result.Blended.MaxCapeJkg);
        Assert.Null(result.Blended.PrecipAmountIn);
    }

    [Fact]
    public void Cv_includesGustEntry_onlyWithTwoReporters()
    {
        var calc = new ConsensusCalculator();

        var one = calc.Compute(new[]
        {
            InputWith("A", 10, 50, 0, gust: 50),
            InputWith("B", 10, 50, 0),
        }, 2);
        Assert.False(one.Consensus!.CoefficientOfVariationByFactor.ContainsKey(ForecastFactors.Gust));

        var two = calc.Compute(new[]
        {
            InputWith("A", 10, 50, 0, gust: 20),
            InputWith("B", 10, 50, 0, gust: 60), // spread 40 > 8 mph floor
        }, 2);
        Assert.True(two.Consensus!.CoefficientOfVariationByFactor[ForecastFactors.Gust] > 0);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter ConsensusCalculatorTests`
Expected: FAIL — blended headlines null, no Gust CV key.

- [ ] **Step 3: Implement**

In `ForecastFactors.cs`, add (names match the `FactorScore` display names so `WorstFactor` aligns with UI factor names):

```csharp
    public const string Gust = "Gusts";
    public const string Cape = "Thunderstorm";
```

In `ConsensusCalculator.cs`:

(a) Add spread-floor constants next to the existing ones:

```csharp
    private const double GustSpreadFloorMph = 8.0;
    private const double CapeSpreadFloorJkg = 200.0;
```

(b) In `BlendSnapshots`, compute headlines from the blended hourly series (consistent with `WindowGradeCalculator.Aggregate`) — replace the final `return`:

```csharp
        var blendedGusts = blendedHours.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var blendedCapes = blendedHours.Where(h => h.CapeJkg.HasValue).Select(h => h.CapeJkg!.Value).ToList();
        var blendedAmounts = blendedHours.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: Math.Round(wind),
            TempF: Math.Round(temp),
            PrecipitationProbabilityPct: (int)Math.Round(precip),
            Next48Hours: blendedHours,
            MaxGustMph: blendedGusts.Count == 0 ? null : blendedGusts.Max(),
            MaxCapeJkg: blendedCapes.Count == 0 ? null : blendedCapes.Max(),
            PrecipAmountIn: blendedAmounts.Count == 0 ? null : blendedAmounts.Sum());
```

(c) In `BlendHourly`, blend the new per-hour fields by presence across ALL inputs — inside the loop, before `result.Add`, and extend the record construction:

```csharp
            var gustH = HourlyMeanNullable(inputs, hour, h => h.GustMph);
            var capeH = HourlyMeanNullable(inputs, hour, h => h.CapeJkg);
            var amountH = HourlyMeanNullable(inputs, hour, h => h.PrecipitationIn);
            var cloudH = HourlyMeanNullable(inputs, hour, h => h.CloudCoverPct.HasValue ? h.CloudCoverPct.Value : null);
            var visH = HourlyMeanNullable(inputs, hour, h => h.VisibilityMiles);
            var apparentH = HourlyMeanNullable(inputs, hour, h => h.ApparentTempF);

            result.Add(new HourlyForecast(
                Time: hour,
                TempF: Math.Round(temp),
                WindMph: Math.Round(wind),
                PrecipitationProbabilityPct: (int)Math.Round(precip),
                ShortForecast: baseline[i].ShortForecast,
                GustMph: gustH is null ? null : Math.Round(gustH.Value),
                CapeJkg: capeH is null ? null : Math.Round(capeH.Value),
                PrecipitationIn: amountH,
                CloudCoverPct: cloudH is null ? null : (int)Math.Round(cloudH.Value),
                VisibilityMiles: visH,
                ApparentTempF: apparentH is null ? null : Math.Round(apparentH.Value)));
```

(d) Add the nullable hourly mean helper next to `HourlyMean`:

```csharp
    private static double? HourlyMeanNullable(
        IReadOnlyList<ConsensusInput> inputs,
        DateTimeOffset target,
        Func<HourlyForecast, double?> select)
    {
        var sum = 0.0;
        var weight = 0.0;
        foreach (var input in inputs)
        {
            var match = FindNearestHour(input.Source.Snapshot.Next48Hours, target);
            var v = match is null ? null : select(match);
            if (v is null) continue;
            sum += v.Value * input.Weight;
            weight += input.Weight;
        }
        return weight <= 0 ? null : sum / weight;
    }
```

(e) Restructure `ComputeCvByFactor` so entries exist only with ≥2 reporters (legacy factors keep `ActiveFactors` gating; new factors gate on presence), and simplify `ResolveLevel` accordingly:

```csharp
    private static IReadOnlyDictionary<string, double> ComputeCvByFactor(IReadOnlyList<ConsensusInput> inputs)
    {
        var cv = new Dictionary<string, double>();
        AddCv(cv, ForecastFactors.Wind, Active(inputs, ForecastFactors.Wind).Select(i => i.Source.Snapshot.WindMph), WindSpreadFloorMph);
        AddCv(cv, ForecastFactors.Temperature, Active(inputs, ForecastFactors.Temperature).Select(i => (double)i.Source.Snapshot.TempF), TempSpreadFloorF);
        AddCv(cv, ForecastFactors.Precipitation, Active(inputs, ForecastFactors.Precipitation).Select(i => (double)i.Source.Snapshot.PrecipitationProbabilityPct), PrecipSpreadFloorPct);
        AddCv(cv, ForecastFactors.Gust, inputs.Where(i => i.Source.Snapshot.MaxGustMph.HasValue).Select(i => i.Source.Snapshot.MaxGustMph!.Value), GustSpreadFloorMph);
        AddCv(cv, ForecastFactors.Cape, inputs.Where(i => i.Source.Snapshot.MaxCapeJkg.HasValue).Select(i => i.Source.Snapshot.MaxCapeJkg!.Value), CapeSpreadFloorJkg);
        return cv;
    }

    private static void AddCv(Dictionary<string, double> cv, string factor, IEnumerable<double> values, double floor)
    {
        var list = values.ToList();
        if (list.Count < 2) return;
        cv[factor] = Cv(list, floor);
    }
```

In `ResolveLevel`, replace the `factorsWithEnoughSources` computation with:

```csharp
        var factorsWithEnoughSources = cv.Keys.ToList();
```

(The ≥2 filter moved into `AddCv`; behavior for the legacy three factors is unchanged.)

- [ ] **Step 4: Run the Core suite — fix CV-shape assertions**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj`
Expected: new tests PASS. Any pre-existing `ConsensusCalculatorTests` asserting the CV dictionary always has exactly 3 keys (or contains a key when only 1 source reports) must be updated to the new presence rule.

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Sources/ForecastFactors.cs backend/RouteWeather.Core/Grading/ConsensusCalculator.cs backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs
git commit -m "feat(backend): presence-gated consensus blending and CV for gusts and CAPE"
```

---

### Task 8: OpenMeteoClient — new variables, elevation, WMO conditions text

**Files:**
- Create: `backend/RouteWeather.API/Services/WmoWeatherText.cs`
- Modify: `backend/RouteWeather.API/Services/OpenMeteoClient.cs`
- Test: `backend/RouteWeather.API.Tests/WmoWeatherTextTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/RouteWeather.API.Tests/WmoWeatherTextTests.cs`:

```csharp
using RouteWeather.API.Services;
using Xunit;

namespace RouteWeather.API.Tests;

public class WmoWeatherTextTests
{
    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(3, "Overcast")]
    [InlineData(61, "Rain")]
    [InlineData(75, "Snow")]
    [InlineData(85, "Snow showers")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(-1, "")]
    [InlineData(42, "")]
    public void For_mapsKnownCodes(int code, string expected) =>
        Assert.Equal(expected, WmoWeatherText.For(code));

    [Fact]
    public void SnowCodes_containSnow_forSnowRelevanceMatching()
    {
        foreach (var code in new[] { 71, 73, 75, 77, 85, 86 })
            Assert.Contains("snow", WmoWeatherText.For(code), StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter WmoWeatherTextTests`
Expected: FAIL to compile — `WmoWeatherText` not defined.

- [ ] **Step 3: Implement the code map**

Create `backend/RouteWeather.API/Services/WmoWeatherText.cs`:

```csharp
namespace RouteWeather.API.Services;

/// <summary>WMO weather interpretation codes (Open-Meteo `weather_code`) to short display text.</summary>
public static class WmoWeatherText
{
    public static string For(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => string.Empty,
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter WmoWeatherTextTests`
Expected: PASS.

- [ ] **Step 5: Widen the OpenMeteo fetch**

In `OpenMeteoClient.cs`:

(a) In `FetchPrimaryAsync` (gains the elevation param), request the new variables:

```csharp
    private async Task<OpenMeteoHourly?> FetchPrimaryAsync(double lat, double lon, int summitElevationFt, CancellationToken ct)
    {
        var modelsParam = string.Join(',', Models.Select(m => m.ModelKey));
        var url = BuildUrl(lat, lon, summitElevationFt,
            "temperature_2m,precipitation,wind_speed_10m,wind_gusts_10m,cape,cloud_cover,visibility,apparent_temperature,weather_code",
            modelsParam);
```

(b) `BuildUrl` gains elevation (Open-Meteo wants meters) and the precip unit:

```csharp
    private static string BuildUrl(double lat, double lon, int summitElevationFt, string hourly, string models) =>
        "v1/forecast" +
        $"?latitude={lat.ToString("F4", CultureInfo.InvariantCulture)}" +
        $"&longitude={lon.ToString("F4", CultureInfo.InvariantCulture)}" +
        $"&elevation={(summitElevationFt * 0.3048).ToString("F0", CultureInfo.InvariantCulture)}" +
        $"&hourly={hourly}" +
        $"&models={models}" +
        "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch" +
        "&forecast_days=2&timezone=UTC";
```

Update `FetchImpl` to pass `summitElevationFt` to both `FetchPrimaryAsync` and `FetchGfsProbabilityAsync` (give the probability fetch the same new parameter).

(c) In `BuildSnapshot`, read the optional series and populate the new fields. Replace the body after the existing temp/wind series lookups:

```csharp
        hourly.Series.TryGetValue($"wind_gusts_10m_{modelKey}", out var gustSeries);
        hourly.Series.TryGetValue($"cape_{modelKey}", out var capeSeries);
        hourly.Series.TryGetValue($"precipitation_{modelKey}", out var precipSeries);
        hourly.Series.TryGetValue($"cloud_cover_{modelKey}", out var cloudSeries);
        hourly.Series.TryGetValue($"visibility_{modelKey}", out var visSeries);
        hourly.Series.TryGetValue($"apparent_temperature_{modelKey}", out var apparentSeries);
        hourly.Series.TryGetValue($"weather_code_{modelKey}", out var codeSeries);

        var count = Math.Min(48, Math.Min(times.Count, tempSeries.Count));
        if (count == 0) return null;

        var hourly48 = new List<HourlyForecast>(count);
        for (var i = 0; i < count; i++)
        {
            var t = tempSeries[i];
            if (!t.HasValue) continue;
            var w = windSeries is not null && i < windSeries.Count ? (windSeries[i] ?? 0.0) : 0.0;
            var code = At(codeSeries, i);
            var visMeters = At(visSeries, i);

            hourly48.Add(new HourlyForecast(
                Time: times[i],
                TempF: t.Value,
                WindMph: w,
                PrecipitationProbabilityPct: 0,
                ShortForecast: code is null ? string.Empty : WmoWeatherText.For((int)code.Value),
                GustMph: At(gustSeries, i),
                CapeJkg: At(capeSeries, i),
                PrecipitationIn: At(precipSeries, i),
                CloudCoverPct: At(cloudSeries, i) is double c ? (int)Math.Round(c) : null,
                VisibilityMiles: visMeters is null ? null : Math.Round(visMeters.Value / 1609.34, 1),
                ApparentTempF: At(apparentSeries, i)));
        }

        if (hourly48.Count == 0) return null;

        var gusts = hourly48.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var capes = hourly48.Where(h => h.CapeJkg.HasValue).Select(h => h.CapeJkg!.Value).ToList();
        var amounts = hourly48.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: hourly48.Max(h => h.WindMph),
            TempF: hourly48.Min(h => h.TempF),
            PrecipitationProbabilityPct: 0,
            Next48Hours: hourly48,
            MaxGustMph: gusts.Count == 0 ? null : gusts.Max(),
            MaxCapeJkg: capes.Count == 0 ? null : capes.Max(),
            PrecipAmountIn: amounts.Count == 0 ? null : amounts.Sum());
```

Add the small accessor helper to the class:

```csharp
    private static double? At(List<double?>? series, int i) =>
        series is not null && i < series.Count ? series[i] : null;
```

(d) `OverlayGfsProbability` is unchanged (the `with` expression preserves the new fields).

- [ ] **Step 6: Build and run all backend tests**

Run:
```bash
dotnet build backend/RouteWeather.API/RouteWeather.API.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```
Expected: clean build, tests PASS. (If the running API locks the DLLs, build the test project path directly instead.)

- [ ] **Step 7: Commit**

```bash
git add backend/RouteWeather.API/Services/WmoWeatherText.cs backend/RouteWeather.API/Services/OpenMeteoClient.cs backend/RouteWeather.API.Tests/WmoWeatherTextTests.cs
git commit -m "feat(backend): widen OpenMeteo fetch - gusts, CAPE, precip, sky, elevation downscaling, WMO text"
```

---

### Task 9: NWS gridpoint parser (pure)

The raw gridpoints payload is layered sparse intervals: each layer has a `uom` and `values: [{validTime: "<ISO start>/<ISO-8601 duration>", value}]`. The parser expands intervals to hours, converts units, and builds a `WeatherSnapshot`. `quantitativePrecipitation` is an interval *total* — divide evenly across the interval's hours. Hours without a temperature value are excluded.

**Files:**
- Create: `backend/RouteWeather.API/Services/NwsGridpointParser.cs`
- Test: `backend/RouteWeather.API.Tests/NwsGridpointParserTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/RouteWeather.API.Tests/NwsGridpointParserTests.cs`:

```csharp
using System.Text.Json;
using RouteWeather.API.Services;
using Xunit;

namespace RouteWeather.API.Tests;

public class NwsGridpointParserTests
{
    private const string SampleJson = """
    {
      "properties": {
        "temperature": { "uom": "wmoUnit:degC", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT2H", "value": 10.0 },
          { "validTime": "2026-06-10T14:00:00+00:00/PT1H", "value": 12.0 } ] },
        "windSpeed": { "uom": "wmoUnit:km_h-1", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 16.0 } ] },
        "windGust": { "uom": "wmoUnit:km_h-1", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 48.0 } ] },
        "probabilityOfPrecipitation": { "uom": "wmoUnit:percent", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 40 } ] },
        "quantitativePrecipitation": { "uom": "wmoUnit:mm", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT2H", "value": 5.08 } ] },
        "skyCover": { "uom": "wmoUnit:percent", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 75 } ] },
        "visibility": { "uom": "wmoUnit:m", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 16093.44 } ] },
        "apparentTemperature": { "uom": "wmoUnit:degC", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 7.0 } ] },
        "weather": { "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H",
            "value": [ { "coverage": "likely", "weather": "snow_showers", "intensity": "light" } ] } ] }
      }
    }
    """;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-10T12:00:00+00:00");

    [Fact]
    public void Parse_includesOnlyHoursWithTemperature()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        var snap = NwsGridpointParser.Parse(doc.RootElement, Now);

        Assert.NotNull(snap);
        Assert.Equal(3, snap!.Next48Hours.Count); // 12:00, 13:00 (PT2H), 14:00 (PT1H)
    }

    [Fact]
    public void Parse_convertsUnits()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        var h0 = NwsGridpointParser.Parse(doc.RootElement, Now)!.Next48Hours[0];

        Assert.Equal(50.0, h0.TempF, 1);                 // 10 C
        Assert.Equal(9.9, h0.WindMph, 1);                // 16 km/h
        Assert.Equal(29.8, h0.GustMph!.Value, 1);        // 48 km/h
        Assert.Equal(40, h0.PrecipitationProbabilityPct);
        Assert.Equal(0.1, h0.PrecipitationIn!.Value, 2); // 5.08 mm over PT2H = 0.2" / 2h
        Assert.Equal(75, h0.CloudCoverPct);
        Assert.Equal(10.0, h0.VisibilityMiles!.Value, 1);
        Assert.Equal(44.6, h0.ApparentTempF!.Value, 1);  // 7 C
    }

    [Fact]
    public void Parse_buildsConditionsTextFromWeatherLayer()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        var h0 = NwsGridpointParser.Parse(doc.RootElement, Now)!.Next48Hours[0];
        Assert.Equal("Snow showers", h0.ShortForecast);
    }

    [Fact]
    public void Parse_computesHeadlines()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        var snap = NwsGridpointParser.Parse(doc.RootElement, Now)!;

        Assert.Equal(50.0, snap.TempF, 1);               // min temp
        Assert.Equal(29.8, snap.MaxGustMph!.Value, 1);
        Assert.Equal(0.2, snap.PrecipAmountIn!.Value, 2); // both QPF hours included
        Assert.Null(snap.MaxCapeJkg);                     // NWS has no CAPE
    }

    [Fact]
    public void Parse_missingTemperatureLayer_returnsNull()
    {
        using var doc = JsonDocument.Parse("""{ "properties": { } }""");
        Assert.Null(NwsGridpointParser.Parse(doc.RootElement, Now));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter NwsGridpointParserTests`
Expected: FAIL to compile — `NwsGridpointParser` not defined.

- [ ] **Step 3: Implement**

Create `backend/RouteWeather.API/Services/NwsGridpointParser.cs`:

```csharp
using System.Text.Json;
using System.Xml;
using RouteWeather.Core.Models;

namespace RouteWeather.API.Services;

/// <summary>
/// Parses the raw NWS gridpoints payload (layered sparse intervals with
/// "validTime": "start/ISO8601-duration") into a WeatherSnapshot. Hours without
/// a temperature value are excluded; quantitativePrecipitation interval totals
/// are spread evenly across their hours.
/// </summary>
public static class NwsGridpointParser
{
    public static WeatherSnapshot? Parse(JsonElement root, DateTimeOffset nowUtc)
    {
        if (!root.TryGetProperty("properties", out var props)) return null;

        var temp = Layer(props, "temperature", spread: false);
        if (temp.Count == 0) return null;

        var wind = Layer(props, "windSpeed", spread: false);
        var gust = Layer(props, "windGust", spread: false);
        var pop = Layer(props, "probabilityOfPrecipitation", spread: false);
        var qpf = Layer(props, "quantitativePrecipitation", spread: true);
        var sky = Layer(props, "skyCover", spread: false);
        var vis = Layer(props, "visibility", spread: false);
        var apparent = Layer(props, "apparentTemperature", spread: false);
        var weather = WeatherTextLayer(props);

        var start = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, TimeSpan.Zero);
        var hours = new List<HourlyForecast>(48);
        for (var i = 0; i < 48; i++)
        {
            var t = start.AddHours(i);
            if (!temp.Values.TryGetValue(t, out var tempVal)) continue;

            hours.Add(new HourlyForecast(
                Time: t,
                TempF: Convert(tempVal, temp.Uom),
                WindMph: wind.Values.TryGetValue(t, out var w) ? Convert(w, wind.Uom) : 0,
                PrecipitationProbabilityPct: pop.Values.TryGetValue(t, out var p) ? (int)Math.Round(p) : 0,
                ShortForecast: weather.TryGetValue(t, out var text) ? text : string.Empty,
                GustMph: gust.Values.TryGetValue(t, out var g) ? Convert(g, gust.Uom) : null,
                CapeJkg: null,
                PrecipitationIn: qpf.Values.TryGetValue(t, out var q) ? Convert(q, qpf.Uom) : null,
                CloudCoverPct: sky.Values.TryGetValue(t, out var s) ? (int)Math.Round(s) : null,
                VisibilityMiles: vis.Values.TryGetValue(t, out var v) ? Math.Round(Convert(v, vis.Uom), 1) : null,
                ApparentTempF: apparent.Values.TryGetValue(t, out var a) ? Convert(a, apparent.Uom) : null));
        }

        if (hours.Count == 0) return null;

        var gusts = hours.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var amounts = hours.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: hours.Max(h => h.WindMph),
            TempF: hours.Min(h => h.TempF),
            PrecipitationProbabilityPct: hours.Max(h => h.PrecipitationProbabilityPct),
            Next48Hours: hours,
            MaxGustMph: gusts.Count == 0 ? null : gusts.Max(),
            MaxCapeJkg: null,
            PrecipAmountIn: amounts.Count == 0 ? null : amounts.Sum());
    }

    private sealed record LayerData(string Uom, Dictionary<DateTimeOffset, double> Values)
    {
        public int Count => Values.Count;
    }

    private static LayerData Layer(JsonElement props, string name, bool spread)
    {
        var values = new Dictionary<DateTimeOffset, double>();
        if (!props.TryGetProperty(name, out var layer))
            return new LayerData(string.Empty, values);

        var uom = layer.TryGetProperty("uom", out var u) ? u.GetString() ?? string.Empty : string.Empty;
        if (!layer.TryGetProperty("values", out var arr)) return new LayerData(uom, values);

        foreach (var entry in arr.EnumerateArray())
        {
            if (!entry.TryGetProperty("value", out var valEl) || valEl.ValueKind != JsonValueKind.Number) continue;
            var (intervalStart, durationHours) = ParseValidTime(entry);
            if (intervalStart is null || durationHours <= 0) continue;

            var value = valEl.GetDouble();
            var perHour = spread ? value / durationHours : value;
            for (var h = 0; h < durationHours; h++)
                values[intervalStart.Value.AddHours(h)] = perHour;
        }
        return new LayerData(uom, values);
    }

    private static Dictionary<DateTimeOffset, string> WeatherTextLayer(JsonElement props)
    {
        var result = new Dictionary<DateTimeOffset, string>();
        if (!props.TryGetProperty("weather", out var layer) || !layer.TryGetProperty("values", out var arr))
            return result;

        foreach (var entry in arr.EnumerateArray())
        {
            var (intervalStart, durationHours) = ParseValidTime(entry);
            if (intervalStart is null || durationHours <= 0) continue;
            if (!entry.TryGetProperty("value", out var phenomena) || phenomena.ValueKind != JsonValueKind.Array) continue;

            var text = string.Empty;
            foreach (var ph in phenomena.EnumerateArray())
            {
                if (ph.TryGetProperty("weather", out var w) && w.ValueKind == JsonValueKind.String)
                {
                    var raw = w.GetString();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        var pretty = raw.Replace('_', ' ');
                        text = char.ToUpperInvariant(pretty[0]) + pretty[1..];
                        break;
                    }
                }
            }
            if (text.Length == 0) continue;
            for (var h = 0; h < durationHours; h++)
                result[intervalStart.Value.AddHours(h)] = text;
        }
        return result;
    }

    private static (DateTimeOffset? Start, int Hours) ParseValidTime(JsonElement entry)
    {
        if (!entry.TryGetProperty("validTime", out var vt) || vt.ValueKind != JsonValueKind.String)
            return (null, 0);
        var parts = (vt.GetString() ?? string.Empty).Split('/');
        if (parts.Length != 2) return (null, 0);
        if (!DateTimeOffset.TryParse(parts[0], out var start)) return (null, 0);
        try
        {
            var duration = XmlConvert.ToTimeSpan(parts[1]);
            return (start, (int)Math.Max(1, Math.Round(duration.TotalHours)));
        }
        catch (FormatException)
        {
            return (null, 0);
        }
    }

    private static double Convert(double value, string uom) => uom switch
    {
        "wmoUnit:degC" => value * 9.0 / 5.0 + 32.0,
        "wmoUnit:degF" => value,
        "wmoUnit:km_h-1" => value * 0.621371,
        "wmoUnit:m_s-1" => value * 2.23694,
        "wmoUnit:mm" => value / 25.4,
        "wmoUnit:m" => value / 1609.34,
        _ => value,
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter NwsGridpointParserTests`
Expected: PASS (all 5).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.API/Services/NwsGridpointParser.cs backend/RouteWeather.API.Tests/NwsGridpointParserTests.cs
git commit -m "feat(backend): pure NWS gridpoint parser - interval expansion, unit conversion, weather text"
```

---

### Task 10: NwsClient gridpoints swap with persisted point lookup

`NwsClient` now: (1) resolves `(gridId, gridX, gridY)` from a persisted `"NWS-Grid"` cache row (365-day expiry), calling `points/{lat},{lon}` only on a miss; (2) fetches the raw gridpoint and parses with `NwsGridpointParser`; (3) on a gridpoints 404 with a cached mapping (NWS regrid), re-resolves once and retries. Net: 1 NWS call per refresh instead of 2.

**Files:**
- Modify: `backend/RouteWeather.API/Services/NwsClient.cs` (full rewrite)
- Test: `backend/RouteWeather.API.Tests/NwsClientTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/RouteWeather.API.Tests/NwsClientTests.cs`. Uses a scripted `HttpMessageHandler` plus the existing `TestDbContextFactory` for the repository:

```csharp
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RouteWeather.API.Services;
using RouteWeather.Core.Services;
using RouteWeather.Core.Sources;
using RouteWeather.Data.Repositories;
using Xunit;

namespace RouteWeather.API.Tests;

public class NwsClientTests
{
    private const string PointsJson = """
    { "properties": { "gridId": "SEW", "gridX": 124, "gridY": 69 } }
    """;

    // Minimal gridpoint body the parser accepts (one temperature interval starting now).
    private static string GridJson()
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day,
            DateTime.UtcNow.Hour, 0, 0, TimeSpan.Zero).ToString("yyyy-MM-dd'T'HH:mm:sszzz",
            System.Globalization.CultureInfo.InvariantCulture);
        return $$"""
        { "properties": { "temperature": { "uom": "wmoUnit:degC", "values": [
            { "validTime": "{{start}}/PT4H", "value": 10.0 } ] } } }
        """;
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        public Func<string, HttpResponseMessage> Respond { get; set; } = _ => Json("{}");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            return Task.FromResult(Respond(path));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/geo+json") };

    private static NwsClient Client(ScriptedHandler handler, ForecastCacheRepository repo) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.weather.gov/") },
            NullLogger<NwsClient>.Instance,
            new DailyCallCounter(),
            repo);

    private static readonly ForecastLocation Loc = new(RouteId: 1, Lat: 46.85, Lon: -121.76, SummitElevationFt: 14411);

    [Fact]
    public async Task FirstFetch_resolvesPoints_thenGridpoints_andCachesMapping()
    {
        await using var fixture = TestDbContextFactory.Create();
        var repo = new ForecastCacheRepository(fixture.Factory);
        var handler = new ScriptedHandler
        {
            Respond = path => path.StartsWith("/points/") ? Json(PointsJson) : Json(GridJson()),
        };

        var snap = await Client(handler, repo).FetchAsync(Loc, CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Equal(2, handler.Paths.Count);
        Assert.StartsWith("/points/", handler.Paths[0]);
        Assert.Equal("/gridpoints/SEW/124,69", handler.Paths[1]);
        Assert.NotNull(await repo.GetAsync(1, "NWS-Grid", CancellationToken.None));
    }

    [Fact]
    public async Task SecondFetch_usesCachedMapping_singleCall()
    {
        await using var fixture = TestDbContextFactory.Create();
        var repo = new ForecastCacheRepository(fixture.Factory);
        var handler = new ScriptedHandler
        {
            Respond = path => path.StartsWith("/points/") ? Json(PointsJson) : Json(GridJson()),
        };
        var client = Client(handler, repo);

        await client.FetchAsync(Loc, CancellationToken.None);
        handler.Paths.Clear();

        var snap = await client.FetchAsync(Loc, CancellationToken.None);

        Assert.NotNull(snap);
        var path = Assert.Single(handler.Paths);
        Assert.Equal("/gridpoints/SEW/124,69", path);
    }

    [Fact]
    public async Task Gridpoints404_withCachedMapping_reResolvesOnce()
    {
        await using var fixture = TestDbContextFactory.Create();
        var repo = new ForecastCacheRepository(fixture.Factory);
        // Pre-seed a stale mapping pointing at an old grid.
        await repo.UpsertAsync(1, "NWS-Grid",
            """{"gridId":"OLD","gridX":1,"gridY":1}""",
            DateTime.UtcNow.AddDays(365), CancellationToken.None);

        var handler = new ScriptedHandler
        {
            Respond = path =>
                path.Contains("/OLD/") ? Json("{}", HttpStatusCode.NotFound)
                : path.StartsWith("/points/") ? Json(PointsJson)
                : Json(GridJson()),
        };

        var snap = await Client(handler, repo).FetchAsync(Loc, CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Equal(new[] { "/gridpoints/OLD/1,1", "/points/46.8500,-121.7600", "/gridpoints/SEW/124,69" },
            handler.Paths.ToArray());
    }
}
```

Adjust the `TestDbContextFactory.Create()` / `ForecastCacheRepository` construction to match the existing helpers in `backend/RouteWeather.API.Tests/TestDbContextFactory.cs` and `ForecastCacheRepositoryTests.cs` — reuse their setup verbatim rather than inventing a new pattern. If `DailyCallCounter`'s constructor needs arguments, copy how `Fakes.cs`/existing tests construct it.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter NwsClientTests`
Expected: FAIL to compile — `NwsClient` has no repository parameter and no gridpoints flow.

- [ ] **Step 3: Rewrite NwsClient**

Replace `backend/RouteWeather.API/Services/NwsClient.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using RouteWeather.Core.Models;
using RouteWeather.Core.Services;
using RouteWeather.Core.Sources;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

public class NwsClient : IForecastSource
{
    public string Name => "NWS";

    public IReadOnlySet<string> ActiveFactors => ForecastFactors.All;

    // The lat/lon -> grid mapping is static per route; persist it in the forecast
    // cache table under this pseudo-source so refreshes cost one NWS call, not two.
    private const string GridCacheSource = "NWS-Grid";
    private static readonly TimeSpan GridCacheTtl = TimeSpan.FromDays(365);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ILogger<NwsClient> _logger;
    private readonly DailyCallCounter _calls;
    private readonly ForecastCacheRepository _cache;

    public NwsClient(HttpClient http, ILogger<NwsClient> logger, DailyCallCounter calls, ForecastCacheRepository cache)
    {
        _http = http;
        _logger = logger;
        _calls = calls;
        _cache = cache;
    }

    public async Task<WeatherSnapshot?> FetchAsync(ForecastLocation location, CancellationToken ct = default)
    {
        try
        {
            var (grid, fromPoints) = await ResolveGridAsync(location, forceRefresh: false, ct);
            if (grid is null) return null;

            var snap = await FetchGridpointAsync(grid, ct);
            if (snap is null && !fromPoints)
            {
                // Cached mapping may be stale after an NWS regrid — re-resolve once.
                (grid, _) = await ResolveGridAsync(location, forceRefresh: true, ct);
                if (grid is null) return null;
                snap = await FetchGridpointAsync(grid, ct);
            }
            return snap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NWS fetch failed for {Lat},{Lon}", location.Lat, location.Lon);
            return null;
        }
    }

    private async Task<(GridRef? Grid, bool FromPointsCall)> ResolveGridAsync(
        ForecastLocation location, bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh)
        {
            var row = await _cache.GetAsync(location.RouteId, GridCacheSource, ct);
            if (row is not null && row.ExpiresAtUtc > DateTime.UtcNow)
            {
                var cached = JsonSerializer.Deserialize<GridRef>(row.PayloadJson, JsonOpts);
                if (cached is not null) return (cached, false);
            }
        }

        _calls.Increment("NWS");
        using var resp = await _http.GetAsync($"points/{location.Lat:0.0000},{location.Lon:0.0000}", ct);
        resp.EnsureSuccessStatusCode();
        var point = await resp.Content.ReadFromJsonAsync<NwsPointResponse>(cancellationToken: ct);
        var props = point?.Properties;
        if (props?.GridId is null) return (null, true);

        var grid = new GridRef(props.GridId, props.GridX, props.GridY);
        await _cache.UpsertAsync(location.RouteId, GridCacheSource,
            JsonSerializer.Serialize(grid, JsonOpts), DateTime.UtcNow.Add(GridCacheTtl), ct);
        return (grid, true);
    }

    private async Task<WeatherSnapshot?> FetchGridpointAsync(GridRef grid, CancellationToken ct)
    {
        _calls.Increment("NWS");
        using var resp = await _http.GetAsync($"gridpoints/{grid.GridId}/{grid.GridX},{grid.GridY}", ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("NWS gridpoints {Status} for {Grid}", (int)resp.StatusCode, grid);
            return null;
        }
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return NwsGridpointParser.Parse(doc.RootElement, DateTimeOffset.UtcNow);
    }

    public sealed record GridRef(string GridId, int GridX, int GridY);

    private sealed class NwsPointResponse
    {
        [JsonPropertyName("properties")] public NwsPointProperties? Properties { get; set; }
    }

    private sealed class NwsPointProperties
    {
        [JsonPropertyName("gridId")] public string? GridId { get; set; }
        [JsonPropertyName("gridX")] public int GridX { get; set; }
        [JsonPropertyName("gridY")] public int GridY { get; set; }
    }
}
```

- [ ] **Step 4: Run all API tests**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: NwsClientTests PASS; pre-existing aggregator/controller tests still PASS (they use fakes, not the real NwsClient). If any test constructed `NwsClient` directly, add the repository argument.

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.API/Services/NwsClient.cs backend/RouteWeather.API.Tests/NwsClientTests.cs
git commit -m "feat(backend): NWS raw gridpoints swap with persisted point lookup and 404 re-resolve"
```

---### Task 11: Air quality source end-to-end (backend)

**Files:**
- Create: `backend/RouteWeather.Core/Models/AirQualitySnapshot.cs`
- Create: `backend/RouteWeather.Core/Sources/IAirQualitySource.cs`
- Modify: `backend/RouteWeather.Core/Models/RouteConditions.cs` (`AirQuality`, `AirQualityFetchedAt`)
- Create: `backend/RouteWeather.API/Services/AirQualityClient.cs`
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs`
- Modify: `backend/RouteWeather.API/Program.cs`
- Modify: `backend/RouteWeather.API/appsettings.json`
- Test: `backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs` (add cases), `backend/RouteWeather.API.Tests/Fakes.cs` (AQI fake)

- [ ] **Step 1: Core contracts**

Create `backend/RouteWeather.Core/Models/AirQualitySnapshot.cs`:

```csharp
namespace RouteWeather.Core.Models;

public record AirQualitySnapshot(int UsAqi, double Pm25);
```

Create `backend/RouteWeather.Core/Sources/IAirQualitySource.cs`:

```csharp
using RouteWeather.Core.Models;

namespace RouteWeather.Core.Sources;

public interface IAirQualitySource
{
    string Name { get; }

    Task<AirQualitySnapshot?> FetchAsync(double lat, double lon, CancellationToken ct);
}
```

In `RouteConditions.cs`, extend `SourceFreshness` and `RouteConditions` (defaults keep existing call sites compiling):

```csharp
public record SourceFreshness(
    DateTimeOffset? NwsFetchedAt,
    DateTimeOffset? SnotelFetchedAt,
    DateTimeOffset? AirQualityFetchedAt = null);
```

and append to the `RouteConditions` record:

```csharp
    IReadOnlyList<PerSourceForecast>? PerSourceForecast,
    AirQualitySnapshot? AirQuality = null
);
```

- [ ] **Step 2: Write the failing aggregator tests**

In `backend/RouteWeather.API.Tests/Fakes.cs`, add an AQI fake following the file's existing fake style:

```csharp
public sealed class FakeAirQualitySource : IAirQualitySource
{
    public string Name => "AirQuality";
    public AirQualitySnapshot? Result { get; set; } = new(42, 5.0);
    public int FetchCount { get; private set; }

    public Task<AirQualitySnapshot?> FetchAsync(double lat, double lon, CancellationToken ct)
    {
        FetchCount++;
        return Task.FromResult(Result);
    }
}
```

Add to `ConditionsAggregatorTests.cs` (follow the file's existing construction helpers for the aggregator/routes — these tests state intent; wire them with the same fixtures the neighboring tests use):

```csharp
    [Fact]
    public async Task ReadThrough_fetchesAndCachesAirQuality()
    {
        // Arrange aggregator with FakeAirQualitySource returning AQI 42.
        // Act: GetConditionsAsync(route, FetchMode.ReadThrough).
        // Assert: conditions.AirQuality is { UsAqi: 42 }, Sources.AirQualityFetchedAt != null,
        //         and a cache row exists for (route.Id, "AirQuality").
    }

    [Fact]
    public async Task CacheOnly_readsAirQualityRow_withoutFetching()
    {
        // Arrange: pre-upsert an AirQuality row {"usAqi":80,"pm25":12.0} with future expiry.
        // Act: GetConditionsAsync(route, FetchMode.CacheOnly).
        // Assert: conditions.AirQuality.UsAqi == 80 and FakeAirQualitySource.FetchCount == 0.
    }

    [Fact]
    public async Task StaleAirQuality_doesNotFlagRouteStale()
    {
        // Arrange: fresh forecast rows + an AirQuality row already past its expiry
        //          (but within the 24h serve-stale window).
        // Act: GetConditionsAsync(route, FetchMode.CacheOnly).
        // Assert: conditions.AirQuality is not null AND conditions.IsStale == false.
    }

    [Fact]
    public async Task FailedAirQuality_yieldsNullAirQuality_gradeUnaffected()
    {
        // Arrange: FakeAirQualitySource.Result = null, healthy forecast fakes.
        // Act: ReadThrough.
        // Assert: conditions.AirQuality == null, conditions.Grade != null.
    }
```

Write the four bodies fully using the file's existing arrange/act helpers (`TestData`, fake sources, `TestDbContextFactory`); the comments above define the exact behavior to assert.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter ConditionsAggregatorTests`
Expected: FAIL to compile (no `IAirQualitySource` ctor param / `AirQuality` property), or fail assertions.

- [ ] **Step 4: Implement the client**

Create `backend/RouteWeather.API/Services/AirQualityClient.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RouteWeather.Core.Models;
using RouteWeather.Core.Services;
using RouteWeather.Core.Sources;

namespace RouteWeather.API.Services;

public class AirQualityClient : IAirQualitySource
{
    public string Name => "AirQuality";

    private readonly HttpClient _http;
    private readonly ILogger<AirQualityClient> _logger;
    private readonly DailyCallCounter _calls;

    public AirQualityClient(HttpClient http, ILogger<AirQualityClient> logger, DailyCallCounter calls)
    {
        _http = http;
        _logger = logger;
        _calls = calls;
    }

    public async Task<AirQualitySnapshot?> FetchAsync(double lat, double lon, CancellationToken ct)
    {
        var url = "v1/air-quality" +
                  $"?latitude={lat.ToString("F4", CultureInfo.InvariantCulture)}" +
                  $"&longitude={lon.ToString("F4", CultureInfo.InvariantCulture)}" +
                  "&current=us_aqi,pm2_5";
        try
        {
            _calls.Increment("AirQuality");
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("AirQuality {Status} for {Lat},{Lon}", (int)resp.StatusCode, lat, lon);
                return null;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<AirQualityResponse>(stream, cancellationToken: ct);
            var current = payload?.Current;
            if (current?.UsAqi is null) return null;
            return new AirQualitySnapshot((int)Math.Round(current.UsAqi.Value), current.Pm25 ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AirQuality fetch failed for {Lat},{Lon}", lat, lon);
            return null;
        }
    }

    private sealed class AirQualityResponse
    {
        [JsonPropertyName("current")] public CurrentBlock? Current { get; set; }
    }

    private sealed class CurrentBlock
    {
        [JsonPropertyName("us_aqi")] public double? UsAqi { get; set; }
        [JsonPropertyName("pm2_5")] public double? Pm25 { get; set; }
    }
}
```

- [ ] **Step 5: Wire the aggregator**

In `ConditionsAggregator.cs`:

(a) Ctor gains `IEnumerable<IAirQualitySource> airQualitySources` (store as `_airQualitySources` array, like the other source lists).

(b) In `GetConditionsAsync` ReadThrough branch, add an AQI fetch arm alongside snowpack:

```csharp
            var airQualityFetches = _airQualitySources
                .Select(s => FetchAirQualityAsync(routeEntity, s, ct))
                .ToArray();

            await Task.WhenAll(forecastFetches.Cast<Task>().Concat(snowpackFetches).Concat(airQualityFetches));
```

and pass `airQualityFetches.Select(t => t.Result).ToList()` into `BuildConditions`.

(c) Add the fetch method, mirroring `FetchSnowpackAsync` exactly (TTL via `_options.TtlFor(source.Name)`, serve-stale fallback, upsert on success) but for `AirQualitySnapshot` and **without** any stale flag consumers:

```csharp
    private async Task<AirQualityFetchResult> FetchAirQualityAsync(RouteEntity route, IAirQualitySource source, CancellationToken ct)
    {
        var ttl = _options.TtlFor(source.Name);
        var nowUtc = DateTime.UtcNow;
        var cached = await _cache.GetAsync(route.Id, source.Name, ct);

        if (cached is not null && cached.ExpiresAtUtc > nowUtc)
        {
            return new AirQualityFetchResult(TryDeserialize<AirQualitySnapshot>(cached.PayloadJson, source.Name, route.Id),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero));
        }

        AirQualitySnapshot? fresh = null;
        try
        {
            fresh = await source.FetchAsync(route.SummitLat, route.SummitLon, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Air quality source {Source} threw for {Slug}", source.Name, route.Slug);
        }

        if (fresh is not null)
        {
            await _cache.UpsertAsync(route.Id, source.Name, JsonSerializer.Serialize(fresh, JsonOpts), nowUtc.Add(ttl), ct);
            return new AirQualityFetchResult(fresh, DateTimeOffset.UtcNow);
        }

        if (cached is not null)
        {
            return new AirQualityFetchResult(TryDeserialize<AirQualitySnapshot>(cached.PayloadJson, source.Name, route.Id),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero));
        }

        return new AirQualityFetchResult(null, null);
    }

    private record struct AirQualityFetchResult(AirQualitySnapshot? Snapshot, DateTimeOffset? FetchedAt);
```

(d) In `BuildFromCachedRows`, read the AQI row the same way (within the 24h `cutoffUtc`), produce an `AirQualityFetchResult`, and — critically — **do not** include it in the `forceStale` computation.

(e) `BuildConditions` gains a `List<AirQualityFetchResult> airQualityResults` parameter; pick `var airQuality = airQualityResults.FirstOrDefault(r => r.Snapshot is not null);` then:
- `SourceFreshness` third arg: `airQuality.FetchedAt`
- `RouteConditions` final args: `..., perSourceForecast.Count == 0 ? null : perSourceForecast, airQuality.Snapshot);`

(f) `isStale` computation: unchanged — AQI results never feed it.

- [ ] **Step 6: Register and configure**

In `Program.cs`, after the OpenMeteo client registration:

```csharp
builder.Services.AddHttpClient<AirQualityClient>(c =>
{
    c.BaseAddress = new Uri("https://air-quality-api.open-meteo.com/");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    c.Timeout = TimeSpan.FromSeconds(15);
});
if (forecastConfig.IsEnabled("AirQuality"))
{
    builder.Services.AddScoped<IAirQualitySource>(sp => sp.GetRequiredService<AirQualityClient>());
}
```

In `appsettings.json`, add to `ForecastSources.Sources`:

```json
      { "Name": "AirQuality",      "Enabled": true, "Weight": 1.0, "CacheTtlMinutes": 180 }
```

- [ ] **Step 7: Run all backend tests**

Run:
```bash
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
```
Expected: PASS, including the four new aggregator cases. Existing aggregator tests need the new ctor arg — pass an empty `Array.Empty<IAirQualitySource>()` or the fake, matching each test's intent.

- [ ] **Step 8: Commit**

```bash
git add backend/RouteWeather.Core/Models/AirQualitySnapshot.cs backend/RouteWeather.Core/Sources/IAirQualitySource.cs backend/RouteWeather.Core/Models/RouteConditions.cs backend/RouteWeather.API/Services/AirQualityClient.cs backend/RouteWeather.API/Services/ConditionsAggregator.cs backend/RouteWeather.API/Program.cs backend/RouteWeather.API/appsettings.json backend/RouteWeather.API.Tests/Fakes.cs backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs
git commit -m "feat(backend): AirQuality source - warmer-fetched, cached, exempt from isStale"
```

---

### Task 12: SolarCalculator + daylight on RouteConditions

**Files:**
- Create: `backend/RouteWeather.Core/Services/SolarCalculator.cs`
- Modify: `backend/RouteWeather.Core/Models/RouteConditions.cs` (`Daylight` field)
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs` (compute in `BuildConditions`)
- Test: `backend/RouteWeather.Core.Tests/Services/SolarCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/RouteWeather.Core.Tests/Services/SolarCalculatorTests.cs`:

```csharp
using RouteWeather.Core.Services;
using Xunit;

namespace RouteWeather.Core.Tests.Services;

public class SolarCalculatorTests
{
    // Mt Rainier summit.
    private const double Lat = 46.853;
    private const double Lon = -121.760;

    [Fact]
    public void SummerSolstice_rainier_matchesKnownTimes()
    {
        var d = SolarCalculator.ComputeUtc(Lat, Lon, new DateOnly(2026, 6, 20));

        Assert.NotNull(d);
        // Sunrise ~05:11 PDT = 12:11 UTC; sunset ~21:11 PDT = 04:11 UTC next day. ±10 min.
        AssertWithin(d!.SunriseUtc, DateTimeOffset.Parse("2026-06-20T12:11:00Z"), minutes: 10);
        AssertWithin(d.SunsetUtc, DateTimeOffset.Parse("2026-06-21T04:11:00Z"), minutes: 10);
        Assert.InRange(d.DaylightHours, 15.7, 16.3);
    }

    [Fact]
    public void Equinox_daylightIsNearTwelveHours()
    {
        var d = SolarCalculator.ComputeUtc(Lat, Lon, new DateOnly(2026, 3, 20));
        Assert.NotNull(d);
        Assert.InRange(d!.DaylightHours, 11.8, 12.4);
    }

    [Fact]
    public void NextDaylight_afterSunset_rollsToTomorrow()
    {
        // 06:00 UTC on Jun 21 is ~23:00 PDT Jun 20 — past sunset.
        var next = SolarCalculator.NextDaylight(Lat, Lon, DateTimeOffset.Parse("2026-06-21T06:00:00Z"));
        Assert.NotNull(next);
        Assert.True(next!.SunsetUtc > DateTimeOffset.Parse("2026-06-21T06:00:00Z"));
    }

    [Fact]
    public void PolarNight_returnsNull() =>
        Assert.Null(SolarCalculator.ComputeUtc(80.0, 0.0, new DateOnly(2026, 12, 21)));

    private static void AssertWithin(DateTimeOffset actual, DateTimeOffset expected, int minutes) =>
        Assert.True((actual - expected).Duration() <= TimeSpan.FromMinutes(minutes),
            $"Expected {expected:u} ±{minutes}m but got {actual:u}");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter SolarCalculatorTests`
Expected: FAIL to compile — `SolarCalculator` not defined.

- [ ] **Step 3: Implement**

Create `backend/RouteWeather.Core/Services/SolarCalculator.cs` (NOAA solar geometry; longitude positive-east):

```csharp
namespace RouteWeather.Core.Services;

public record DaylightInfo(DateTimeOffset SunriseUtc, DateTimeOffset SunsetUtc, double DaylightHours);

/// <summary>NOAA sunrise/sunset approximation — accurate to a couple of minutes at mid-latitudes.</summary>
public static class SolarCalculator
{
    public static DaylightInfo? NextDaylight(double lat, double lon, DateTimeOffset nowUtc)
    {
        var today = ComputeUtc(lat, lon, DateOnly.FromDateTime(nowUtc.UtcDateTime));
        if (today is not null && today.SunsetUtc > nowUtc) return today;
        return ComputeUtc(lat, lon, DateOnly.FromDateTime(nowUtc.UtcDateTime).AddDays(1));
    }

    public static DaylightInfo? ComputeUtc(double lat, double lon, DateOnly dateUtc)
    {
        var gamma = 2.0 * Math.PI / 365.0 * (dateUtc.DayOfYear - 1);

        var eqTimeMin = 229.18 * (0.000075
            + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));

        var declRad = 0.006918
            - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
            - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        var latRad = lat * Math.PI / 180.0;
        var zenithRad = 90.833 * Math.PI / 180.0; // official sunrise: refraction + solar disc

        var cosHa = Math.Cos(zenithRad) / (Math.Cos(latRad) * Math.Cos(declRad))
                    - Math.Tan(latRad) * Math.Tan(declRad);
        if (cosHa < -1 || cosHa > 1) return null; // polar day or night

        var haDeg = Math.Acos(cosHa) * 180.0 / Math.PI;
        var sunriseMin = 720.0 - 4.0 * (lon + haDeg) - eqTimeMin;
        var sunsetMin = 720.0 - 4.0 * (lon - haDeg) - eqTimeMin;

        var midnightUtc = new DateTimeOffset(dateUtc.Year, dateUtc.Month, dateUtc.Day, 0, 0, 0, TimeSpan.Zero);
        var sunrise = midnightUtc.AddMinutes(sunriseMin);
        var sunset = midnightUtc.AddMinutes(sunsetMin);
        return new DaylightInfo(sunrise, sunset, (sunset - sunrise).TotalMinutes / 60.0);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter SolarCalculatorTests`
Expected: PASS. If the solstice assertion misses by more than the ±10-min tolerance, debug the longitude sign convention (NOAA uses positive-east; route longitudes are negative) before touching the tolerance.

- [ ] **Step 5: Wire into conditions**

In `RouteConditions.cs`, add to the record after `AirQuality`:

```csharp
    AirQualitySnapshot? AirQuality = null,
    DaylightInfo? Daylight = null
);
```

with `using RouteWeather.Core.Services;` at the top.

In `ConditionsAggregator.BuildConditions`, compute and pass it:

```csharp
        var daylight = SolarCalculator.NextDaylight(routeEntity.SummitLat, routeEntity.SummitLon, DateTimeOffset.UtcNow);
```

and append `daylight` as the final `RouteConditions` argument.

- [ ] **Step 6: Run all backend tests, then commit**

Run both backend test suites. Expected: PASS.

```bash
git add backend/RouteWeather.Core/Services/SolarCalculator.cs backend/RouteWeather.Core/Models/RouteConditions.cs backend/RouteWeather.API/Services/ConditionsAggregator.cs backend/RouteWeather.Core.Tests/Services/SolarCalculatorTests.cs
git commit -m "feat(backend): NOAA SolarCalculator and daylight info on route conditions"
```

---

### Task 13: Per-source gust/CAPE + controller DTOs

**Files:**
- Modify: `backend/RouteWeather.Core/Models/RouteConditions.cs` (`PerSourceForecast` fields)
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs` (populate them)
- Modify: `backend/RouteWeather.API/Controllers/RoutesController.cs`
- Test: `backend/RouteWeather.API.Tests/RoutesControllerTests.cs`

- [ ] **Step 1: Extend PerSourceForecast**

In `RouteConditions.cs`:

```csharp
public record PerSourceForecast(
    string SourceName,
    double WindMph,
    double TempF,
    int? PrecipitationProbabilityPct,
    DateTimeOffset FetchedAt,
    double? MaxGustMph = null,
    double? MaxCapeJkg = null
);
```

In `ConditionsAggregator.BuildConditions`, extend the projection:

```csharp
        var perSourceForecast = liveForecasts
            .Select(r => new PerSourceForecast(
                r.SourceName,
                r.Snapshot!.WindMph,
                r.Snapshot.TempF,
                r.ActiveFactors.Contains(ForecastFactors.Precipitation) ? r.Snapshot.PrecipitationProbabilityPct : (int?)null,
                r.FetchedAt ?? DateTimeOffset.UtcNow,
                r.Snapshot.MaxGustMph,
                r.Snapshot.MaxCapeJkg))
            .ToList();
```

- [ ] **Step 2: Write failing controller tests**

Add to `RoutesControllerTests.cs`, following its existing arrange style (fake aggregator returning canned `RouteConditions`):

```csharp
    [Fact]
    public async Task GetAll_summaryIncludesAirQualityUsAqi()
    {
        // Arrange conditions with AirQuality = new AirQualitySnapshot(168, 60.0).
        // Act GET all; deserialize first summary.
        // Assert JSON property airQualityUsAqi == 168; when AirQuality is null -> null.
    }

    [Fact]
    public async Task GetBySlug_detailIncludesAirQualityDaylightAndPerSourceExtras()
    {
        // Arrange conditions with AirQuality, Daylight, and PerSourceForecast
        //   containing MaxGustMph 42.5 / MaxCapeJkg 850.
        // Assert detail JSON: airQuality.usAqi, airQuality.pm25, airQuality.fetchedAt,
        //   daylight.sunriseUtc, daylight.sunsetUtc, daylight.daylightHours,
        //   perSourceForecast[0].maxGustMph == 42.5, .maxCapeJkg == 850.
    }
```

Write the bodies fully using the file's existing fixture helpers (`TestData` builds `RouteConditions` — extend its builder with optional `airQuality`/`daylight` parameters defaulting to null).

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter RoutesControllerTests`
Expected: FAIL — DTOs don't emit the new properties.

- [ ] **Step 4: Extend the controller DTOs**

In `RoutesController.ToSummary`, add after `consensus`:

```csharp
            airQualityUsAqi = c.AirQuality?.UsAqi,
```

In `ToDetail`, add after `consensus = ...`:

```csharp
        airQuality = c.AirQuality is null ? null : new
        {
            usAqi = c.AirQuality.UsAqi,
            pm25 = c.AirQuality.Pm25,
            fetchedAt = c.Sources.AirQualityFetchedAt,
        },
        daylight = c.Daylight is null ? null : new
        {
            sunriseUtc = c.Daylight.SunriseUtc,
            sunsetUtc = c.Daylight.SunsetUtc,
            daylightHours = c.Daylight.DaylightHours,
        },
```

and extend the `perSourceForecast` projection:

```csharp
        perSourceForecast = c.PerSourceForecast?.Select(p => new
        {
            sourceName = p.SourceName,
            windMph = p.WindMph,
            tempF = p.TempF,
            precipitationProbabilityPct = p.PrecipitationProbabilityPct,
            fetchedAt = p.FetchedAt,
            maxGustMph = p.MaxGustMph,
            maxCapeJkg = p.MaxCapeJkg,
        }),
```

(`ToDetail`'s `forecastNext48h = c.Weather?.Next48Hours` already serializes the new hourly fields automatically — records emit all properties camelCased.)

- [ ] **Step 5: Run all backend tests, then commit**

Run both backend suites. Expected: PASS.

```bash
git add backend/RouteWeather.Core/Models/RouteConditions.cs backend/RouteWeather.API/Services/ConditionsAggregator.cs backend/RouteWeather.API/Controllers/RoutesController.cs backend/RouteWeather.API.Tests/RoutesControllerTests.cs backend/RouteWeather.API.Tests/TestData.cs
git commit -m "feat(backend): expose air quality, daylight, and per-source gust/CAPE in API DTOs"
```

---

### Task 14: Frontend models + fixture sweep

**Files:**
- Modify: `frontend/src/app/models/route-conditions.ts`
- Modify: `frontend/src/app/components/route-card/route-card.spec.ts`
- Modify: `frontend/src/app/components/route-grid/route-grid.spec.ts`
- Modify: `frontend/src/app/pages/map-home/map-home.spec.ts`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`

- [ ] **Step 1: Extend the TypeScript models**

In `frontend/src/app/models/route-conditions.ts`:

```ts
export interface PerSourceForecast {
  sourceName: string;
  windMph: number;
  tempF: number;
  precipitationProbabilityPct: number | null;
  fetchedAt: string;
  maxGustMph: number | null;
  maxCapeJkg: number | null;
}
```

`RouteSummary` gains one field (after `consensus`):

```ts
  airQualityUsAqi: number | null;
```

`HourlyForecast` gains:

```ts
  gustMph: number | null;
  capeJkg: number | null;
  precipitationIn: number | null;
  cloudCoverPct: number | null;
  visibilityMiles: number | null;
  apparentTempF: number | null;
```

Add new interfaces and extend `RouteDetail`:

```ts
export interface AirQuality {
  usAqi: number;
  pm25: number;
  fetchedAt: string | null;
}

export interface Daylight {
  sunriseUtc: string;
  sunsetUtc: string;
  daylightHours: number;
}
```

```ts
export interface RouteDetail extends RouteSummary {
  factors: FactorScore[];
  rationale: string;
  forecastNext48h: HourlyForecast[] | null;
  snowpack: SnowpackSnapshot | null;
  windowGrades: WindowGrades | null;
  sources: DetailSources;
  perSourceForecast: PerSourceForecast[] | null;
  airQuality: AirQuality | null;
  daylight: Daylight | null;
}
```

- [ ] **Step 2: Sweep every inline fixture builder**

Run `Grep` for `isStale:` under `frontend/src` — every object-literal `RouteSummary`/`RouteDetail` builder (known: `route-card.spec.ts` `summary()`, `route-grid.spec.ts`, `map-home.spec.ts`, `peak-detail.spec.ts`) gains:

- `airQualityUsAqi: null,` (all RouteSummary builders)
- RouteDetail builders additionally: `airQuality: null,` `daylight: null,`
- Any `HourlyForecast` literals gain the six new nullable fields set to `null`.
- Any `PerSourceForecast` literals gain `maxGustMph: null, maxCapeJkg: null`.

- [ ] **Step 3: Run the frontend tests**

Run from `frontend/`: `npm test`
Expected: PASS — TypeScript enforces fixture completeness; no behavior changed yet.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/app/models/route-conditions.ts frontend/src/app/components/route-card/route-card.spec.ts frontend/src/app/components/route-grid/route-grid.spec.ts frontend/src/app/pages/map-home/map-home.spec.ts frontend/src/app/pages/peak-detail/peak-detail.spec.ts
git commit -m "feat(frontend): mirror new weather-signal contract in models and fixtures"
```

---

### Task 15: Route card smoke chip

**Files:**
- Modify: `frontend/src/app/components/route-card/route-card.html`
- Modify: `frontend/src/app/components/route-card/route-card.scss`
- Test: `frontend/src/app/components/route-card/route-card.spec.ts`

- [ ] **Step 1: Write the failing tests**

Add to `route-card.spec.ts`:

```ts
  it('shows a smoky-air chip when AQI is 151 or above', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), airQualityUsAqi: 168 });
    fixture.detectChanges();

    const chip = (fixture.nativeElement as HTMLElement).querySelector('.aqi-chip');
    expect(chip).toBeTruthy();
    expect(chip!.textContent).toContain('168');
  });

  it('stays silent at AQI 150 and below, and when AQI is null', () => {
    for (const aqi of [150, 42, null]) {
      const fixture = TestBed.createComponent(RouteCard);
      fixture.componentRef.setInput('route', { ...summary('foo'), airQualityUsAqi: aqi });
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('.aqi-chip')).toBeNull();
    }
  });
```

- [ ] **Step 2: Run tests to verify they fail**

Run from `frontend/`: `npm test`
Expected: new specs FAIL — no `.aqi-chip` rendered.

- [ ] **Step 3: Implement**

In `route-card.html`, inside `<div class="meta">` after the stale chip:

```html
    @if ((route().airQualityUsAqi ?? 0) >= 151) {
      <span class="aqi-chip">Smoky air · AQI {{ route().airQualityUsAqi }}</span>
    }
```

In `route-card.scss`, attach the new chip to the existing muted stale-chip rule by extending its selector (find the `.stale-chip` rule and change the selector to `.stale-chip, .aqi-chip`). Do not add new colors — silent-by-default means the actionable state is muted, not loud.

- [ ] **Step 4: Run tests to verify they pass, then commit**

Run: `npm test` — expected PASS.

```bash
git add frontend/src/app/components/route-card/route-card.html frontend/src/app/components/route-card/route-card.scss frontend/src/app/components/route-card/route-card.spec.ts
git commit -m "feat(frontend): muted smoky-air card chip at AQI >= 151"
```

---

### Task 16: Peak-detail — Sky & Air tiles, table columns, 24h collapse

**Files:**
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.ts`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.html`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.scss`
- Test: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`

- [ ] **Step 1: Write the failing tests**

Add to `peak-detail.spec.ts`, reusing its existing detail-fixture helper (give the fixture a 48-entry `forecastNext48h`, an `airQuality` of `{ usAqi: 95, pm25: 30, fetchedAt: <iso> }`, and a `daylight` object):

```ts
  it('renders the Sky & Air section with AQI category and daylight', () => {
    // fixture detail: airQuality.usAqi 95 -> category "Moderate"; daylight present
    // assert: section [data-testid="sky-air"] exists; text contains "Moderate" and "95";
    //         daylight tile shows daylightHours formatted to 1 decimal
  });

  it('shows "unavailable" in the AQI tile when airQuality is null', () => {
    // fixture detail: airQuality null
    // assert: sky-air section text contains "unavailable"
  });

  it('collapses the hourly table to 24 rows with a Show 48h toggle', () => {
    // fixture: 48 hourly entries
    // assert: tbody rows === 24; button [data-testid="forecast-toggle"] text contains "48";
    // click the button; detectChanges; rows === 48; button text contains "24"
  });

  it('renders Gust and Clouds columns with em-dash for nulls', () => {
    // fixture: first hourly entry gustMph 32, cloudCoverPct 80; second entry both null
    // assert: header row includes "Gust" and "Clouds";
    //         first data row contains "32"; second data row contains "—"
  });

  it('renders per-source Max gust and CAPE columns', () => {
    // fixture: perSourceForecast entry with maxGustMph 42.5, maxCapeJkg 850; another with nulls
    // assert: per-source header includes "Max gust" and "CAPE"; null cells render "—"
  });
```

Write each body concretely against the spec file's existing patterns (`TestBed` + `HttpTestingController` flush of the detail response, `fixture.componentRef.setInput('slug', ...)` before first `detectChanges()`, `httpMock.verify()` in `afterEach`). Anchor on structure (`data-testid`, column count, row count) — not prose.

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test`
Expected: new specs FAIL.

- [ ] **Step 3: Implement the component**

In `peak-detail.ts` add:

```ts
  showAll48h = signal(false);

  displayedForecast = computed(() => {
    const all = this.detail()?.forecastNext48h ?? [];
    return this.showAll48h() ? all : all.slice(0, 24);
  });

  skyNow = computed(() => {
    const first = this.detail()?.forecastNext48h?.[0];
    return first ? { cloudCoverPct: first.cloudCoverPct, visibilityMiles: first.visibilityMiles, apparentTempF: first.apparentTempF } : null;
  });

  aqiCategory(aqi: number): string {
    if (aqi <= 50) return 'Good';
    if (aqi <= 100) return 'Moderate';
    if (aqi <= 150) return 'Unhealthy for sensitive groups';
    if (aqi <= 200) return 'Unhealthy';
    if (aqi <= 300) return 'Very unhealthy';
    return 'Hazardous';
  }
```

(`showAll48h` resets naturally on navigation because the component re-instantiates per route; if the spec shows it persisting across `slug` changes, reset it in `load()`.)

In `peak-detail.html`:

(a) Insert the Sky & Air section between the snowpack section and the forecast section:

```html
    <section class="sky-air" [attr.data-testid]="'sky-air'">
      <h3>Sky &amp; air</h3>
      <div class="snowpack-grid">
        <div class="snowpack-tile">
          <span class="label">Clouds now</span>
          <span class="value">{{ skyNow()?.cloudCoverPct !== null && skyNow()?.cloudCoverPct !== undefined ? skyNow()!.cloudCoverPct + '%' : '—' }}</span>
        </div>
        <div class="snowpack-tile">
          <span class="label">Visibility</span>
          <span class="value">{{ skyNow()?.visibilityMiles !== null && skyNow()?.visibilityMiles !== undefined ? (skyNow()!.visibilityMiles | number:'1.0-1') + ' mi' : '—' }}</span>
        </div>
        <div class="snowpack-tile">
          <span class="label">Air quality</span>
          @if (d.airQuality; as aq) {
            <span class="value">{{ aq.usAqi }}</span>
            <span class="label">{{ aqiCategory(aq.usAqi) }}</span>
          } @else {
            <span class="value muted">unavailable</span>
          }
        </div>
        <div class="snowpack-tile">
          <span class="label">Feels like</span>
          <span class="value">{{ skyNow()?.apparentTempF !== null && skyNow()?.apparentTempF !== undefined ? (skyNow()!.apparentTempF | number:'1.0-0') + '°F' : '—' }}</span>
        </div>
        <div class="snowpack-tile">
          <span class="label">Daylight</span>
          @if (d.daylight; as day) {
            <span class="value">{{ day.sunriseUtc | date:'h:mm a' }}–{{ day.sunsetUtc | date:'h:mm a' }}</span>
            <span class="label">{{ day.daylightHours | number:'1.1-1' }} h</span>
          } @else {
            <span class="value muted">—</span>
          }
        </div>
      </div>
    </section>
```

(b) Rework the hourly forecast section to use `displayedForecast()` with the two new columns and the toggle:

```html
    @if (displayedForecast().length > 0) {
      <section class="forecast">
        <h3>Hourly forecast — next {{ displayedForecast().length }}h</h3>
        <table>
          <thead>
            <tr><th>Time</th><th>°F</th><th>Wind</th><th>Gust</th><th>Precip</th><th>Clouds</th><th>Conditions</th></tr>
          </thead>
          <tbody>
            @for (h of displayedForecast(); track h.time) {
              <tr>
                <td>{{ h.time | date:'EEE h a' }}</td>
                <td>{{ h.tempF }}</td>
                <td>{{ h.windMph }} mph</td>
                <td>@if (h.gustMph !== null) { {{ h.gustMph }} mph } @else { — }</td>
                <td>{{ h.precipitationProbabilityPct }}%</td>
                <td>@if (h.cloudCoverPct !== null) { {{ h.cloudCoverPct }}% } @else { — }</td>
                <td>{{ h.shortForecast }}</td>
              </tr>
            }
          </tbody>
        </table>
        @if ((d.forecastNext48h?.length ?? 0) > 24) {
          <button type="button" class="forecast-toggle" [attr.data-testid]="'forecast-toggle'"
                  (click)="showAll48h.set(!showAll48h())">
            {{ showAll48h() ? 'Show 24h' : 'Show 48h' }}
          </button>
        }
      </section>
    }
```

(c) Extend the per-source table:

```html
            <tr><th>Source</th><th>Max wind</th><th>Max gust</th><th>Min °F</th><th>Max precip</th><th>CAPE</th></tr>
```

and in the row, after the wind cell / after the precip cell respectively:

```html
                <td>@if (s.maxGustMph !== null) { {{ s.maxGustMph | number:'1.0-1' }} mph } @else { — }</td>
```

```html
                <td>@if (s.maxCapeJkg !== null) { {{ s.maxCapeJkg | number:'1.0-0' }} } @else { — }</td>
```

(d) In `peak-detail.scss`, add only what's strictly new (reuse `.snowpack-grid`/`.snowpack-tile` as-is):

```scss
.forecast-toggle {
  margin-top: 0.5rem;
  background: none;
  border: 1px solid var(--border, #ccc);
  border-radius: 4px;
  padding: 0.25rem 0.75rem;
  cursor: pointer;
  font: inherit;
}

.sky-air .muted { opacity: 0.6; }
```

- [ ] **Step 4: Run the frontend suite**

Run: `npm test`
Expected: PASS, including pre-existing peak-detail specs. Any old spec asserting 5 hourly columns or "next 48h" heading text must be updated to the new structure (24-row default).

- [ ] **Step 5: Check the style budget**

Run from `frontend/`: `npm run build`
Expected: success. If `peak-detail.scss` exceeds the 5kB `anyComponentStyle` warning (it starts at 5,171 bytes — already at the line), first compact existing rules; if still over, bump the warning to `6kB` in `angular.json` (`projects.*.architect.build.configurations.production.budgets`) and say so in the commit message.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/pages/peak-detail frontend/angular.json
git commit -m "feat(frontend): Sky & Air tiles, gust/cloud columns, 24h-collapsed hourly table"
```

---

### Task 17: Spec as-built notes + full verification

**Files:**
- Modify: `docs/superpowers/specs/2026-06-10-weather-signals-design.md`

- [ ] **Step 1: Record as-built deltas in the spec**

Append an `## As-built deltas` section to the spec documenting:
1. TTLs were already 180 min for all OpenMeteo models; HRRR was NOT lowered to 1h because all models share one HTTP fetch and min-TTL drives request frequency.
2. `weather_code` added to the OpenMeteo fetch; OpenMeteo sources now carry conditions text (previously empty), which also feeds `SnowRelevance` from non-NWS sources.
3. `IForecastSource.FetchAsync` takes `ForecastLocation` (RouteId/lat/lon/elevation).
4. Gridpoint mapping persisted as pseudo-source `"NWS-Grid"` in the existing cache table (365-day TTL) — no schema migration.
5. New-field consensus participation is gated on value presence rather than `ActiveFactors` membership.

- [ ] **Step 2: Full verification**

Run everything:

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```
and from `frontend/`:
```bash
npm test
npm run build
```
Expected: all PASS, production build clean (or with the documented budget bump only).

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-06-10-weather-signals-design.md
git commit -m "docs: as-built deltas for weather signals expansion"
```

---

## Out of scope (do not implement)

- Recent rain / rock-drying signal (deferred in spec).
- Location-dedup cache keying (coordinate-keyed rows) and Open-Meteo paid tier — 300-route-scale levers, recorded in the spec.
- Any TTL changes (already tiered; see as-built delta #1).
- Avalanche, UV, humidity signals.

## Manual verification after implementation

With the user's running servers (do **not** start them yourself): load a route detail page and confirm — Sky & Air tiles render; hourly table shows 24 rows + toggle; per-source table shows gust/CAPE with "—" for ECMWF if it lacks them; factor breakdown shows Thunderstorm/Gusts (active or "Not a factor today"); a card with AQI ≥ 151 (if any) shows the muted chip. Check the Fly logs for one NWS call per route per refresh (gridpoints only after first warm cycle).
