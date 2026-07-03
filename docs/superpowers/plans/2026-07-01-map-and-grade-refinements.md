# Map & Grade Refinements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nudge the glacier map badge closer to its dot, promote AQI to a conditional grade factor (active at AQI ≥ 101), and restore the map's zoom/center when the user returns to the map mid-session.

**Architecture:** Feature 1 is a one-line SCSS change. Feature 2 adds an `AirQualityFactor` in `Core/Grading` following the existing silent-until-active factor pattern (Thunderstorm/Gusts), threaded through `GradeCalculator` → `WindowGradeCalculator` → `ConditionsAggregator` so every grade surface stays consistent. Feature 3 adds a tiny in-memory `MapViewState` root service that `MapHome` writes on Leaflet `moveend` and reads on init.

**Tech Stack:** ASP.NET Core (.NET 10, C#), xUnit; Angular 21 (zoneless, signals), Vitest + jsdom; Leaflet.

**Spec:** `docs/superpowers/specs/2026-07-01-map-and-grade-refinements-design.md`

**Branch:** `feature/map-and-grade-refinements` (already created off `dev`; the spec commit is the first commit).

**Commit shape:** tasks below commit individually (TDD, frequent commits). The spec proposed a 3-commit shape (one per feature); if you prefer that, squash per-feature at merge — all tasks land in one PR to `dev`.

**Command notes (project-specific):**
- Backend Core tests: `dotnet test backend/RouteWeather.Core.Tests` (Core is not locked by a running API).
- Backend API tests: `dotnet test backend/RouteWeather.API.Tests`. If this fails to build with a *file-in-use* error, the local API is running and locking `RouteWeather.API.dll`; stop that terminal's API for the test run (do not start/stop servers yourself if the user manages them — ask them to).
- Frontend tests: run from `frontend/` with `npm test` (Vitest runs once and exits — do NOT pass `--watch=false`, `--browsers=...`, etc.).

---

## Task 1: Glacier badge — nudge closer to its dot

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.scss:147-156`

No automated test (pure visual taste); verified manually on the map.

- [ ] **Step 1: Move the badge in toward the dot**

In `frontend/src/app/pages/map-home/map-home.scss`, change the `.peak-marker .glacier-badge` offsets from the box corner (`-2px / -2px`) to sit on the dot's upper-right shoulder.

Replace:

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
```

with:

```scss
.peak-marker .glacier-badge {
  position: absolute;
  top: 1px;
  right: 2px;
  font-size: 0.7rem;
  line-height: 1;
  color: #d6ecf7;
  text-shadow: 0 0 2px #0b1620, 0 0 2px #0b1620;
  pointer-events: none;
}
```

- [ ] **Step 2: Build the frontend to confirm SCSS compiles**

Run (from `frontend/`): `npm run build`
Expected: build succeeds (no SCSS errors, within the anyComponentStyle budget).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/app/pages/map-home/map-home.scss
git commit -m "fix(map): tuck glacier badge closer to its grade dot"
```

> Manual check on the dev preview: on a glaciated peak marker (e.g., Mount Rainier), the ❄ should sit snug at the dot's top-right, not floating in the corner. If `1px / 2px` isn't quite right, nudge the two values and rebuild — this is the only "dial-in" step in the plan.

---

## Task 2: `AirQualityFactor` — the scoring/cap unit

**Files:**
- Create: `backend/RouteWeather.Core/Grading/AirQualityFactor.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/AirQualityFactorTests.cs`

Mirrors the shape of `GustFactor` / `ThunderstormFactor`: a static class with `Weight`, an active floor, `IsActive`, `Score`, `Detail`, and `Cap`. No `InactiveDetail` — unlike Gusts/Thunderstorm, AQI is never rendered as an inactive card (it has its own "Air quality" tile on the detail page), so below the floor it contributes nothing at all.

- [ ] **Step 1: Write the failing test**

Create `backend/RouteWeather.Core.Tests/Grading/AirQualityFactorTests.cs`:

```csharp
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class AirQualityFactorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(101, true)]
    [InlineData(250, true)]
    public void IsActive_gatesAt101(int aqi, bool expected) =>
        Assert.Equal(expected, AirQualityFactor.IsActive(aqi));

    [Fact]
    public void Score_dragsBelow85_theInstantItActivates() =>
        Assert.Equal(80, AirQualityFactor.Score(101));

    [Fact]
    public void Score_isMidwayByAqi175() =>
        Assert.Equal(50, AirQualityFactor.Score(175));

    [Fact]
    public void Score_hitsZero_atHazardous() =>
        Assert.Equal(0, AirQualityFactor.Score(300));

    [Theory]
    [InlineData(101, null)]
    [InlineData(150, null)]
    [InlineData(151, Grade.C)]
    [InlineData(200, Grade.C)]
    [InlineData(201, Grade.F)]
    [InlineData(400, Grade.F)]
    public void Cap_noCapUntil151_thenCThenF(int aqi, Grade? expected) =>
        Assert.Equal(expected, AirQualityFactor.Cap(aqi).Cap);

    [Fact]
    public void Detail_mentionsAqiValue() =>
        Assert.Contains("101", AirQualityFactor.Detail(101));

    [Fact]
    public void Cap_reasonMentionsAqiValue() =>
        Assert.Contains("250", AirQualityFactor.Cap(250).Reason);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/RouteWeather.Core.Tests`
Expected: FAIL — build error, `AirQualityFactor` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `backend/RouteWeather.Core/Grading/AirQualityFactor.cs`:

```csharp
using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class AirQualityFactor
{
    public const double Weight = 0.15;

    // US EPA AQI bands: 0-50 Good, 51-100 Moderate, 101-150 Unhealthy for
    // sensitive groups, 151-200 Unhealthy, 201-300 Very Unhealthy, 301+ Hazardous.
    // Silent until 101 — below that, air is not a real factor on a big objective,
    // and an always-on AQI factor would dilute every other weight.
    public const int ActiveFloorUsAqi = 101;

    public static bool IsActive(int usAqi) => usAqi >= ActiveFloorUsAqi;

    // goodValue 50 sits well below the 101 active floor, so the score is already
    // a drag (~80) the instant the factor activates — bad air can only lower the
    // grade, never inflate it, and it never reads as a positive driver.
    public static int Score(int usAqi) =>
        ScoringMath.LinearBetween(usAqi, goodValue: 50, badValue: 300);

    public static string Detail(int usAqi) => $"US AQI {usAqi}";

    public static (Grade? Cap, string Reason) Cap(int usAqi)
    {
        if (usAqi >= 201) return (Grade.F, $"air quality index {usAqi}");
        if (usAqi >= 151) return (Grade.C, $"air quality index {usAqi}");
        return (null, string.Empty);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/RouteWeather.Core.Tests`
Expected: PASS (all `AirQualityFactorTests` green, no regressions).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/AirQualityFactor.cs backend/RouteWeather.Core.Tests/Grading/AirQualityFactorTests.cs
git commit -m "feat(grading): add AirQualityFactor (silent below AQI 101, caps at C/F)"
```

---

## Task 3: Fold AQI into `GradeCalculator`

**Files:**
- Modify: `backend/RouteWeather.Core/Grading/GradeCalculator.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/GradeCalculatorTests.cs`

`Compute` gains an optional `AirQualitySnapshot? airQuality = null` (optional keeps existing callers/tests compiling). AQI is added as a factor only when a reading exists, it's active (≥ 101), **and** at least one weather/snowpack factor already exists (`factors.Count > 0`) — the last guard guarantees AQI alone can never manufacture a grade on a data-less route.

- [ ] **Step 1: Write the failing tests**

Append these facts to the `GradeCalculatorTests` class in `backend/RouteWeather.Core.Tests/Grading/GradeCalculatorTests.cs` (just before the closing brace of the class). They reuse the existing private `Weather(...)` helper (benign weather → grades A) and `AirQualitySnapshot` (already imported):

```csharp
    [Fact]
    public void GoodAqi_addsNoAirQualityFactor()
    {
        var result = GradeCalculator.Compute(Weather(), null, new AirQualitySnapshot(80, 10));
        Assert.DoesNotContain(result.Factors, f => f.Name == "Air quality");
    }

    [Fact]
    public void UnhealthyForSensitiveAqi_addsActiveDrag_withoutCap()
    {
        var result = GradeCalculator.Compute(Weather(), null, new AirQualitySnapshot(120, 30));
        var f = Assert.Single(result.Factors, x => x.Name == "Air quality");
        Assert.True(f.IsActive);
        Assert.DoesNotContain("Capped", result.Rationale);
    }

    [Fact]
    public void UnhealthyAqi_capsGradeAtC()
    {
        var result = GradeCalculator.Compute(Weather(), null, new AirQualitySnapshot(175, 60));
        Assert.Equal(Grade.C, result.Grade);
        Assert.Contains("Capped at C", result.Rationale);
    }

    [Fact]
    public void HazardousAqi_capsGradeAtF_andLeadsDrivers()
    {
        var result = GradeCalculator.Compute(Weather(), null, new AirQualitySnapshot(250, 90));
        Assert.Equal(Grade.F, result.Grade);
        Assert.Equal("Poor air quality", result.Drivers[0].Label);
    }

    [Fact]
    public void Aqi_neverManufacturesGradeWithoutWeatherOrSnowpack()
    {
        var result = GradeCalculator.Compute(null, null, new AirQualitySnapshot(250, 90));
        Assert.DoesNotContain(result.Factors, f => f.Name == "Air quality");
        Assert.Empty(result.Factors);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/RouteWeather.Core.Tests`
Expected: FAIL — build error, `Compute` has no 3-argument overload.

- [ ] **Step 3: Add the parameter and the AQI block**

In `backend/RouteWeather.Core/Grading/GradeCalculator.cs`:

3a. Change the signature (line 15):

```csharp
    public static GradeResult Compute(WeatherSnapshot? weather, SnowpackSnapshot? snowpack)
```

to:

```csharp
    public static GradeResult Compute(WeatherSnapshot? weather, SnowpackSnapshot? snowpack, AirQualitySnapshot? airQuality = null)
```

3b. Insert the AQI factor block immediately after the snowpack `if (snowpack is not null) { ... }` block and before `var activeFactors = factors.Where(f => f.IsActive).ToList();`:

```csharp
        // AQI is a grade modifier, never a standalone grade: only fold it in once
        // at least one weather/snowpack factor exists (factors.Count > 0). Silent
        // below 101, so no card and no drag on clean/moderate air.
        if (airQuality is not null && factors.Count > 0 && AirQualityFactor.IsActive(airQuality.UsAqi))
        {
            factors.Add(new FactorScore(
                "Air quality",
                AirQualityFactor.Score(airQuality.UsAqi),
                AirQualityFactor.Weight,
                AirQualityFactor.Detail(airQuality.UsAqi)));
            AddCap(capCandidates, "Air quality", AirQualityFactor.Cap(airQuality.UsAqi));
        }
```

3c. Add an "Air quality" case to the `LabelFor` switch (inside the `f.Name switch { ... }` expression), e.g. right after the `"Gusts" => ...` line:

```csharp
        "Air quality" => severity == "negative" ? "Poor air quality" : severity == "neutral" ? "Reduced air quality" : "Clean air",
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.Core.Tests`
Expected: PASS — the five new facts green, and all pre-existing `GradeCalculatorTests` still green (existing tests never pass AQI, so behavior is unchanged for them).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/GradeCalculator.cs backend/RouteWeather.Core.Tests/Grading/GradeCalculatorTests.cs
git commit -m "feat(grading): fold AQI into the overall grade when >= 101"
```

---

## Task 4: Thread AQI through `WindowGradeCalculator`

**Files:**
- Modify: `backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/WindowGradeCalculatorTests.cs`

The 12h/24h/48h window grades run through the same `GradeCalculator.Compute`. Passing the same current AQI snapshot into every window keeps the detail-page hero (24h) and the map's headline grade consistent — no headline-vs-window AQI mismatch.

- [ ] **Step 1: Write the failing test**

Append to the `WindowGradeCalculatorTests` class in `backend/RouteWeather.Core.Tests/Grading/WindowGradeCalculatorTests.cs` (before the class closing brace). This test is self-contained (builds its own 48h benign weather):

```csharp
    [Fact]
    public void HazardousAqi_capsEveryWindowAtF()
    {
        var hours = Enumerable.Range(0, 48)
            .Select(i => new HourlyForecast(DateTimeOffset.UtcNow.AddHours(i), 50, 5, 0, "Clear"))
            .ToList();
        var weather = new WeatherSnapshot(
            WindMph: 5, TempF: 50, PrecipitationProbabilityPct: 0, Next48Hours: hours);

        var grades = WindowGradeCalculator.Compute(weather, null, new AirQualitySnapshot(250, 90));

        Assert.Equal(Grade.F, grades.Next12h.Grade);
        Assert.Equal(Grade.F, grades.Next24h.Grade);
        Assert.Equal(Grade.F, grades.Next48h.Grade);
    }
```

> If `WindowGradeCalculatorTests.cs` does not already have `using RouteWeather.Core.Models;` and `using Xunit;` at the top, add them (they are needed for `WeatherSnapshot`, `HourlyForecast`, `AirQualitySnapshot`, `Grade`, and `[Fact]`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/RouteWeather.Core.Tests`
Expected: FAIL — build error, `WindowGradeCalculator.Compute` has no 3-argument overload.

- [ ] **Step 3: Thread the parameter through**

In `backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs`:

3a. Change `Compute` to accept and forward AQI:

```csharp
    public static WindowGrades Compute(WeatherSnapshot? weather, SnowpackSnapshot? snowpack, AirQualitySnapshot? airQuality = null)
    {
        return new WindowGrades(
            Next12h: GradeWindow(weather, snowpack, 12, airQuality),
            Next24h: GradeWindow(weather, snowpack, 24, airQuality),
            Next48h: GradeWindow(weather, snowpack, 48, airQuality));
    }
```

3b. Change `GradeWindow`'s signature and its `GradeCalculator.Compute` call:

```csharp
    private static WindowGrade GradeWindow(WeatherSnapshot? weather, SnowpackSnapshot? snowpack, int hours, AirQualitySnapshot? airQuality)
```

and inside it, change:

```csharp
        var result = GradeCalculator.Compute(windowed, snowpack);
```

to:

```csharp
        var result = GradeCalculator.Compute(windowed, snowpack, airQuality);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/RouteWeather.Core.Tests`
Expected: PASS — new window test green; existing `WindowGradeCalculatorTests` still green (they call the 2-arg form, unchanged behavior).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/WindowGradeCalculator.cs backend/RouteWeather.Core.Tests/Grading/WindowGradeCalculatorTests.cs
git commit -m "feat(grading): apply AQI to window grades to keep surfaces consistent"
```

---

## Task 5: Wire AQI into `ConditionsAggregator`

**Files:**
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs:223,233-235`
- Test: `backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs`

`BuildConditions` already resolves `airQuality` (the first non-null AQI fetch result). Pass its `.Snapshot` into both the overall grade and the window grades. The `forceStale` / `isStale` logic is **not** touched — a stale or missing AQI must still never mark the route stale (existing tests `StaleAirQuality_doesNotFlagRouteStale` and `FailedAirQuality_yieldsNullAirQuality_gradeUnaffected` guard this and must stay green).

- [ ] **Step 1: Write the failing test**

Append to the `ConditionsAggregatorTests` class in `backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs` (before the class closing brace). It reuses the existing `Harness`, `AddForecastRowAsync`, and `AddAirQualityRowAsync` helpers. A benign forecast row grades well; a cached AQI of 250 must cap the served grade at F:

```csharp
    [Fact]
    public async Task HighAirQuality_capsServedGrade()
    {
        var h = new Harness(nameof(HighAirQuality_capsServedGrade));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));
        await h.AddAirQualityRowAsync(
            """{"usAqi":250,"pm25":80.0}""",
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.Equal(Grade.F, conditions.Grade);
        Assert.Contains("air quality", conditions.Rationale, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/RouteWeather.API.Tests`
Expected: FAIL — assertion fails (grade is the benign A/B, not F) because AQI is not yet wired into grading.
(If instead you see a *file-in-use* build error, the local API is running — see Command notes at the top.)

- [ ] **Step 3: Pass the AQI snapshot into grading**

In `backend/RouteWeather.API/Services/ConditionsAggregator.cs`, in `BuildConditions`:

3a. Change the overall grade call (line 223):

```csharp
        var result = GradeCalculator.Compute(blendedWeather, snowpack);
```

to:

```csharp
        var result = GradeCalculator.Compute(blendedWeather, snowpack, airQuality.Snapshot);
```

3b. Change the window grades call (lines 233-235):

```csharp
        var windowGrades = blendedWeather is null && snowpack is null
            ? null
            : WindowGradeCalculator.Compute(blendedWeather, snowpack);
```

to:

```csharp
        var windowGrades = blendedWeather is null && snowpack is null
            ? null
            : WindowGradeCalculator.Compute(blendedWeather, snowpack, airQuality.Snapshot);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/RouteWeather.API.Tests`
Expected: PASS — `HighAirQuality_capsServedGrade` green; `StaleAirQuality_doesNotFlagRouteStale`, `FailedAirQuality_yieldsNullAirQuality_gradeUnaffected`, `ReadThrough_fetchesAndCachesAirQuality`, and `CacheOnly_readsAirQualityRow_withoutFetching` all still green (their AQI values are ≤ 80, so grading is unaffected).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.API/Services/ConditionsAggregator.cs backend/RouteWeather.API.Tests/ConditionsAggregatorTests.cs
git commit -m "feat(api): thread AQI snapshot into overall and window grades"
```

---

## Task 6: `MapViewState` session service

**Files:**
- Create: `frontend/src/app/services/map-view-state.ts`
- Test: `frontend/src/app/services/map-view-state.spec.ts`

An in-memory, root-singleton store for the last map view. In-memory means it survives client-side navigation (map → peak → back) but resets on a hard reload.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/app/services/map-view-state.spec.ts`:

```typescript
import { MapViewState } from './map-view-state';

describe('MapViewState', () => {
  it('returns null before any view is saved', () => {
    expect(new MapViewState().load()).toBeNull();
  });

  it('round-trips the saved view', () => {
    const svc = new MapViewState();
    svc.save({ center: [40, -105], zoom: 9 });
    expect(svc.load()).toEqual({ center: [40, -105], zoom: 9 });
  });

  it('overwrites the previous view on re-save', () => {
    const svc = new MapViewState();
    svc.save({ center: [40, -105], zoom: 9 });
    svc.save({ center: [45, -110], zoom: 5 });
    expect(svc.load()).toEqual({ center: [45, -110], zoom: 5 });
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `frontend/`): `npm test`
Expected: FAIL — cannot resolve `./map-view-state`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/app/services/map-view-state.ts`:

```typescript
import { Injectable } from '@angular/core';

export interface MapView {
  center: [number, number];
  zoom: number;
}

/**
 * In-memory store for the map's last zoom/center. Root singleton, so it survives
 * client-side navigation (map -> peak -> back) but resets on a hard reload —
 * mid-session you return to where you were; a fresh visit gets the default view.
 */
@Injectable({ providedIn: 'root' })
export class MapViewState {
  private view: MapView | null = null;

  save(view: MapView): void {
    this.view = view;
  }

  load(): MapView | null {
    return this.view;
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `frontend/`): `npm test`
Expected: PASS — all three `MapViewState` specs green.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/services/map-view-state.ts frontend/src/app/services/map-view-state.spec.ts
git commit -m "feat(map): add MapViewState in-memory session store"
```

---

## Task 7: Save/restore the map view in `MapHome`

**Files:**
- Modify: `frontend/src/app/pages/map-home/map-home.ts:1,26-31,197-207`

`MapHome` reads the saved view when creating the Leaflet map and writes it on every `moveend` (fires after pan and after zoom). The map init runs behind `afterNextRender` + `isPlatformBrowser` with real Leaflet, which jsdom does not exercise — so this task has no jsdom test; correctness of the store is covered by Task 6, and the wiring is verified manually on the dev preview.

- [ ] **Step 1: Import and inject `MapViewState`**

In `frontend/src/app/pages/map-home/map-home.ts`, add the import near the other service imports (around line 5-6):

```typescript
import { MapViewState } from '../../services/map-view-state';
```

Then add the injection alongside the other `inject(...)` fields (around lines 26-31):

```typescript
  private mapViewState = inject(MapViewState);
```

- [ ] **Step 2: Restore on init and save on moveend**

In `initMap()`, replace this block (lines ~197-207):

```typescript
    const isMobile = window.innerWidth <= 480;

    this.map = L.map(el, {
      center: [43.0, -113.6],
      zoom: isMobile ? 4 : 6,
      minZoom: 4,
      maxZoom: 12,
      maxBounds: [[28, -130], [52, -100]],
      scrollWheelZoom: true,
      zoomControl: false,
    });
```

with:

```typescript
    const isMobile = window.innerWidth <= 480;
    const saved = this.mapViewState.load();

    this.map = L.map(el, {
      center: saved?.center ?? [43.0, -113.6],
      zoom: saved?.zoom ?? (isMobile ? 4 : 6),
      minZoom: 4,
      maxZoom: 12,
      maxBounds: [[28, -130], [52, -100]],
      scrollWheelZoom: true,
      zoomControl: false,
    });

    // Persist the view for the session so returning to the map (via any path)
    // lands back where the user left it. moveend covers both pan and zoom.
    this.map.on('moveend', () => {
      const c = this.map.getCenter();
      this.mapViewState.save({ center: [c.lat, c.lng], zoom: this.map.getZoom() });
    });
```

- [ ] **Step 3: Verify the frontend builds and existing specs pass**

Run (from `frontend/`): `npm run build`
Expected: build succeeds.

Run (from `frontend/`): `npm test`
Expected: PASS — existing `MapHome` specs still green (they flush HTTP and test pure helpers; injecting the root service does not change that, and the Leaflet init still does not run under jsdom).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/app/pages/map-home/map-home.ts
git commit -m "feat(map): restore saved zoom/center on return to the map"
```

> Manual check on the dev preview: zoom/pan the map, click a peak, then click "← Map" on the detail page — the map should reopen at the same zoom and center. A hard browser reload should reset to the default view.

---

## Manual verification (whole feature, on the dev preview)

After the branch is pushed and the `dev` preview builds:

1. **Glacier badge:** a glaciated marker's ❄ sits snug on the dot's upper-right, not in the far corner.
2. **AQI grade:** a peak whose current US AQI is ≥ 151 shows a capped grade (C at 151–200, F at 201+) with an "Air quality" factor card in the breakdown and a "Poor air quality" driver on the card/popup; a peak with AQI ≤ 100 shows no AQI factor card (only the "Air quality" tile), grade unchanged.
3. **Map restore:** zoom in, open a peak, return via "← Map" → same view; hard reload → default view.

---

## Self-review notes (author)

- **Spec coverage:** Feature 1 → Task 1. Feature 2 (factor, overall grade, window grades, aggregator wiring, invariants) → Tasks 2–5. Feature 3 (service + MapHome wiring, popup out of scope) → Tasks 6–7. All spec sections covered.
- **Invariants:** isStale asymmetry preserved by *not* touching `forceStale` (Task 5) and guarded by existing tests; ghost/no-data routes preserved by the `factors.Count > 0` guard (Task 3) plus the aggregator's existing null-grade rule.
- **Type consistency:** `AirQualityFactor` members (`Weight`, `IsActive`, `Score`, `Detail`, `Cap`, `ActiveFloorUsAqi`) are used identically in Task 3; `GradeCalculator.Compute` / `WindowGradeCalculator.Compute` gain the same optional `AirQualitySnapshot? airQuality = null` third parameter; `MapViewState.save/load` and the `MapView { center, zoom }` shape match between Tasks 6 and 7.
