# Climb-Window Finder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scan each route's 7-day hourly forecast for contiguous climbable windows sized to that route's typical summit-day duration, and surface them as a hero callout + week strip on peak detail and a muted "Next window" line on route cards.

**Architecture:** Extend the existing snapshot pipeline: clients emit up to 168h of hourly data while all *scalar* headline fields stay pinned to the first 48h, so every existing consumer (headline grade, 12/24/48 window grades, consensus CV, per-source display) is bit-identical to today. A new pure-static `WindowFinder` in Core scores each hour with the existing factor machinery, intersects qualifying runs with per-day "climbing day" frames (sunrise−6h → sunset), and emits `ClimbWindow`s. The aggregator computes windows at build time (memory-cached only — SQLite still caches raw per-source snapshots); the controller adds `nextWindow` to summaries and `climbWindows`/`hourlyQuality`/`dailyDaylight` to detail.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core + SQLite, xUnit; Angular 21 zoneless signals, Vitest + jsdom.

**Spec:** `docs/superpowers/specs/2026-07-18-climb-window-finder-design.md`

**Spec corrections discovered during planning (exploration findings, spec still authoritative for behavior):**
- The catalog is **124 routes** (Cascades, Sierra, Wind River, Sawtooth, Wasatch, Colorado 14ers), not ~30. The seed table below covers all of them.
- `WeatherSnapshot` JSON is persisted per-source in SQLite (`CachedForecastEntity.PayloadJson`, camelCase). The `Next48Hours → Hourly` rename keeps the stored key via `[property: JsonPropertyName("next48Hours")]` so pre-deploy rows still deserialize (no NREs in the deploy→first-warm gap).
- `dailyDaylight` is computed **read-time in the controller** (mirroring the existing `daylight` field) rather than cached on `RouteConditions` — cached sunrise/sunset would freeze on stale rows.
- The seeder is add-only for existing rows, so `TypicalClimbHours` follows the existing `IsGlaciated` **reconcile** pattern (catalog dictionary synced onto existing rows at startup).
- The detail response keeps `forecastNext48h` at 48 entries (`Hourly.Take(48)`) — the existing hourly table and its payload contract don't change; the strip renders from the new `hourlyQuality` array instead.
- The spec's `NextWindowSummary` record is dropped: the controller picks the best upcoming window at request time and serializes it inline (an anonymous object, like every other DTO in `RoutesController`), so no C# record is needed.

---

## File map

**Create (backend):**
- `backend/RouteWeather.Core/Models/ClimbWindow.cs` — `ClimbWindow`, `HourlyQuality`, `NextWindowSummary` records
- `backend/RouteWeather.Core/Grading/WindowFinder.cs` — hour scoring, day frames, window detection, end-reasons
- `backend/RouteWeather.Core.Tests/Grading/WindowFinderTests.cs`
- `backend/RouteWeather.Core.Tests/Grading/HeadlineInvariantTests.cs` — 168h vs 48h-twin regression
- `backend/RouteWeather.Data/Migrations/<timestamp>_AddTypicalClimbHours.cs` (+ Designer, via `dotnet ef`)

**Modify (backend):**
- `backend/RouteWeather.Core/Models/WeatherSnapshot.cs` — rename `Next48Hours` → `Hourly` (JSON key kept), add `HeadlineHours` const
- `backend/RouteWeather.Core/Models/Route.cs` — append `TypicalClimbHours`
- `backend/RouteWeather.Core/Models/RouteConditions.cs` — append `Windows`, `HourlyQuality`
- `backend/RouteWeather.Core/Grading/GradeCalculator.cs` — `windowHours` capped at `HeadlineHours`
- `backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs` — rename refs; widen `Aggregate` to `internal`
- `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs` — rename refs; pin blended scalar derivation to first 48h
- `backend/RouteWeather.API/Services/OpenMeteoClient.cs` — `forecast_days=7`, 168h cap, scalars from first 48h
- `backend/RouteWeather.API/Services/NwsGridpointParser.cs` — 168h loop, scalars from first 48h
- `backend/RouteWeather.API/Services/ConditionsAggregator.cs` — call `WindowFinder`, pass `TypicalClimbHours`
- `backend/RouteWeather.API/Controllers/RoutesController.cs` — `nextWindow` on summary; `climbWindows`/`hourlyQuality`/`dailyDaylight` on detail
- `backend/RouteWeather.Data/Entities/RouteEntity.cs` — `TypicalClimbHours` column
- `backend/RouteWeather.Data/RouteSeeder.cs` — seed table + reconcile
- `backend/RouteWeather.Core.Tests/Data/RouteSeederTests.cs`, `backend/RouteWeather.API.Tests/*` — updated/expanded tests

**Create (frontend):**
- `frontend/src/app/components/climb-window-hero/climb-window-hero.{ts,html,scss,spec.ts}`
- `frontend/src/app/components/week-strip/week-strip.{ts,html,scss,spec.ts}`

**Modify (frontend):**
- `frontend/src/app/models/route-conditions.ts` — new interfaces + `nextWindow` on `RouteSummary`
- `frontend/src/app/components/route-card/route-card.{ts,html,scss,spec.ts}` — window line
- `frontend/src/app/pages/peak-detail/peak-detail.{ts,html,scss}` — mount hero + strip
- Every spec with an inline `RouteSummary` builder (fixture sweep), `.claude/rules/testing.md`

---

### Task 1: Branch setup

Pre-condition: `feature/user-options` has merged to `dev` (the card/hero time formats use `SettingsService.clockFmt()` from that branch). Verify with `gh pr list --head feature/user-options --state merged` — if not merged, stop and ask the user.

**Files:** none (git only)

- [ ] **Step 1: Create the branch off dev**

```bash
git checkout dev && git pull && git checkout -b feature/climb-window-finder
```

- [ ] **Step 2: Commit the approved spec (written during brainstorming, intentionally left uncommitted)**

```bash
git add docs/superpowers/specs/2026-07-18-climb-window-finder-design.md docs/superpowers/plans/2026-07-18-climb-window-finder.md
git commit -m "docs: climb-window finder spec and implementation plan"
```

Expected: both files on the new branch; `git status` clean apart from unrelated local files.

---

### Task 2: Core records + `WeatherSnapshot.Hourly` rename

**Files:**
- Create: `backend/RouteWeather.Core/Models/ClimbWindow.cs`
- Modify: `backend/RouteWeather.Core/Models/WeatherSnapshot.cs`
- Modify (rename fallout): `backend/RouteWeather.Core/Grading/GradeCalculator.cs:37`, `backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs:17,52`, `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs:55,72,139,161,181`, `backend/RouteWeather.API/Services/OpenMeteoClient.cs:199,220,231`, `backend/RouteWeather.API/Services/NwsGridpointParser.cs:65`, `backend/RouteWeather.API/Controllers/RoutesController.cs:109`, plus any test references the compiler flags
- Test: existing suites (this is a behavior-preserving refactor; green tests are the verification)

- [ ] **Step 1: Create the new records**

`backend/RouteWeather.Core/Models/ClimbWindow.cs`:

```csharp
namespace RouteWeather.Core.Models;

/// <summary>
/// A contiguous stretch of climbable hours, clipped to one climbing day
/// (sunrise − 6h → sunset) and long enough for the route's typical summit-day push.
/// EndReason is a display-ready clause: "closes as storm energy builds",
/// "ends with daylight", "runs to the forecast edge".
/// </summary>
public record ClimbWindow(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    Grade Grade,
    int Score,
    string EndReason,
    bool LowConfidence);

/// <summary>Per-hour quality for the week strip. Score/grade come from the same factor machinery as the headline.</summary>
public record HourlyQuality(
    DateTimeOffset TimeUtc,
    int Score,
    bool Qualifies);

```

- [ ] **Step 2: Rename the hourly series and pin the headline window size**

Replace the whole of `backend/RouteWeather.Core/Models/WeatherSnapshot.cs` with:

```csharp
using System.Text.Json.Serialization;

namespace RouteWeather.Core.Models;

/// <summary>
/// Invariant: the scalar headline fields (WindMph, TempF, PrecipitationProbabilityPct,
/// MaxGustMph, MaxCapeJkg, PrecipAmountIn) always describe the FIRST HeadlineHours of
/// Hourly, even when Hourly extends to the full 7-day horizon. Builders (NWS parser,
/// OpenMeteo client, ConsensusCalculator) enforce this; HeadlineInvariantTests pins it.
/// The JSON name "next48Hours" is kept so per-source rows already persisted in the
/// SQLite forecast cache keep deserializing across the rename.
/// </summary>
public record WeatherSnapshot(
    double WindMph,
    double TempF,
    int PrecipitationProbabilityPct,
    [property: JsonPropertyName("next48Hours")] IReadOnlyList<HourlyForecast> Hourly,
    double? MaxGustMph = null,
    double? MaxCapeJkg = null,
    double? PrecipAmountIn = null)
{
    /// <summary>Hours the scalar headline fields describe (and the visible-window grades cover).</summary>
    public const int HeadlineHours = 48;
}

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

- [ ] **Step 3: Mechanically fix every `Next48Hours` reference**

Build and let the compiler enumerate them:

```bash
dotnet build backend/RouteWeather.API/RouteWeather.API.csproj
```

For each error, replace `.Next48Hours` with `.Hourly` and named argument `Next48Hours:` with `Hourly:`. Two call sites need more than the rename:

`GradeCalculator.cs` line 37 — the precip normalization must describe the headline window, not the full series:

```csharp
var windowHours = Math.Min(WeatherSnapshot.HeadlineHours, weather.Hourly.Count);
```

`RoutesController.cs` line 109 — the hourly-table payload stays 48 entries even when the series is longer:

```csharp
forecastNext48h = c.Weather?.Hourly.Take(WeatherSnapshot.HeadlineHours),
```

All other sites (WindowGradeCalculator lines 17/52, ConsensusCalculator lines 55/139/161/181, OpenMeteoClient lines 199/220/231, NwsGridpointParser line 65, tests, `TestData.Snapshot()`) are a pure rename — `Take(12/24/48)` slices already operate on the front of the list.

- [ ] **Step 4: Run the backend suites**

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```

Expected: PASS (identical behavior — only the property name changed). If the user's API is running locally, stop it first or these builds fail on the locked Core DLL.

- [ ] **Step 5: Commit**

```bash
git add -u backend && git add backend/RouteWeather.Core/Models/ClimbWindow.cs
git commit -m "refactor(core): rename WeatherSnapshot.Next48Hours to Hourly; add climb-window records"
```

---

### Task 3: Clients emit the 7-day series (scalars pinned to first 48h)

**Files:**
- Modify: `backend/RouteWeather.API/Services/OpenMeteoClient.cs`
- Modify: `backend/RouteWeather.API/Services/NwsGridpointParser.cs`
- Modify: `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs`
- Test: `backend/RouteWeather.API.Tests/NwsGridpointParserTests.cs`, `backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs`

The pinning rule everywhere is **time-based, not count-based** (sparse series can skip hours): head = hours with `Time < seriesStart + 48h`. If the head is empty the builder returns null, exactly like today's no-data path.

- [ ] **Step 1: Write the failing NWS parser test**

Append to `backend/RouteWeather.API.Tests/NwsGridpointParserTests.cs` (reuse the file's existing helper for building a gridpoint JSON payload — it already builds `temperature`/`windSpeed` layers with `validTime` strings; extend the temperature layer's duration to cover 168h):

```csharp
[Fact]
public void Parse_series_beyond_48h_extends_hourly_but_pins_scalars_to_first_48()
{
    var now = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);
    // 168h of 50°F temps and calm wind, except hour 100 is 90°F with 60 mph wind.
    // If scalars leak past 48h, WindMph becomes 60 and TempF stays 50 (min unchanged)
    // — so assert on WindMph and on the hourly count.
    var json = BuildGridpointJson(
        tempF: Enumerable.Repeat(50.0, 168).Select((v, i) => i == 100 ? 90.0 : v).ToArray(),
        windMph: Enumerable.Repeat(5.0, 168).Select((v, i) => i == 100 ? 60.0 : v).ToArray(),
        startUtc: now);

    using var doc = JsonDocument.Parse(json);
    var snap = NwsGridpointParser.Parse(doc.RootElement, now);

    Assert.NotNull(snap);
    Assert.Equal(168, snap!.Hourly.Count);
    Assert.Equal(5.0, snap.WindMph);            // scalar ignores hour 100
    Assert.Equal(60.0, snap.Hourly[100].WindMph); // series carries it
}
```

If the file has no reusable JSON builder, add this private helper to the test class:

```csharp
private static string BuildGridpointJson(double[] tempF, double[] windMph, DateTimeOffset startUtc)
{
    string Iso(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:ssK");
    string Layer(string uom, double[] values) =>
        $"{{\"uom\":\"{uom}\",\"values\":[" +
        string.Join(',', values.Select((v, i) =>
            $"{{\"validTime\":\"{Iso(startUtc.AddHours(i))}/PT1H\",\"value\":{v.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}")) +
        "]}";
    return $"{{\"properties\":{{\"temperature\":{Layer("wmoUnit:degF", tempF)},\"windSpeed\":{Layer("wmoUnit:km_h-1", windMph.Select(w => w / 0.621371).ToArray())}}}}}";
}
```

- [ ] **Step 2: Run it to make sure it fails**

```bash
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter Parse_series_beyond_48h
```

Expected: FAIL — `Hourly.Count` is 48 (parser still loops 48) .

- [ ] **Step 3: Extend the parser**

In `backend/RouteWeather.API/Services/NwsGridpointParser.cs`, replace the loop bound and the scalar block (lines 36–68):

```csharp
        const int horizonHours = 168;
        var start = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, TimeSpan.Zero);
        var hours = new List<HourlyForecast>(horizonHours);
        for (var i = 0; i < horizonHours; i++)
        {
            // ... existing loop body unchanged ...
        }

        if (hours.Count == 0) return null;

        // Scalars describe the headline window only (see WeatherSnapshot invariant).
        var headCutoff = start.AddHours(WeatherSnapshot.HeadlineHours);
        var head = hours.Where(h => h.Time < headCutoff).ToList();
        if (head.Count == 0) return null;

        var gusts = head.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var amounts = head.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: head.Max(h => h.WindMph),
            TempF: head.Min(h => h.TempF),
            PrecipitationProbabilityPct: head.Max(h => h.PrecipitationProbabilityPct),
            Hourly: hours,
            MaxGustMph: gusts.Count == 0 ? null : gusts.Max(),
            MaxCapeJkg: null,
            PrecipAmountIn: amounts.Count == 0 ? null : amounts.Sum());
```

(NWS data typically ends near hour 156 — the loop just stops finding temps.)

- [ ] **Step 4: OpenMeteo — 7-day URL + same pinning**

In `backend/RouteWeather.API/Services/OpenMeteoClient.cs`:

Line 138, the URL constant:

```csharp
        "&forecast_days=7&timezone=UTC";
```

In `BuildSnapshot` (lines 163–202): change the cap and the scalar block. Rename the local `hourly48` to `series`:

```csharp
        var count = Math.Min(168, Math.Min(times.Count, tempSeries.Count));
        if (count == 0) return null;

        var series = new List<HourlyForecast>(count);
        for (var i = 0; i < count; i++)
        {
            // ... existing loop body unchanged, appending to series ...
        }

        if (series.Count == 0) return null;

        var headCutoff = times[0].AddHours(WeatherSnapshot.HeadlineHours);
        var head = series.Where(h => h.Time < headCutoff).ToList();
        if (head.Count == 0) return null;

        var gusts = head.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var capes = head.Where(h => h.CapeJkg.HasValue).Select(h => h.CapeJkg!.Value).ToList();
        var amounts = head.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: head.Max(h => h.WindMph),
            TempF: head.Min(h => h.TempF),
            PrecipitationProbabilityPct: 0,
            Hourly: series,
            MaxGustMph: gusts.Count == 0 ? null : gusts.Max(),
            MaxCapeJkg: capes.Count == 0 ? null : capes.Max(),
            PrecipAmountIn: amounts.Count == 0 ? null : amounts.Sum());
```

In `OverlayGfsProbability` (lines 208–232), pin the headline PoP the same way:

```csharp
        var hourly = snapshot.Hourly.Select(h =>
            probByTime.TryGetValue(h.Time, out var pct)
                ? h with { PrecipitationProbabilityPct = pct }
                : h).ToList();

        var headCutoff = hourly.Count == 0 ? default : hourly[0].Time.AddHours(WeatherSnapshot.HeadlineHours);
        return snapshot with
        {
            PrecipitationProbabilityPct = hourly.Count == 0 ? 0
                : hourly.Where(h => h.Time < headCutoff).Max(h => h.PrecipitationProbabilityPct),
            Hourly = hourly,
        };
```

Note: multi-model responses pad each model's series with nulls past its own horizon (HRRR ~48h); the existing `if (!t.HasValue) continue;` already drops those hours, so short-horizon models naturally contribute short series.

- [ ] **Step 5: Pin the blended scalars in `ConsensusCalculator.BlendSnapshots`**

Replace the derivation block (lines 57–75):

```csharp
        // Blended scalars keep the WeatherSnapshot invariant: they describe the first
        // HeadlineHours of the blended series even when sources extend to 7 days.
        var headCutoff = blendedHours.Count == 0
            ? default
            : blendedHours[0].Time.AddHours(WeatherSnapshot.HeadlineHours);
        var head = blendedHours.Where(h => h.Time < headCutoff).ToList();

        var blendedGusts = head.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var blendedCapes = head.Where(h => h.CapeJkg.HasValue).Select(h => h.CapeJkg!.Value).ToList();
        var blendedAmounts = head.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: Math.Round(wind),
            TempF: Math.Round(temp),
            PrecipitationProbabilityPct: head.Count == 0
                ? (int)Math.Round(WeightedMean(precipInputs, s => s.PrecipitationProbabilityPct))
                : head.Max(h => h.PrecipitationProbabilityPct),
            Hourly: blendedHours,
            MaxGustMph: blendedGusts.Count == 0 ? null : blendedGusts.Max(),
            MaxCapeJkg: blendedCapes.Count == 0 ? null : blendedCapes.Max(),
            PrecipAmountIn: blendedAmounts.Count == 0 ? null : blendedAmounts.Sum());
```

(`WindMph`/`TempF` are weighted means of source scalars, which Steps 3–4 already pinned upstream.)

- [ ] **Step 6: Run the full backend suites**

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```

Expected: PASS, including the new parser test.

- [ ] **Step 7: Commit**

```bash
git add -u backend
git commit -m "feat(sources): 7-day hourly horizon with headline scalars pinned to first 48h"
```

---

### Task 4: Headline-invariant regression test

**Files:**
- Create: `backend/RouteWeather.Core.Tests/Grading/HeadlineInvariantTests.cs`

This is the PR #3 aggregation-window invariant, pinned by construction: a 168h pipeline run and its first-48h twin must produce identical headline output.

- [ ] **Step 1: Write the test**

```csharp
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class HeadlineInvariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);

    /// Varied-but-plausible series: benign first 48h, deteriorating back half —
    /// exactly the case where a leak past 48h would change the headline.
    private static List<HourlyForecast> Series(int hours) =>
        Enumerable.Range(0, hours).Select(i => new HourlyForecast(
            Time: T0.AddHours(i),
            TempF: i < 48 ? 45 : 20,
            WindMph: i < 48 ? 8 : 45,
            PrecipitationProbabilityPct: i < 48 ? 10 : 90,
            ShortForecast: "Test",
            GustMph: i < 48 ? 12 : 70,
            CapeJkg: i < 48 ? 100 : 2500,
            PrecipitationIn: i < 48 ? 0.0 : 0.2)).ToList();

    private static WeatherSnapshot Snapshot(int hours)
    {
        var series = Series(hours);
        var head = series.Where(h => h.Time < T0.AddHours(WeatherSnapshot.HeadlineHours)).ToList();
        return new WeatherSnapshot(
            WindMph: head.Max(h => h.WindMph),
            TempF: head.Min(h => h.TempF),
            PrecipitationProbabilityPct: head.Max(h => h.PrecipitationProbabilityPct),
            Hourly: series,
            MaxGustMph: head.Max(h => h.GustMph!.Value),
            MaxCapeJkg: head.Max(h => h.CapeJkg!.Value),
            PrecipAmountIn: head.Sum(h => h.PrecipitationIn!.Value));
    }

    [Fact]
    public void Headline_grade_is_identical_for_168h_series_and_its_48h_twin()
    {
        var full = GradeCalculator.Compute(Snapshot(168), snowpack: null);
        var twin = GradeCalculator.Compute(Snapshot(48), snowpack: null);

        Assert.Equal(twin.Grade, full.Grade);
        Assert.Equal(twin.OverallScore, full.OverallScore);
        Assert.Equal(twin.Rationale, full.Rationale);
        Assert.Equal(
            twin.Factors.Select(f => (f.Name, f.Score, f.Detail)),
            full.Factors.Select(f => (f.Name, f.Score, f.Detail)));
    }

    [Fact]
    public void Window_grades_are_identical_for_168h_series_and_its_48h_twin()
    {
        var full = WindowGradeCalculator.Compute(Snapshot(168), snowpack: null);
        var twin = WindowGradeCalculator.Compute(Snapshot(48), snowpack: null);

        foreach (var (f, t) in new[] { (full.Next12h, twin.Next12h), (full.Next24h, twin.Next24h), (full.Next48h, twin.Next48h) })
        {
            Assert.Equal(t.Grade, f.Grade);
            Assert.Equal(t.OverallScore, f.OverallScore);
            Assert.Equal(t.HoursCovered, f.HoursCovered);
        }
    }

    [Fact]
    public void Blended_consensus_scalars_are_identical_for_168h_sources_and_their_48h_twins()
    {
        EnsembleResult Run(int hours)
        {
            var inputs = new[]
            {
                new ConsensusInput(new SourceSnapshot("A", Snapshot(hours), T0, ForecastFactors.All), 1.75),
                new ConsensusInput(new SourceSnapshot("B", Snapshot(hours), T0, ForecastFactors.All), 1.0),
            };
            return new ConsensusCalculator().Compute(inputs, 2);
        }

        var full = Run(168).Blended!;
        var twin = Run(48).Blended!;

        Assert.Equal(twin.WindMph, full.WindMph);
        Assert.Equal(twin.TempF, full.TempF);
        Assert.Equal(twin.PrecipitationProbabilityPct, full.PrecipitationProbabilityPct);
        Assert.Equal(twin.MaxGustMph, full.MaxGustMph);
        Assert.Equal(twin.MaxCapeJkg, full.MaxCapeJkg);
        Assert.Equal(twin.PrecipAmountIn, full.PrecipAmountIn);
    }
}
```

- [ ] **Step 2: Run it**

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter HeadlineInvariant
```

Expected: PASS (Tasks 2–3 already enforce the invariant; this pins it). If any assert fails, a scalar leaked past 48h — fix the builder, not the test.

- [ ] **Step 3: Commit**

```bash
git add backend/RouteWeather.Core.Tests/Grading/HeadlineInvariantTests.cs
git commit -m "test(core): pin headline invariant across 7-day horizon extension"
```

---

### Task 5: `WindowFinder`

**Files:**
- Create: `backend/RouteWeather.Core/Grading/WindowFinder.cs`
- Modify: `backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs:42` (`private static` → `internal static` on `Aggregate`)
- Test: `backend/RouteWeather.Core.Tests/Grading/WindowFinderTests.cs`

- [ ] **Step 1: Write the failing tests**

`backend/RouteWeather.Core.Tests/Grading/WindowFinderTests.cs`:

```csharp
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class WindowFinderTests
{
    // Mt Baker-ish coords; July UTC sunrise ≈ 12:30Z, sunset ≈ 04:10Z (next UTC day),
    // so each climbing frame is roughly [06:30Z, 04:10Z next day].
    private const double Lat = 48.777;
    private const double Lon = -121.813;
    // 18:00Z (11 am PDT) — deliberately mid-frame, so the 168h horizon cuts the final
    // frame short and the "runs to the forecast edge" end-reason is reachable.
    private static readonly DateTimeOffset T0 = new(2026, 7, 20, 18, 0, 0, TimeSpan.Zero);

    private static HourlyForecast Good(DateTimeOffset t) =>
        new(t, TempF: 45, WindMph: 8, PrecipitationProbabilityPct: 5, "Clear",
            GustMph: 12, CapeJkg: 50, PrecipitationIn: 0.0);

    // Precip prob kept moderate (30) so Thunderstorm is unambiguously the worst factor
    // and the end-reason assertion can't flip to the precip phrase.
    private static HourlyForecast Stormy(DateTimeOffset t) =>
        new(t, TempF: 45, WindMph: 10, PrecipitationProbabilityPct: 30, "Thunderstorm",
            GustMph: 20, CapeJkg: 2600, PrecipitationIn: 0.05);

    private static HourlyForecast Windy(DateTimeOffset t) =>
        new(t, TempF: 40, WindMph: 45, PrecipitationProbabilityPct: 5, "Windy",
            GustMph: 65, CapeJkg: 50, PrecipitationIn: 0.0);

    /// Build a 168h snapshot from a per-index chooser.
    private static WeatherSnapshot Snap(Func<int, DateTimeOffset, HourlyForecast> hour)
    {
        var series = Enumerable.Range(0, 168).Select(i => hour(i, T0.AddHours(i))).ToList();
        var head = series.Take(48).ToList();
        return new WeatherSnapshot(
            head.Max(h => h.WindMph), head.Min(h => h.TempF), head.Max(h => h.PrecipitationProbabilityPct),
            series,
            head.Max(h => h.GustMph!.Value), head.Max(h => h.CapeJkg!.Value), head.Sum(h => h.PrecipitationIn!.Value));
    }

    private static IReadOnlyList<ClimbWindow> Find(WeatherSnapshot snap, double typicalHours = 8) =>
        WindowFinder.Find(snap, snowpack: null, airQuality: null, typicalHours, Lat, Lon);

    [Fact]
    public void All_good_week_yields_one_window_per_day_clipped_to_climbing_frames()
    {
        var windows = Find(Snap((_, t) => Good(t)));

        Assert.InRange(windows.Count, 6, 8);          // one per climbing day in the horizon
        Assert.All(windows, w => Assert.True(w.EndUtc - w.StartUtc >= TimeSpan.FromHours(8)));
        Assert.All(windows, w => Assert.Equal(Grade.A, w.Grade));
        // Chronological order.
        Assert.Equal(windows.OrderBy(w => w.StartUtc).Select(w => w.StartUtc), windows.Select(w => w.StartUtc));
        // Every window ends by that day's sunset — no window spans a night.
        Assert.All(windows, w => Assert.True((w.EndUtc - w.StartUtc) <= TimeSpan.FromHours(24)));
    }

    [Fact]
    public void Storm_hours_do_not_qualify_and_split_the_day()
    {
        // Storms from hour 30 onward kill every later day; only the first climbing day survives.
        var windows = Find(Snap((i, t) => i < 30 ? Good(t) : Stormy(t)));

        Assert.All(windows, w => Assert.True(w.EndUtc <= T0.AddHours(31)));
    }

    [Fact]
    public void Windows_shorter_than_typical_climb_hours_are_dropped()
    {
        var all = Find(Snap((_, t) => Good(t)), typicalHours: 8);
        var strict = Find(Snap((_, t) => Good(t)), typicalHours: 22);

        Assert.NotEmpty(all);
        Assert.Empty(strict);                          // no frame is 22h long
    }

    [Fact]
    public void Night_only_good_runs_never_become_windows()
    {
        // Good ONLY between sunset and the next sunrise-6h (roughly hours 22..30 UTC daily): use
        // a chooser that is Windy during every climbing frame and Good otherwise.
        // Frames at this lat/lon in July: [sunrise-6h ≈ 06:30Z, sunset ≈ 04:10Z next day] — nearly
        // all day. Construct the inverse precisely: Good 04:30Z–06:30Z only (2h nightly slack).
        var windows = Find(Snap((_, t) =>
        {
            var frameStart = new DateTimeOffset(t.Year, t.Month, t.Day, 6, 30, 0, TimeSpan.Zero);
            var inSlack = t >= frameStart.AddHours(-2) && t < frameStart;
            return inSlack ? Good(t) : Windy(t);
        }), typicalHours: 2);

        Assert.Empty(windows);
    }

    [Fact]
    public void End_reason_names_the_disqualifying_factor()
    {
        // Good until hour 32, storms after.
        var windows = Find(Snap((i, t) => i < 32 ? Good(t) : Stormy(t)));

        var last = windows.Last();
        Assert.Equal("closes as storm energy builds", last.EndReason);
    }

    [Fact]
    public void End_reason_is_daylight_when_the_run_continues_past_sunset()
    {
        var windows = Find(Snap((_, t) => Good(t)));

        // Interior windows are clipped by their frame while hours stay good.
        Assert.Contains(windows, w => w.EndReason == "ends with daylight");
    }

    [Fact]
    public void End_reason_is_forecast_edge_at_the_horizon()
    {
        var windows = Find(Snap((_, t) => Good(t)));
        Assert.Equal("runs to the forecast edge", windows.Last().EndReason);
    }

    [Fact]
    public void Low_confidence_flags_windows_with_midpoint_past_96h()
    {
        var windows = Find(Snap((_, t) => Good(t)));

        Assert.Contains(windows, w => !w.LowConfidence);
        Assert.Contains(windows, w => w.LowConfidence);
        Assert.All(windows, w =>
        {
            var mid = w.StartUtc + (w.EndUtc - w.StartUtc) / 2;
            Assert.Equal(mid > T0.AddHours(96), w.LowConfidence);
        });
    }

    [Fact]
    public void Score_hours_qualify_only_at_post_cap_grade_B_or_better()
    {
        var snap = Snap((i, t) => i == 10 ? Stormy(t) : Good(t));
        var scored = WindowFinder.ScoreHours(snap, snowpack: null, airQuality: null);

        Assert.Equal(168, scored.Count);
        Assert.False(scored[10].Qualifies);            // capped by thunderstorm
        Assert.True(scored[11].Qualifies);
    }

    [Fact]
    public void Null_weather_returns_empty()
    {
        Assert.Empty(WindowFinder.Find(null, null, null, 8, Lat, Lon));
        Assert.Empty(WindowFinder.ScoreHours(null, null, null));
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter WindowFinderTests
```

Expected: FAIL — `WindowFinder` does not exist.

- [ ] **Step 3: Widen `WindowGradeCalculator.Aggregate`**

`backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs` line 42: `private static WeatherSnapshot Aggregate(` → `internal static WeatherSnapshot Aggregate(`.

- [ ] **Step 4: Implement `WindowFinder`**

`backend/RouteWeather.Core/Grading/WindowFinder.cs`:

```csharp
using RouteWeather.Core.Models;
using RouteWeather.Core.Services;

namespace RouteWeather.Core.Grading;

/// <summary>
/// Finds climbable windows in a (up to 7-day) hourly series. Pure and clock-free:
/// "low confidence" is anchored to the series start, and callers filter
/// already-past windows at read time.
/// </summary>
public static class WindowFinder
{
    /// <summary>Hours before sunrise a climbing day starts (alpine-start slack).</summary>
    private const double FrameLeadHours = 6;
    /// <summary>Window midpoints beyond this many hours from series start are low-confidence.</summary>
    private const double ConfidenceHorizonHours = 96;

    public static IReadOnlyList<HourlyQuality> ScoreHours(
        WeatherSnapshot? weather, SnowpackSnapshot? snowpack, AirQualitySnapshot? airQuality)
    {
        if (weather is null || weather.Hourly.Count == 0) return Array.Empty<HourlyQuality>();

        var result = new List<HourlyQuality>(weather.Hourly.Count);
        foreach (var hour in weather.Hourly)
        {
            var single = WindowGradeCalculator.Aggregate(new[] { hour });
            var graded = GradeCalculator.Compute(single, snowpack, airQuality);
            // Post-cap grade folds both score and caps: B-or-better means "the site says go".
            var qualifies = graded.Grade is Grade.A or Grade.B;
            result.Add(new HourlyQuality(hour.Time, graded.OverallScore, qualifies));
        }
        return result;
    }

    public static IReadOnlyList<ClimbWindow> Find(
        WeatherSnapshot? weather,
        SnowpackSnapshot? snowpack,
        AirQualitySnapshot? airQuality,
        double typicalClimbHours,
        double lat,
        double lon)
    {
        if (weather is null || weather.Hourly.Count == 0 || typicalClimbHours <= 0)
            return Array.Empty<ClimbWindow>();

        var hours = weather.Hourly;
        var scored = ScoreHours(weather, snowpack, airQuality);
        var seriesStart = hours[0].Time;
        var seriesEnd = hours[^1].Time.AddHours(1);

        var runs = QualifyingRuns(hours, scored);
        var frames = ClimbingFrames(lat, lon, seriesStart, seriesEnd);

        var windows = new List<ClimbWindow>();
        foreach (var frame in frames)
        {
            foreach (var run in runs)
            {
                var start = run.Start > frame.Start ? run.Start : frame.Start;
                var end = run.End < frame.End ? run.End : frame.End;
                if ((end - start).TotalHours < typicalClimbHours) continue;

                windows.Add(BuildWindow(hours, scored, snowpack, airQuality,
                    start, end, run, frame, seriesStart, seriesEnd));
            }
        }
        return windows.OrderBy(w => w.StartUtc).ToList();
    }

    private sealed record Run(DateTimeOffset Start, DateTimeOffset End, int FirstIndex, int LastIndex);
    private sealed record Frame(DateTimeOffset Start, DateTimeOffset End);

    /// Contiguous stretches of qualifying hours. An hour h covers [h, h+1); a gap in
    /// the series (missing hour) breaks the run.
    private static List<Run> QualifyingRuns(IReadOnlyList<HourlyForecast> hours, IReadOnlyList<HourlyQuality> scored)
    {
        var runs = new List<Run>();
        int? runStart = null;
        for (var i = 0; i < hours.Count; i++)
        {
            var brokeContinuity = i > 0 && hours[i].Time - hours[i - 1].Time != TimeSpan.FromHours(1);
            if (runStart is not null && (brokeContinuity || !scored[i].Qualifies))
            {
                runs.Add(MakeRun(hours, runStart.Value, i - 1));
                runStart = null;
            }
            if (scored[i].Qualifies && runStart is null) runStart = i;
        }
        if (runStart is not null) runs.Add(MakeRun(hours, runStart.Value, hours.Count - 1));
        return runs;
    }

    private static Run MakeRun(IReadOnlyList<HourlyForecast> hours, int first, int last) =>
        new(hours[first].Time, hours[last].Time.AddHours(1), first, last);

    /// One frame per UTC date the horizon touches: [sunrise − FrameLeadHours, sunset].
    private static List<Frame> ClimbingFrames(double lat, double lon, DateTimeOffset start, DateTimeOffset end)
    {
        var frames = new List<Frame>();
        for (var date = DateOnly.FromDateTime(start.UtcDateTime).AddDays(-1);
             date <= DateOnly.FromDateTime(end.UtcDateTime);
             date = date.AddDays(1))
        {
            var daylight = SolarCalculator.ComputeUtc(lat, lon, date);
            if (daylight is null) continue; // polar day/night
            var frame = new Frame(daylight.SunriseUtc.AddHours(-FrameLeadHours), daylight.SunsetUtc);
            if (frame.End <= start || frame.Start >= end) continue;
            frames.Add(frame);
        }
        return frames;
    }

    private static ClimbWindow BuildWindow(
        IReadOnlyList<HourlyForecast> hours,
        IReadOnlyList<HourlyQuality> scored,
        SnowpackSnapshot? snowpack,
        AirQualitySnapshot? airQuality,
        DateTimeOffset start,
        DateTimeOffset end,
        Run run,
        Frame frame,
        DateTimeOffset seriesStart,
        DateTimeOffset seriesEnd)
    {
        var slice = hours.Where(h => h.Time >= start && h.Time < end).ToList();
        var graded = GradeCalculator.Compute(WindowGradeCalculator.Aggregate(slice), snowpack, airQuality);

        var midpoint = start + (end - start) / 2;
        var lowConfidence = (midpoint - seriesStart).TotalHours > ConfidenceHorizonHours;

        return new ClimbWindow(start, end, graded.Grade, graded.OverallScore,
            EndReason(hours, scored, snowpack, airQuality, end, run, frame, seriesEnd), lowConfidence);
    }

    private static string EndReason(
        IReadOnlyList<HourlyForecast> hours,
        IReadOnlyList<HourlyQuality> scored,
        SnowpackSnapshot? snowpack,
        AirQualitySnapshot? airQuality,
        DateTimeOffset end,
        Run run,
        Frame frame,
        DateTimeOffset seriesEnd)
    {
        // Clipped by the data horizon while still good.
        if (end >= seriesEnd && run.End >= seriesEnd) return "runs to the forecast edge";
        // Clipped by sunset while the run keeps going.
        if (end >= frame.End && run.End > frame.End) return "ends with daylight";

        // Otherwise the run itself ended: name the factor that broke it.
        var nextIndex = run.LastIndex + 1;
        if (nextIndex >= hours.Count) return "runs to the forecast edge";

        var single = WindowGradeCalculator.Aggregate(new[] { hours[nextIndex] });
        var graded = GradeCalculator.Compute(single, snowpack, airQuality);
        // Worst-scoring active factor. (A cap in a factor's neutral band can technically
        // out-vote it, but naming the worst factor is the right v1 message either way.)
        var culprit = graded.Factors.Where(f => f.IsActive).OrderBy(f => f.Score).FirstOrDefault();
        return Phrase(culprit?.Name);
    }

    private static string Phrase(string? factorName) => factorName switch
    {
        "Thunderstorm" => "closes as storm energy builds",
        "Wind" or "Gusts" => "closes as wind picks up",
        "Precipitation" => "closes as precip moves in",
        "Temperature" => "closes as temps turn harsh",
        "Air quality" => "closes as smoke thickens",
        "Recent snow" => "closes on fresh snow",
        "Snowpack" => "closes on snowpack conditions",
        _ => "closes as conditions deteriorate",
    };
}
```

- [ ] **Step 5: Run the tests; iterate until green**

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter WindowFinderTests
```

Expected: PASS. Likely first-run failures to check against *behavior, not the test*: frame boundary off-by-one (an hour at exactly `frame.End` must not count — `h.Time < end` handles it) and the `QualifyingRuns` gap logic (walk it with a 3-hour gapped fixture if the night-only test fails).

- [ ] **Step 6: Commit**

```bash
git add backend/RouteWeather.Core/Grading/WindowFinder.cs backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs backend/RouteWeather.Core.Tests/Grading/WindowFinderTests.cs
git commit -m "feat(core): WindowFinder — climbable-window detection over the 7-day series"
```

---

### Task 6: `TypicalClimbHours` — entity, migration, catalog, reconcile

**Files:**
- Modify: `backend/RouteWeather.Data/Entities/RouteEntity.cs`
- Modify: `backend/RouteWeather.Core/Models/Route.cs`
- Modify: `backend/RouteWeather.Data/RouteSeeder.cs`
- Modify: `backend/RouteWeather.API.Tests/TestData.cs` (route fixture gets a duration)
- Create: migration `AddTypicalClimbHours` (+ Designer/snapshot via `dotnet ef`)
- Test: `backend/RouteWeather.Core.Tests/Data/RouteSeederTests.cs`

- [ ] **Step 1: Write the failing seeder tests**

Append to `backend/RouteWeather.Core.Tests/Data/RouteSeederTests.cs` (same `NewContext()` helper the file already has):

```csharp
    [Fact]
    public async Task Every_seeded_route_has_a_positive_typical_climb_duration()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var missing = await db.Routes
            .Where(r => r.TypicalClimbHours <= 0)
            .Select(r => r.Slug)
            .ToListAsync();

        Assert.True(missing.Count == 0, $"Routes without TypicalClimbHours: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("mount-rainier", 12)]   // glaciated camp-to-camp push
    [InlineData("liberty-bell", 7)]     // short alpine rock day
    [InlineData("capitol-peak", 14)]    // longest CO 14er standard route
    [InlineData("mount-sherman", 5)]    // shortest CO 14er
    public async Task Spot_check_typical_climb_hours(string slug, double expected)
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var route = await db.Routes.SingleAsync(r => r.Slug == slug);
        Assert.Equal(expected, route.TypicalClimbHours);
    }

    [Fact]
    public async Task Reconciles_TypicalClimbHours_on_existing_rows()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        // A pre-migration DB looks like this: column defaulted to 0.
        var rainier = await db.Routes.SingleAsync(r => r.Slug == "mount-rainier");
        rainier.TypicalClimbHours = 0;
        await db.SaveChangesAsync();

        await RouteSeeder.SeedAsync(db);

        var after = await db.Routes.SingleAsync(r => r.Slug == "mount-rainier");
        Assert.Equal(12, after.TypicalClimbHours);
    }
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter TypicalClimbHours
```

Expected: FAIL — `RouteEntity.TypicalClimbHours` doesn't compile yet. (Compile failure counts as the red step here.)

- [ ] **Step 3: Add the column and the Core field**

`backend/RouteWeather.Data/Entities/RouteEntity.cs` — after `IsGlaciated`:

```csharp
    /// <summary>Typical summit-day push in hours (car-to-car or camp-to-camp), slow end of guidebook ranges.</summary>
    public double TypicalClimbHours { get; set; }
```

`backend/RouteWeather.Core/Models/Route.cs` — append an optional positional param (optional so existing test constructions keep compiling):

```csharp
public record Route(
    string Slug,
    string Mountain,
    string RouteName,
    int SummitElevationFt,
    double SummitLat,
    double SummitLon,
    string ClassDifficulty,
    string SnotelStationTriplet,
    double TypicalClimbHours = 0);
```

`backend/RouteWeather.API.Tests/TestData.cs` — in `Route(...)`, after `SnotelStationTriplet`:

```csharp
        TypicalClimbHours = 8,
```

- [ ] **Step 4: Seeder catalog + reconcile**

In `backend/RouteWeather.Data/RouteSeeder.cs`, add below `GlaciatedSlugs` (values are hours for the *summit-day push*, slow end of guidebook ranges — **the user reviews this table before the task is executed**):

```csharp
    // Typical summit-day push (car-to-car or camp-to-camp), hours, slow end of
    // guidebook ranges. Single source of truth; reconciled onto existing rows.
    private static readonly Dictionary<string, double> TypicalClimbHoursBySlug = new()
    {
        // Cascades
        ["mount-rainier"] = 12, ["mount-hood"] = 9, ["mount-adams"] = 11, ["mount-baker"] = 10,
        ["mount-shasta"] = 11, ["glacier-peak"] = 14, ["mount-st-helens"] = 9, ["mount-shuksan"] = 14,
        ["mount-stuart"] = 14, ["forbidden-peak"] = 14, ["dragontail-peak"] = 14, ["eldorado-peak"] = 12,
        ["sahale-peak"] = 10, ["liberty-bell"] = 7, ["bonanza-peak"] = 14, ["goode-mountain"] = 16,
        ["black-peak"] = 10, ["sloan-peak"] = 12, ["silver-star-mountain"] = 10, ["south-sister"] = 9,
        ["north-sister"] = 12, ["mount-jefferson"] = 14, ["mount-thielsen"] = 8, ["mount-mcloughlin"] = 8,
        ["lassen-peak"] = 6,
        // Sierra
        ["mount-whitney"] = 14, ["mount-williamson"] = 16, ["north-palisade"] = 14, ["mount-sill"] = 14,
        ["mount-russell"] = 12, ["mount-langley"] = 11, ["mount-conness"] = 12, ["cathedral-peak"] = 8,
        ["matterhorn-peak"] = 12, ["mount-dana"] = 7, ["mount-lyell"] = 12, ["mount-ritter"] = 12,
        ["banner-peak"] = 12, ["mount-humphreys"] = 12, ["mount-darwin"] = 14, ["temple-crag"] = 14,
        ["bear-creek-spire"] = 12, ["mount-brewer"] = 14, ["middle-palisade"] = 14, ["mount-tyndall"] = 14,
        // Wind River
        ["gannett-peak"] = 12, ["fremont-peak"] = 10, ["mount-helen"] = 12, ["mount-sacagawea"] = 12,
        ["wind-river-peak"] = 14,
        // Sawtooth
        ["thompson-peak"] = 8, ["mount-heyburn"] = 8, ["mount-cramer"] = 10, ["williams-peak"] = 8,
        ["snowyside-peak"] = 9,
        // Wasatch
        ["mount-timpanogos"] = 8, ["mount-nebo"] = 7, ["lone-peak"] = 10, ["pfeifferhorn"] = 8,
        ["mount-olympus"] = 6, ["box-elder-peak"] = 7, ["broads-fork-twin-peaks"] = 9, ["dromedary-peak"] = 8,
        ["sunrise-peak"] = 8, ["mount-superior"] = 7, ["mount-raymond"] = 7,
        // Colorado 14ers
        ["mount-elbert"] = 8, ["mount-massive"] = 9, ["mount-harvard"] = 10, ["blanca-peak"] = 10,
        ["la-plata-peak"] = 8, ["uncompahgre-peak"] = 7, ["crestone-peak"] = 12, ["mount-lincoln"] = 6,
        ["grays-peak"] = 6, ["mount-antero"] = 9, ["torreys-peak"] = 7, ["castle-peak"] = 9,
        ["quandary-peak"] = 6, ["mount-evans"] = 7, ["longs-peak"] = 13, ["mount-wilson"] = 12,
        ["mount-cameron"] = 6, ["mount-shavano"] = 8, ["mount-belford"] = 8, ["crestone-needle"] = 12,
        ["mount-princeton"] = 8, ["mount-yale"] = 8, ["mount-bross"] = 6, ["kit-carson-peak"] = 12,
        ["el-diente-peak"] = 12, ["maroon-peak"] = 12, ["tabeguache-peak"] = 10, ["mount-oxford"] = 9,
        ["mount-sneffels"] = 8, ["mount-democrat"] = 6, ["capitol-peak"] = 14, ["pikes-peak"] = 12,
        ["snowmass-mountain"] = 13, ["mount-eolus"] = 12, ["windom-peak"] = 11, ["challenger-point"] = 11,
        ["mount-columbia"] = 9, ["missouri-mountain"] = 9, ["humboldt-peak"] = 8, ["mount-bierstadt"] = 6,
        ["conundrum-peak"] = 10, ["sunlight-peak"] = 12, ["handies-peak"] = 6, ["culebra-peak"] = 7,
        ["ellingwood-point"] = 10, ["mount-lindsey"] = 9, ["north-eolus"] = 11, ["little-bear-peak"] = 12,
        ["mount-sherman"] = 5, ["redcloud-peak"] = 8, ["pyramid-peak"] = 12, ["wilson-peak"] = 10,
        ["wetterhorn-peak"] = 9, ["north-maroon-peak"] = 12, ["san-luis-peak"] = 8,
        ["mount-of-the-holy-cross"] = 11, ["huron-peak"] = 7, ["sunshine-peak"] = 9,
    };
```

⚠️ The last ten CO slugs above were inferred from mountain names (the catalog grep paginated) — verify each against `BuildRoutes` while editing; the `Every_seeded_route...` test catches any mismatch.

Apply the catalog at the end of `BuildRoutes` (find its `return` of the route list; wrap):

```csharp
        // (at the end of BuildRoutes, where the list is returned)
        foreach (var route in routes)
        {
            // Indexer throws on a missing slug — RouteSeederTests turns that into a named failure.
            route.TypicalClimbHours = TypicalClimbHoursBySlug[route.Slug];
        }
        return routes;
```

Add the reconcile (mirrors `ReconcileGlaciatedAsync`) and call it from `SeedAsync` right after `await ReconcileGlaciatedAsync(db, ct);`:

```csharp
    private static async Task ReconcileTypicalClimbHoursAsync(RouteWeatherContext db, CancellationToken ct)
    {
        var rows = await db.Routes.ToListAsync(ct);
        var changed = false;
        foreach (var row in rows)
        {
            if (!TypicalClimbHoursBySlug.TryGetValue(row.Slug, out var hours)) continue;
            if (Math.Abs(row.TypicalClimbHours - hours) > 0.001)
            {
                row.TypicalClimbHours = hours;
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }
```

Note `SeedAsync`'s early-return branch (fresh DB): `BuildRoutes` already sets the values there, so no reconcile is needed on that path — same as `IsGlaciated`.

- [ ] **Step 5: Migration**

Stop the local API if it's running (it locks the DLLs), then:

```bash
dotnet ef migrations add AddTypicalClimbHours --project backend/RouteWeather.Data --startup-project backend/RouteWeather.API
dotnet build backend/RouteWeather.Data/RouteWeather.Data.csproj
```

Expected migration body (sanity-check the generated file):

```csharp
migrationBuilder.AddColumn<double>(
    name: "TypicalClimbHours",
    table: "Routes",
    type: "REAL",
    nullable: false,
    defaultValue: 0.0);
```

Fallback if the API can't be stopped: hand-author the trio (migration + Designer + `RouteWeatherContextModelSnapshot` edit) following `20260626000000_AddIsGlaciated.*` as the template, then verify with the Data-only build above.

- [ ] **Step 6: Run the seeder tests**

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter RouteSeederTests
```

Expected: PASS, including the three new tests. A `KeyNotFoundException` naming a slug means one of the ten inferred slugs is wrong — fix the dictionary key to match `BuildRoutes`.

- [ ] **Step 7: Commit**

```bash
git add -u backend && git add backend/RouteWeather.Data/Migrations
git commit -m "feat(data): TypicalClimbHours column, 124-route duration catalog, startup reconcile"
```

---

### Task 7: Aggregator wiring — windows computed at build time

**Files:**
- Modify: `backend/RouteWeather.Core/Models/RouteConditions.cs`
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs` (`BuildConditions`, lines 203–280)
- Test: `backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `ConditionsAggregatorTests.cs` (uses the file's existing `Harness`; `TestData.Route()` now carries `TypicalClimbHours = 8` and `TestData.Snapshot()` is 48 benign hours, so at least one window exists regardless of wall-clock):

```csharp
    [Fact]
    public async Task CacheOnly_WithForecastRow_ComputesClimbWindowsAndHourlyScores()
    {
        var h = new Harness(nameof(CacheOnly_WithForecastRow_ComputesClimbWindowsAndHourlyScores));
        await h.AddForecastRowAsync(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(55));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.Windows);
        Assert.NotNull(conditions.HourlyScores);
        Assert.Equal(48, conditions.HourlyScores!.Count);      // one score per series hour
        // Benign fixture should clear the B bar every hour. If this assert alone fails,
        // the fixture's 30°F hours score sub-B on the hourly path — relax to
        // Assert.Contains(conditions.HourlyScores!, q => q.Qualifies) rather than touching
        // the shared TestData.Snapshot() fixture.
        Assert.All(conditions.HourlyScores!, q => Assert.True(q.Qualifies));
        Assert.NotEmpty(conditions.Windows!);
    }

    [Fact]
    public async Task CacheOnly_NoRows_HasNoWindows()
    {
        var h = new Harness(nameof(CacheOnly_NoRows_HasNoWindows));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.True(conditions.Windows is null || conditions.Windows.Count == 0);
    }
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter ComputesClimbWindows
```

Expected: FAIL — `RouteConditions.Windows` doesn't exist.

- [ ] **Step 3: Extend `RouteConditions`**

In `backend/RouteWeather.Core/Models/RouteConditions.cs`, append two optional params after `AirQuality` (named `HourlyScores`, not `HourlyQuality`, to avoid the type/property name collision):

```csharp
public record RouteConditions(
    Route Route,
    Grade? Grade,
    int? OverallScore,
    IReadOnlyList<Driver> Drivers,
    IReadOnlyList<FactorScore> Factors,
    string Rationale,
    DateTimeOffset UpdatedAt,
    bool IsStale,
    WeatherSnapshot? Weather,
    SnowpackSnapshot? Snowpack,
    WindowGrades? WindowGrades,
    SourceFreshness Sources,
    ConsensusReport? Consensus,
    IReadOnlyList<PerSourceForecast>? PerSourceForecast,
    AirQualitySnapshot? AirQuality = null,
    IReadOnlyList<ClimbWindow>? Windows = null,
    IReadOnlyList<HourlyQuality>? HourlyScores = null
);
```

- [ ] **Step 4: Compute in `BuildConditions`**

In `ConditionsAggregator.BuildConditions`, the `route` (Core model) construction at line 237 gains the new field:

```csharp
        var route = new Core.Models.Route(
            routeEntity.Slug,
            routeEntity.Mountain,
            routeEntity.RouteName,
            routeEntity.SummitElevationFt,
            routeEntity.SummitLat,
            routeEntity.SummitLon,
            routeEntity.ClassDifficulty,
            routeEntity.SnotelStationTriplet,
            routeEntity.TypicalClimbHours);
```

After the `windowGrades` assignment (line 233–235), add:

```csharp
        var climbWindows = blendedWeather is null
            ? Array.Empty<ClimbWindow>()
            : WindowFinder.Find(blendedWeather, snowpack, airQuality.Snapshot,
                routeEntity.TypicalClimbHours, routeEntity.SummitLat, routeEntity.SummitLon);
        var hourlyScores = blendedWeather is null
            ? Array.Empty<HourlyQuality>()
            : WindowFinder.ScoreHours(blendedWeather, snowpack, airQuality.Snapshot);
```

And pass both at the end of the `RouteConditions` construction (after `airQuality.Snapshot`):

```csharp
            airQuality.Snapshot,
            climbWindows,
            hourlyScores);
```

- [ ] **Step 5: Run the suites**

```bash
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```

Expected: PASS. (Windows recompute on every `BuildConditions` — memory-cached ≤30m with the whole `RouteConditions`; nothing new is persisted to SQLite.)

- [ ] **Step 6: Commit**

```bash
git add -u backend
git commit -m "feat(api): aggregator computes climb windows and hourly scores"
```

---

### Task 8: Controller — `nextWindow` on summary, windows/strip/daylight on detail

**Files:**
- Modify: `backend/RouteWeather.API/Controllers/RoutesController.cs`
- Modify: `backend/RouteWeather.API.Tests/TestData.cs` (`Conditions` gains an optional `windows` param)
- Test: `backend/RouteWeather.API.Tests/RoutesControllerTests.cs`

- [ ] **Step 1: Extend the `TestData.Conditions` builder**

Add a parameter (after `perSourceForecast`) and pass it through in the ctor after `airQuality`:

```csharp
    public static RouteConditions Conditions(
        RouteEntity r,
        bool isStale,
        AirQualitySnapshot? airQuality = null,
        DateTimeOffset? airQualityFetchedAt = null,
        IReadOnlyList<PerSourceForecast>? perSourceForecast = null,
        IReadOnlyList<ClimbWindow>? windows = null) => new(
        // ... existing args unchanged ...
        airQuality,
        windows);
```

- [ ] **Step 2: Write the failing tests**

Append to `RoutesControllerTests.cs`:

```csharp
    private static ClimbWindow Window(int startHoursFromNow, int lengthHours, Grade grade, int score,
        bool lowConfidence = false) => new(
        DateTimeOffset.UtcNow.AddHours(startHoursFromNow),
        DateTimeOffset.UtcNow.AddHours(startHoursFromNow + lengthHours),
        grade, score, "closes as storm energy builds", lowConfidence);

    [Fact]
    public async Task GetAll_SerializesBestUpcomingWindowAsNextWindow()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_SerializesBestUpcomingWindowAsNextWindow));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var best = Window(30, 9, Grade.A, 95);
        var fake = new FakeConditionsAggregator
        {
            OnGet = r => TestData.Conditions(r, isStale: false, windows: new[]
            {
                Window(-30, 8, Grade.B, 85),   // already past — ignored
                Window(6, 8, Grade.B, 84),     // sooner but weaker
                best,                          // strongest upcoming → nextWindow
            }),
        };
        var controller = Build(dbFactory, fake);

        var json = Json(await controller.GetAll(CancellationToken.None));
        var nextWindow = json[0].GetProperty("nextWindow");

        Assert.Equal("A", nextWindow.GetProperty("grade").GetString());
        Assert.Equal(best.StartUtc, nextWindow.GetProperty("startUtc").GetDateTimeOffset());
        Assert.False(nextWindow.GetProperty("lowConfidence").GetBoolean());
    }

    [Fact]
    public async Task GetAll_NoUpcomingWindows_SerializesNullNextWindow()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_NoUpcomingWindows_SerializesNullNextWindow));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var fake = new FakeConditionsAggregator
        {
            OnGet = r => TestData.Conditions(r, isStale: false, windows: new[] { Window(-30, 8, Grade.A, 95) }),
        };
        var controller = Build(dbFactory, fake);

        var json = Json(await controller.GetAll(CancellationToken.None));

        Assert.Equal(JsonValueKind.Null, json[0].GetProperty("nextWindow").ValueKind);
    }

    [Fact]
    public async Task GetBySlug_SerializesUpcomingClimbWindowsAndDailyDaylight()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_SerializesUpcomingClimbWindowsAndDailyDaylight));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var fake = new FakeConditionsAggregator
        {
            OnGet = r => TestData.Conditions(r, isStale: false, windows: new[]
            {
                Window(-30, 8, Grade.A, 95),   // past — filtered out
                Window(6, 8, Grade.B, 84, lowConfidence: true),
            }),
        };
        var controller = Build(dbFactory, fake);

        var json = Json(await controller.GetBySlug("mt-test", CancellationToken.None));

        var windows = json.GetProperty("climbWindows");
        Assert.Equal(1, windows.GetArrayLength());
        Assert.Equal("closes as storm energy builds", windows[0].GetProperty("endReason").GetString());
        Assert.True(windows[0].GetProperty("lowConfidence").GetBoolean());

        // Read-time daylight covers the horizon: 9 mid-latitude entries.
        Assert.Equal(9, json.GetProperty("dailyDaylight").GetArrayLength());
    }
```

- [ ] **Step 3: Run to verify failure**

```bash
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter NextWindow
```

Expected: FAIL — `nextWindow` property missing from the summary DTO.

- [ ] **Step 4: Implement the controller changes**

In `RoutesController.cs`, add to `ToSummary` (after `airQualityUsAqi`):

```csharp
            nextWindow = SerializeNextWindow(c.Windows),
```

Add to `ToDetail` (after `perSourceForecast`):

```csharp
        climbWindows = c.Windows?
            .Where(w => w.EndUtc > DateTimeOffset.UtcNow)
            .Select(w => new
            {
                startUtc = w.StartUtc,
                endUtc = w.EndUtc,
                grade = w.Grade.ToString(),
                score = w.Score,
                endReason = w.EndReason,
                lowConfidence = w.LowConfidence,
            }),
        hourlyQuality = c.HourlyScores?.Select(q => new
        {
            timeUtc = q.TimeUtc,
            score = q.Score,
            qualifies = q.Qualifies,
        }),
        dailyDaylight = ComputeDailyDaylight(c),
```

Add the helpers next to `ComputeDaylight`:

```csharp
    // Selected at request time so a memory-cached RouteConditions can't pin
    // "next window" to a stretch that has already ended.
    private static object? SerializeNextWindow(IReadOnlyList<ClimbWindow>? windows)
    {
        var best = windows?
            .Where(w => w.EndUtc > DateTimeOffset.UtcNow)
            .OrderByDescending(w => w.Score)
            .ThenBy(w => w.StartUtc)
            .FirstOrDefault();
        return best is null ? null : new
        {
            startUtc = best.StartUtc,
            endUtc = best.EndUtc,
            grade = best.Grade.ToString(),
            lowConfidence = best.LowConfidence,
        };
    }

    // Read-time like ComputeDaylight: a stale cached row must not freeze sunrise/sunset.
    // Today + 8 days covers the 168h horizon plus UTC-date spill at western longitudes.
    private static IReadOnlyList<object> ComputeDailyDaylight(RouteConditions c)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Enumerable.Range(0, 9)
            .Select(i => SolarCalculator.ComputeUtc(c.Route.SummitLat, c.Route.SummitLon, today.AddDays(i)))
            .Where(d => d is not null)
            .Select(d => (object)new { sunriseUtc = d!.SunriseUtc, sunsetUtc = d.SunsetUtc })
            .ToList();
    }
```

- [ ] **Step 5: Run the suites**

```bash
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -u backend
git commit -m "feat(api): nextWindow summary field; climbWindows, hourlyQuality, dailyDaylight on detail"
```

---

### Task 9: Frontend models + fixture sweep + testing-rules update

**Files:**
- Modify: `frontend/src/app/models/route-conditions.ts`
- Modify: every spec with an inline `RouteSummary`/`RouteDetail` builder
- Modify: `.claude/rules/testing.md`

- [ ] **Step 1: Add the interfaces**

In `frontend/src/app/models/route-conditions.ts`, after the `Daylight` interface:

```typescript
export interface DaylightSpan {
  sunriseUtc: string;
  sunsetUtc: string;
}

export interface NextWindow {
  startUtc: string;
  endUtc: string;
  grade: Grade;
  lowConfidence: boolean;
}

export interface ClimbWindow extends NextWindow {
  score: number;
  endReason: string;
}

export interface HourlyQuality {
  timeUtc: string;
  score: number;
  qualifies: boolean;
}
```

Add to `RouteSummary` (after `airQualityUsAqi`):

```typescript
  nextWindow: NextWindow | null;
```

Add to `RouteDetail` (after `daylight`):

```typescript
  climbWindows: ClimbWindow[] | null;
  hourlyQuality: HourlyQuality[] | null;
  dailyDaylight: DaylightSpan[] | null;
```

- [ ] **Step 2: Fixture sweep**

Enumerate every builder the compiler now rejects:

```bash
cd frontend && npm test
```

(vitest surfaces the TypeScript errors; `npx tsc --noEmit -p tsconfig.spec.json` also works if that tsconfig exists). For each failing spec, add `nextWindow: null,` to `RouteSummary` literals and `climbWindows: null, hourlyQuality: null, dailyDaylight: null,` to `RouteDetail` literals. Expected files (verify with `grep -rl "RouteSummary\|RouteDetail" frontend/src/app --include="*.spec.ts"`): `route-card.spec.ts`, the home/all-peaks page spec, `peak-detail.spec.ts`, and any map-popup spec.

- [ ] **Step 3: Run the frontend suite**

```bash
npm test
```

(from `frontend/`; no watch flags — vitest runs once.) Expected: PASS.

- [ ] **Step 4: Update the testing rules**

In `.claude/rules/testing.md`, replace the `## RouteSummary fixtures` bullet with:

```markdown
## RouteSummary fixtures
- Specs that build a `RouteSummary` object literal must include every field on the interface (`slug`, `mountain`, `routeName`, `summitElevationFt`, `classDifficulty`, `isGlaciated`, `rangeSlug`, `rangeName`, `summitLat`, `summitLon`, `grade`, `overallScore`, `drivers`, `updatedAt`, `isStale`, `consensus`, `airQualityUsAqi`, `nextWindow`). TypeScript will catch omissions, but inline builders are common — keep them current as the contract evolves. `RouteDetail` additionally carries `climbWindows`, `hourlyQuality`, and `dailyDaylight` (all nullable).
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/models/route-conditions.ts .claude/rules/testing.md
git add -u frontend/src
git commit -m "feat(frontend): climb-window models; fixture sweep for nextWindow"
```

---

### Task 10: Route-card "Next window" line

**Files:**
- Modify: `frontend/src/app/components/route-card/route-card.ts`
- Modify: `frontend/src/app/components/route-card/route-card.html`
- Modify: `frontend/src/app/components/route-card/route-card.scss`
- Test: `frontend/src/app/components/route-card/route-card.spec.ts`

- [ ] **Step 1: Write the failing specs**

Append to `route-card.spec.ts` (the file's `summary()` builder now includes `nextWindow: null` from Task 9). Structure-anchored — no literal prose assertions beyond the strings this feature owns:

```typescript
  const nextWindow = (over: Partial<import('../../models/route-conditions').NextWindow> = {}) => ({
    startUtc: new Date(Date.now() + 30 * 3600_000).toISOString(),
    endUtc: new Date(Date.now() + 39 * 3600_000).toISOString(),
    grade: 'A' as const,
    lowConfidence: false,
    ...over,
  });

  it('shows the next-window line when a window exists', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), nextWindow: nextWindow() });
    fixture.detectChanges();

    const line = (fixture.nativeElement as HTMLElement).querySelector('.next-window');
    expect(line).toBeTruthy();
    expect(line!.textContent).toContain('A');
  });

  it('hides the line when nextWindow is null', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', summary('foo'));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.next-window')).toBeNull();
  });

  it('labels an underway window as starting now', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', {
      ...summary('foo'),
      nextWindow: nextWindow({ startUtc: new Date(Date.now() - 3600_000).toISOString() }),
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.next-window')!.textContent).toContain('Now');
  });

  it('marks low-confidence windows with a muted suffix', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), nextWindow: nextWindow({ lowConfidence: true }) });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.next-window .win-conf')).toBeTruthy();
  });

  it('renders 24h clock times when the time format setting is 24h', () => {
    TestBed.inject(SettingsService).set('timeFormat', '24h');
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), nextWindow: nextWindow() });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('.next-window')!.textContent ?? '';
    expect(text).not.toMatch(/AM|PM/i);
  });
```

- [ ] **Step 2: Run to verify failure**

```bash
npm test
```

Expected: FAIL — `.next-window` never renders.

- [ ] **Step 3: Implement**

`route-card.ts` — add the import and two helpers:

```typescript
import { NextWindow, RouteSummary } from '../../models/route-conditions';
import { DatePipe, DecimalPipe } from '@angular/common';
// add DatePipe to the component imports array
```

```typescript
  startsNow(w: NextWindow): boolean {
    return new Date(w.startUtc).getTime() <= Date.now();
  }

  crossesDay(w: NextWindow): boolean {
    return new Date(w.startUtc).toDateString() !== new Date(w.endUtc).toDateString();
  }
```

`route-card.html` — after the closing `</ul>` of `.drivers`:

```html
  @if (route().nextWindow; as w) {
    <p class="next-window">
      Next window:
      <strong>
        @if (startsNow(w)) { Now } @else { {{ w.startUtc | date:'EEE' }} {{ w.startUtc | date:u.clockFmt() }} }
        – @if (crossesDay(w)) { {{ w.endUtc | date:'EEE' }} } {{ w.endUtc | date:u.clockFmt() }} · {{ w.grade }}
      </strong>
      @if (w.lowConfidence) { <span class="win-conf">low confidence</span> }
    </p>
  }
```

`route-card.scss` — match the card's existing muted-text treatment (reuse its color variables/mixins if present rather than these literals):

```scss
.next-window {
  margin: 0.5rem 0 0;
  padding-top: 0.5rem;
  border-top: 1px solid rgba(128, 128, 128, 0.25);
  font-size: 0.85rem;
  opacity: 0.85;

  .win-conf {
    margin-left: 0.4rem;
    font-size: 0.75rem;
    opacity: 0.7;
    font-style: italic;
  }
}
```

- [ ] **Step 4: Run the suite**

```bash
npm test
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -u frontend/src
git commit -m "feat(cards): muted next-window line on route cards"
```

---

### Task 11: `climb-window-hero` component

**Files:**
- Create: `frontend/src/app/components/climb-window-hero/climb-window-hero.ts`
- Create: `frontend/src/app/components/climb-window-hero/climb-window-hero.html`
- Create: `frontend/src/app/components/climb-window-hero/climb-window-hero.scss`
- Test: `frontend/src/app/components/climb-window-hero/climb-window-hero.spec.ts`

- [ ] **Step 1: Write the failing specs**

```typescript
import { TestBed } from '@angular/core/testing';
import { ClimbWindowHero } from './climb-window-hero';
import { ClimbWindow } from '../../models/route-conditions';

describe('ClimbWindowHero', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [ClimbWindowHero] });
  });

  const win = (over: Partial<ClimbWindow> = {}): ClimbWindow => ({
    startUtc: new Date(Date.now() + 30 * 3600_000).toISOString(),
    endUtc: new Date(Date.now() + 39 * 3600_000).toISOString(),
    grade: 'A',
    score: 95,
    endReason: 'closes as storm energy builds',
    lowConfidence: false,
    ...over,
  });

  function create(windows: ClimbWindow[] | null) {
    const fixture = TestBed.createComponent(ClimbWindowHero);
    fixture.componentRef.setInput('windows', windows);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the strongest upcoming window with its end reason', () => {
    const el = create([
      win({ grade: 'B', score: 84, endUtc: new Date(Date.now() + 10 * 3600_000).toISOString(), startUtc: new Date(Date.now() + 2 * 3600_000).toISOString() }),
      win(), // A/95 → the hero
    ]);

    const hero = el.querySelector('.cwh');
    expect(hero).toBeTruthy();
    expect(hero!.textContent).toContain('storm energy builds');
    expect(el.querySelector('app-grade-badge')).toBeTruthy();
  });

  it('skips windows that already ended', () => {
    const past = win({
      startUtc: new Date(Date.now() - 20 * 3600_000).toISOString(),
      endUtc: new Date(Date.now() - 10 * 3600_000).toISOString(),
    });
    const el = create([past]);

    expect(el.querySelector('.cwh')).toBeNull();
    expect(el.querySelector('.cwh-none')).toBeTruthy();
  });

  it('states plainly when there is no window this week', () => {
    const el = create([]);
    expect(el.querySelector('.cwh-none')).toBeTruthy();
  });

  it('renders nothing at all when windows is null (ghost route)', () => {
    const el = create(null);
    expect(el.querySelector('.cwh')).toBeNull();
    expect(el.querySelector('.cwh-none')).toBeNull();
  });

  it('flags low-confidence windows', () => {
    const el = create([win({ lowConfidence: true })]);
    expect(el.querySelector('.cwh-conf')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run to verify failure**

```bash
npm test
```

Expected: FAIL — component doesn't exist.

- [ ] **Step 3: Implement**

`climb-window-hero.ts`:

```typescript
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { GradeBadge } from '../grade-badge/grade-badge';
import { ClimbWindow } from '../../models/route-conditions';
import { SettingsService } from '../../services/settings';

@Component({
  selector: 'app-climb-window-hero',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, GradeBadge],
  templateUrl: './climb-window-hero.html',
  styleUrl: './climb-window-hero.scss',
})
export class ClimbWindowHero {
  readonly u = inject(SettingsService);

  windows = input.required<ClimbWindow[] | null>();

  // Captured once per component instance; the page is short-lived enough that a
  // ticking clock isn't worth the change-detection churn.
  private readonly now = Date.now();

  private upcoming = computed(() =>
    (this.windows() ?? []).filter(w => new Date(w.endUtc).getTime() > this.now));

  best = computed<ClimbWindow | null>(() => {
    const list = this.upcoming();
    if (list.length === 0) return null;
    return [...list].sort((a, b) =>
      b.score - a.score || new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())[0];
  });

  startsNow(w: ClimbWindow): boolean {
    return new Date(w.startUtc).getTime() <= this.now;
  }

  crossesDay(w: ClimbWindow): boolean {
    return new Date(w.startUtc).toDateString() !== new Date(w.endUtc).toDateString();
  }
}
```

`climb-window-hero.html`:

```html
@if (best(); as w) {
  <div class="cwh">
    <app-grade-badge [grade]="w.grade" />
    <div class="cwh-body">
      <p class="cwh-title">
        Best window:
        <strong>
          @if (startsNow(w)) { Now } @else { {{ w.startUtc | date:'EEE' }} {{ w.startUtc | date:u.clockFmt() }} }
          – @if (crossesDay(w)) { {{ w.endUtc | date:'EEE' }} } {{ w.endUtc | date:u.clockFmt() }}
        </strong>
      </p>
      <p class="cwh-reason">
        Window {{ w.endReason }}.
        @if (w.lowConfidence) { <span class="cwh-conf">Low confidence — 5+ days out.</span> }
      </p>
    </div>
  </div>
} @else if (windows() !== null) {
  <p class="cwh-none">No climbable window in the next 7 days.</p>
}
```

`climb-window-hero.scss` (match the app's existing panel/border idiom — reuse its variables if the neighboring components have them):

```scss
.cwh {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  padding: 0.75rem 1rem;
  border: 1px solid rgba(128, 128, 128, 0.35);
  border-radius: 10px;
}

.cwh-title {
  margin: 0;
}

.cwh-reason {
  margin: 0.15rem 0 0;
  font-size: 0.9rem;
  opacity: 0.8;
}

.cwh-conf {
  font-style: italic;
  opacity: 0.75;
}

.cwh-none {
  margin: 0;
  padding: 0.75rem 1rem;
  border: 1px dashed rgba(128, 128, 128, 0.4);
  border-radius: 10px;
  opacity: 0.8;
}
```

- [ ] **Step 4: Run the suite**

```bash
npm test
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/components/climb-window-hero
git commit -m "feat(frontend): climb-window hero callout component"
```

---

### Task 12: `week-strip` component

**Files:**
- Create: `frontend/src/app/components/week-strip/week-strip.ts`
- Create: `frontend/src/app/components/week-strip/week-strip.html`
- Create: `frontend/src/app/components/week-strip/week-strip.scss`
- Test: `frontend/src/app/components/week-strip/week-strip.spec.ts`

- [ ] **Step 1: Write the failing specs**

```typescript
import { TestBed } from '@angular/core/testing';
import { WeekStrip } from './week-strip';
import { ClimbWindow, DaylightSpan, HourlyQuality } from '../../models/route-conditions';

describe('WeekStrip', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [WeekStrip] }));

  const T0 = Date.parse('2026-07-20T06:00:00Z');
  const iso = (h: number) => new Date(T0 + h * 3600_000).toISOString();

  const hours = (n: number): HourlyQuality[] =>
    Array.from({ length: n }, (_, i) => ({ timeUtc: iso(i), score: i % 5 === 0 ? 60 : 92, qualifies: i % 5 !== 0 }));

  const daylight: DaylightSpan[] = Array.from({ length: 9 }, (_, d) => ({
    sunriseUtc: new Date(T0 + (d * 24 + 6) * 3600_000).toISOString(),
    sunsetUtc: new Date(T0 + (d * 24 + 21) * 3600_000).toISOString(),
  }));

  const win: ClimbWindow = {
    startUtc: iso(10), endUtc: iso(20), grade: 'A', score: 95,
    endReason: 'ends with daylight', lowConfidence: false,
  };

  function create(h: HourlyQuality[] | null, w: ClimbWindow[] | null = [win], d: DaylightSpan[] | null = daylight) {
    const fixture = TestBed.createComponent(WeekStrip);
    fixture.componentRef.setInput('hours', h);
    fixture.componentRef.setInput('windows', w);
    fixture.componentRef.setInput('daylight', d);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders one cell per hour, grouped into day columns', () => {
    const el = create(hours(168));
    expect(el.querySelectorAll('.cell').length).toBe(168);
    expect(el.querySelectorAll('.day').length).toBeGreaterThanOrEqual(7);
  });

  it('marks hours inside a climb window', () => {
    const el = create(hours(48));
    expect(el.querySelectorAll('.cell.in-window').length).toBe(10);
  });

  it('marks night hours outside the daylight spans', () => {
    const el = create(hours(24));
    expect(el.querySelectorAll('.cell.night').length).toBeGreaterThan(0);
    expect(el.querySelectorAll('.cell:not(.night)').length).toBeGreaterThan(0);
  });

  it('hatches hours beyond the 96h confidence horizon', () => {
    const el = create(hours(168));
    expect(el.querySelectorAll('.cell.low-conf').length).toBe(72);
    expect(create(hours(48)).querySelectorAll('.cell.low-conf').length).toBe(0);
  });

  it('renders nothing without hourly data', () => {
    expect(create(null).querySelector('.strip')).toBeNull();
    expect(create([]).querySelector('.strip')).toBeNull();
  });
});
```

- [ ] **Step 2: Run to verify failure**

```bash
npm test
```

Expected: FAIL — component doesn't exist.

- [ ] **Step 3: Implement**

`week-strip.ts`:

```typescript
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ClimbWindow, DaylightSpan, HourlyQuality } from '../../models/route-conditions';

interface StripCell { cls: string; }
interface StripDay { label: string; cells: StripCell[]; }

@Component({
  selector: 'app-week-strip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './week-strip.html',
  styleUrl: './week-strip.scss',
})
export class WeekStrip {
  hours = input.required<HourlyQuality[] | null>();
  windows = input.required<ClimbWindow[] | null>();
  daylight = input.required<DaylightSpan[] | null>();

  days = computed<StripDay[]>(() => {
    const hours = this.hours() ?? [];
    if (hours.length === 0) return [];

    const spans = (this.daylight() ?? []).map(d => ({ rise: Date.parse(d.sunriseUtc), set: Date.parse(d.sunsetUtc) }));
    const wins = (this.windows() ?? []).map(w => ({ s: Date.parse(w.startUtc), e: Date.parse(w.endUtc) }));

    const days: StripDay[] = [];
    let current: StripDay | null = null;
    hours.forEach((h, i) => {
      const t = new Date(h.timeUtc);
      const ms = t.getTime();
      // Local day label — a new label starts a new column.
      const label = t.toLocaleDateString(undefined, { weekday: 'short' });
      if (!current || current.label !== label) {
        current = { label, cells: [] };
        days.push(current);
      }
      const cls = [
        h.qualifies ? (h.score >= 90 ? 'q-a' : 'q-b') : 'q-no',
        spans.some(d => ms >= d.rise && ms < d.set) ? '' : 'night',
        wins.some(w => ms >= w.s && ms < w.e) ? 'in-window' : '',
        i >= 96 ? 'low-conf' : '',
      ].filter(Boolean).join(' ');
      current.cells.push({ cls });
    });
    return days;
  });
}
```

`week-strip.html`:

```html
@if (days().length > 0) {
  <div class="strip">
    @for (day of days(); track $index) {
      <div class="day" [style.flex-grow]="day.cells.length">
        <span class="day-label">{{ day.label }}</span>
        <div class="cells">
          @for (c of day.cells; track $index) {
            <span [class]="'cell ' + c.cls"></span>
          }
        </div>
      </div>
    }
  </div>
  <p class="strip-legend">Solid = climbable hour · dimmed = night · hatched = 5+ days out (lower confidence)</p>
}
```

`week-strip.scss`:

```scss
.strip {
  display: flex;
  gap: 3px;
}

.day {
  flex: 1 1 0;
  min-width: 0;
}

.day-label {
  display: block;
  text-align: center;
  font-size: 0.7rem;
  opacity: 0.6;
  margin-bottom: 2px;
}

.cells {
  display: flex;
  height: 14px;
  border-radius: 4px;
  overflow: hidden;
  background: rgba(128, 128, 128, 0.18);
}

.cell {
  flex: 1 1 0;

  &.q-a { background: #3e9c6d; }
  &.q-b { background: #6aa84f; }
  &.q-no { background: transparent; }
  &.night { filter: brightness(0.55); }
  &.in-window { box-shadow: inset 0 -2px 0 currentColor; }
  &.low-conf {
    background-image: repeating-linear-gradient(45deg, transparent 0 3px, rgba(128, 128, 128, 0.45) 3px 5px);
  }
}

.strip-legend {
  margin: 0.35rem 0 0;
  font-size: 0.75rem;
  opacity: 0.6;
}
```

(Colors are placeholders in the repo's current idiom — align `q-a`/`q-b` with the grade-badge greens if those are defined as variables.)

- [ ] **Step 4: Run the suite**

```bash
npm test
```

Expected: PASS. The night-cell test depends only on the fixture's own daylight spans — no wall clock anywhere in this component (it renders whatever it's given; past-window filtering is the hero's job).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/components/week-strip
git commit -m "feat(frontend): 7-day week-strip visualization"
```

---

### Task 13: Peak-detail integration

**Files:**
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.ts`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.html`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.scss` (thin wrapper ONLY — file is at ~6.6kB of a 7kB warn / 8kB error budget)
- Test: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`

- [ ] **Step 1: Write the failing spec**

Append to `peak-detail.spec.ts`, following the file's existing pattern for feeding a `RouteDetail` through the mocked `RoutesService`/HTTP layer (its detail fixture gained `climbWindows`/`hourlyQuality`/`dailyDaylight: null` in Task 9's sweep — build one here with real values):

```typescript
  it('renders the climb-window hero and week strip when the detail payload carries windows', () => {
    // Use this spec file's existing detail-fixture helper; override the three new fields:
    const T0 = Date.now() + 6 * 3600_000;
    const detail = {
      ...baseDetailFixture(),          // ← the file's existing RouteDetail builder
      climbWindows: [{
        startUtc: new Date(T0).toISOString(),
        endUtc: new Date(T0 + 9 * 3600_000).toISOString(),
        grade: 'A' as const, score: 95,
        endReason: 'ends with daylight', lowConfidence: false,
      }],
      hourlyQuality: Array.from({ length: 48 }, (_, i) => ({
        timeUtc: new Date(Date.now() + i * 3600_000).toISOString(), score: 92, qualifies: true,
      })),
      dailyDaylight: Array.from({ length: 9 }, (_, d) => ({
        sunriseUtc: new Date(Date.now() + (d * 24 + 6) * 3600_000).toISOString(),
        sunsetUtc: new Date(Date.now() + (d * 24 + 21) * 3600_000).toISOString(),
      })),
    };
    // ...flush `detail` through the spec's existing HTTP/service mock...

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('app-climb-window-hero .cwh')).toBeTruthy();
    expect(el.querySelector('app-week-strip .strip')).toBeTruthy();
  });
```

(`baseDetailFixture()` stands for whatever builder the spec file already uses — reuse it verbatim; the assertion block is the contract.)

- [ ] **Step 2: Run to verify failure**

```bash
npm test
```

Expected: FAIL — the selectors don't exist in the template yet.

- [ ] **Step 3: Implement**

`peak-detail.ts`:
- Add imports: `import { ClimbWindowHero } from '../../components/climb-window-hero/climb-window-hero';` and `import { WeekStrip } from '../../components/week-strip/week-strip';`
- Add `ClimbWindowHero, WeekStrip` to the component `imports` array.

`peak-detail.html` — first thing inside `@if (detail(); as d) {` (before the existing `.window-strip` section):

```html
    @if (d.climbWindows !== null || d.hourlyQuality !== null) {
      <section class="climb-windows">
        <h3>Climb windows</h3>
        <app-climb-window-hero [windows]="d.climbWindows" />
        <app-week-strip [hours]="d.hourlyQuality" [windows]="d.climbWindows" [daylight]="d.dailyDaylight" />
      </section>
    }
```

`peak-detail.scss` — layout wrapper only (component styling lives in the two new components):

```scss
.climb-windows {
  display: grid;
  gap: 0.75rem;
  margin: 1rem 0;
}
```

- [ ] **Step 4: Run the suite and the production build**

```bash
npm test
npm run build
```

Expected: tests PASS; build succeeds with **no** `anyComponentStyle` warning for `peak-detail.scss` (the wrapper adds ~60 bytes). If a warning appears, trim `peak-detail.scss` — do not raise the budget.

- [ ] **Step 5: Commit**

```bash
git add -u frontend/src
git commit -m "feat(peak-detail): climb-windows section — hero callout + week strip"
```

---

### Task 14: Full verification, PR

**Files:** none (verification + git)

- [ ] **Step 1: Full backend + frontend suites**

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
cd frontend && npm test && npm run build
```

Expected: all PASS; build clean. (Stop the local API first if it's running — DLL locks.)

- [ ] **Step 2: One end-to-end smoke with real data**

Ask the user to start the API (they run it in their own terminal; do not start it yourself), wait for one warmer cycle, then:

```bash
curl -s http://localhost:5150/api/routes | head -c 2000
curl -s http://localhost:5150/api/routes/mount-baker | head -c 4000
```

Expected: summaries carry `nextWindow` (object or null, never missing); detail carries `climbWindows`, 100+ `hourlyQuality` entries (NWS+Open-Meteo horizons), `dailyDaylight` with 9 entries, and `forecastNext48h` still capped at 48.

- [ ] **Step 3: Pre-PR hygiene**

Run the `git-security` agent over the branch diff (project convention before any PR), then:

```bash
git log --oneline dev..HEAD        # sanity: only this feature's commits
git push -u origin feature/climb-window-finder
gh pr create --base dev --title "feat: climb-window finder — 7-day windows, hero + week strip, next-window cards" --body "$(cat <<'EOF'
## Summary
- 7-day hourly horizon (Open-Meteo forecast_days=7; NWS untruncated) with headline scalars pinned to the first 48h — headline/12/24/48 grades are bit-identical (HeadlineInvariantTests)
- WindowFinder: per-hour scoring through the existing factor machinery; windows = qualifying runs ∩ climbing-day frames (sunrise−6h→sunset), sized by per-route TypicalClimbHours (124-route catalog + startup reconcile)
- API: nextWindow on summaries; climbWindows / hourlyQuality / dailyDaylight on detail (forecastNext48h unchanged at 48 entries)
- UI: climb-window hero + week strip on peak detail; muted "Next window" line on route cards; 12/24h time-format aware

## Test plan
- [ ] Core, API, frontend suites green
- [ ] Dev preview: hydration clean, strip renders on mobile width, card line shows on a route with an upcoming window
- [ ] Ghost route (no data) shows no window UI; stale route inherits the stale banner untouched

Spec: docs/superpowers/specs/2026-07-18-climb-window-finder-design.md

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 4: Manual verification on the dev preview (after merge to dev)**

On `https://dev.<project>.pages.dev`:
1. Peak detail for a Cascades route: hero states a window or "No climbable window in the next 7 days"; strip shows ~7 day columns with night dimming and a hatched tail.
2. Route card list: quiet "Next window" line present only where a window exists; toggle 24h time format in settings and confirm the line follows.
3. Browser console: no hydration (NG0xxx) errors on first load of a peak page.
4. Compare the hero's headline grade against the strip's first 48 hours — they must tell the same story (the invariant, verified by eye once, end to end).

---

## Self-review checklist (run after writing, before execution)

- **Spec coverage:** 7-day horizon (T3), route-aware windows via TypicalClimbHours (T5/T6), window+end-reason not summit-ETA (T5), hero+strip on detail (T11–13), muted card line (T10), single-pipeline architecture (T2–T4/T7), invariant test (T4), low-confidence at 96h (T5), read-time daylight (T8), ghosts/stale semantics (T7/T8/T11), fixture sweep + testing.md (T9), sequencing (T1). ✓
- **Type consistency:** `RouteConditions.HourlyScores` (C#) serializes as `hourlyQuality` (wire) → `RouteDetail.hourlyQuality` (TS). `WeatherSnapshot.Hourly` keeps wire/cache name `next48Hours`; the detail field `forecastNext48h` is unchanged. `ClimbWindow`/`NextWindow`/`HourlyQuality`/`DaylightSpan` match the controller's anonymous objects field-for-field. ✓
- **Known judgment calls encoded above:** qualifying bar = post-cap A/B; frames clip multi-day runs into per-day windows; `EndReason` is a display-ready clause; windows recompute per `BuildConditions` (never persisted); the ten inferred CO slugs are guarded by a seeder test.







