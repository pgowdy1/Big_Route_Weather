# Multi-Source Forecast Weighting (MVP)

**Branch:** `feature/multi-source-forecast-weighting`
**Base:** `dev`
**Scope:** Large (full-stack + new external integration + grading refactor)
**Complexity:** medium-large — recommend **solo build** with backend-first order; team build only if it stalls

## Goal

Today the grade card uses NWS for forecast + SNOTEL for snowpack. Adding more forecast models lets us (a) catch model bias and (b) measure agreement across models so the user knows how confident the grade is. MVP scope:

1. Introduce a clean source abstraction (`IForecastSource`, `ISnowpackSource`)
2. Add Open-Meteo as a second forecast provider, fetching 4 models in a single HTTP call (GFS, ECMWF, ICON, HRRR) — instantly gives a 5-member ensemble (NWS + 4)
3. Compute **per-factor coefficient of variation** across sources; the worst factor sets a high/medium/low **consensus level**
4. Blend forecast factors (mean) into the existing `WeatherSnapshot` so `GradeCalculator` works unchanged
5. Surface consensus as a **badge only** on the grade card — does not alter the grade itself
6. Best-effort source failures: a missing source is excluded from the ensemble and surfaced as "N of M sources reporting"
7. Configurable via `ForecastSources` section in appsettings — per-source enable/weight/TTL

## Non-goals (Phase 2+)

- Per-source per-row dashboard in the UI
- Skill-weighted blending by forecast horizon
- Pre-fetch cron / scheduled background warmer
- Paid Open-Meteo tier
- More than one Open-Meteo HTTP client (e.g. separate HRRR sub-source)

---

## Architecture

### Sources

Two interfaces in `RouteWeather.Core/Sources/`:

```csharp
public interface IForecastSource {
    string Name { get; }                 // "NWS", "OpenMeteo-GFS", etc.
    Task<WeatherSnapshot?> FetchAsync(double lat, double lon, CancellationToken ct);
}

public interface ISnowpackSource {
    string Name { get; }
    Task<SnowpackSnapshot?> FetchAsync(string stationTriplet, CancellationToken ct);
}
```

`NwsClient` implements `IForecastSource` (name "NWS").
`SnotelClient` implements `ISnowpackSource` (name "SNOTEL").

### Open-Meteo client

`OpenMeteoClient` makes **one** combined HTTP call:

```
GET https://api.open-meteo.com/v1/forecast
  ?latitude={lat}&longitude={lon}
  &hourly=temperature_2m,precipitation_probability,wind_speed_10m
  &models=gfs_global,ecmwf_ifs025,icon_global,hrrr
  &temperature_unit=fahrenheit&wind_speed_unit=mph
  &forecast_days=2&timezone=UTC
```

The response contains 4 parallel hourly arrays (suffixed `_gfs_global`, `_ecmwf_ifs025`, `_icon_global`, `_hrrr`). The client maps each model to a `WeatherSnapshot` and returns **a list of (sourceName, snapshot)** rather than a single snapshot.

To fit the `IForecastSource` interface cleanly while still doing one HTTP call, the client is registered as 4 logical sources that share a memoized fetch. Pattern:

- `OpenMeteoClient` is a singleton HTTP-level client with `FetchAllModelsAsync(lat, lon)` returning `IReadOnlyDictionary<string, WeatherSnapshot>` keyed by source name. It de-dupes concurrent calls per (lat,lon) with a `ConcurrentDictionary<key, Lazy<Task>>`.
- 4 thin `IForecastSource` implementations (`OpenMeteoGfsSource`, `OpenMeteoEcmwfSource`, `OpenMeteoIconSource`, `OpenMeteoHrrrSource`) each call `_client.FetchAllModelsAsync(...)` and pluck their key out. The aggregator-level cache (see below) ensures only one HTTP call per route per TTL window.

Cache key for the combined call: `"OpenMeteo:{lat:F4},{lon:F4}"`. The 4 per-model caches in the existing `ForecastCacheRepository` store each model snapshot separately so the per-source weight + freshness signals remain accurate.

### ConditionsAggregator refactor

```csharp
public ConditionsAggregator(
    IEnumerable<IForecastSource> forecastSources,   // all enabled per config
    IEnumerable<ISnowpackSource> snowpackSources,
    ForecastCacheRepository cache,
    IOptions<ForecastSourcesOptions> options,
    ConsensusCalculator consensus,
    ILogger<ConditionsAggregator> logger)
```

`GetConditionsAsync` flow:

1. Fan out forecast sources in parallel (each uses its own cached fetch path keyed by source name + per-source TTL). Memory rule: use `IDbContextFactory` for repo calls inside the fan-out — already in place.
2. Fan out snowpack sources in parallel.
3. Collect `(sourceName, snapshot)` tuples. Drop nulls.
4. Pass forecast results to `ConsensusCalculator.Compute(snapshots, weights)` → `(blendedSnapshot, ConsensusReport)`.
5. `GradeCalculator.Compute(blendedSnapshot, snowpack)` unchanged.
6. `RouteConditions` extended with `Consensus` + `PerSourceForecast` (detail only).

### ConsensusCalculator

`RouteWeather.Core/Grading/ConsensusCalculator.cs`:

```csharp
public record ConsensusReport(
    ConsensusLevel Level,
    string WorstFactor,
    IReadOnlyDictionary<string, double> CoefficientOfVariationByFactor,
    int SourcesReporting,
    int SourcesAttempted);

public enum ConsensusLevel { High, Medium, Low }
```

For each factor (Wind, Temp, Precip) across N source snapshots:
- Compute weighted mean (using configured source weights) and standard deviation.
- CV = stddev / |mean|. Guard against mean ≈ 0 by adding small epsilon and falling back to absolute spread for near-zero means (e.g. precipitation probability of 0).
- Worst-factor CV picks the consensus level:
  - **High**: CV ≤ 0.15 — sources agree closely
  - **Medium**: 0.15 < CV ≤ 0.35
  - **Low**: CV > 0.35 — sources disagree
- Thresholds live in `ForecastSourcesOptions.ConsensusThresholds` so they can be tuned without a deploy-and-recompile.

Special cases:
- 0 sources reporting → grade is null (existing behavior); no consensus computed.
- 1 source reporting → `Level = High` with a "single source" flag set; UI shows "1 of M sources reporting".

The blended snapshot is the weighted mean per scalar field. `Next48Hours` is blended hour-by-hour (matched by hour-of-forecast). When NWS returns a slightly different hourly grid, snap to NWS's hour boundaries — NWS is the baseline.

### Configuration

`appsettings.json`:

```json
{
  "ForecastSources": {
    "ConsensusThresholds": { "HighMaxCv": 0.15, "MediumMaxCv": 0.35 },
    "Sources": [
      { "Name": "NWS",              "Enabled": true, "Weight": 1.0, "CacheTtlMinutes": 60 },
      { "Name": "OpenMeteo-GFS",    "Enabled": true, "Weight": 1.0, "CacheTtlMinutes": 180 },
      { "Name": "OpenMeteo-ECMWF",  "Enabled": true, "Weight": 1.0, "CacheTtlMinutes": 180 },
      { "Name": "OpenMeteo-ICON",   "Enabled": true, "Weight": 1.0, "CacheTtlMinutes": 180 },
      { "Name": "OpenMeteo-HRRR",   "Enabled": true, "Weight": 1.2, "CacheTtlMinutes": 180 }
    ]
  }
}
```

`Program.cs` reads the section, registers each enabled source. Disabled sources are not registered.

A `DailyCallCounter` (singleton) increments per source per day and logs daily totals at info level so the Open-Meteo budget is visible. Logged daily at midnight UTC and on app start.

---

## Affected files

### Existing — modify

- `backend/RouteWeather.API/Services/NwsClient.cs` — implement `IForecastSource`; add `Name` property
- `backend/RouteWeather.API/Services/SnotelClient.cs` — implement `ISnowpackSource`; add `Name` property
- `backend/RouteWeather.API/Services/ConditionsAggregator.cs` — fan-out refactor; use cache per source name; per-source TTL from config
- `backend/RouteWeather.API/Program.cs` — bind `ForecastSourcesOptions`; register IForecastSource/ISnowpackSource per config; register `OpenMeteoClient` + the 4 logical sources; register `ConsensusCalculator`, `DailyCallCounter`
- `backend/RouteWeather.API/appsettings.json` (and Development) — add `ForecastSources` section
- `backend/RouteWeather.Core/Models/RouteConditions.cs` — add `Consensus` + `IReadOnlyList<SourceSnapshot>? PerSourceForecast` (detail only — summary stays lean)
- `frontend/src/app/models/route-conditions.ts` — add `Consensus` interface + field on `RouteSummary` (summary-level Level only) and on `RouteDetail` (full report + per-source values)
- `frontend/src/app/pages/peak-detail/peak-detail.html` — render consensus badge near grade
- `frontend/src/app/pages/peak-detail/peak-detail.scss` — styles for badge (mind the 5kB budget; compact)
- `frontend/src/app/pages/route-list/route-list.html` (or wherever route cards live) — small consensus badge on each card

### New

- `backend/RouteWeather.Core/Sources/IForecastSource.cs`
- `backend/RouteWeather.Core/Sources/ISnowpackSource.cs`
- `backend/RouteWeather.Core/Sources/SourceSnapshot.cs` — `(string SourceName, WeatherSnapshot Snapshot, DateTimeOffset FetchedAt)`
- `backend/RouteWeather.API/Services/OpenMeteoClient.cs` — combined-call HTTP client
- `backend/RouteWeather.API/Services/OpenMeteoSources.cs` — 4 thin `IForecastSource` adapters
- `backend/RouteWeather.API/Services/DailyCallCounter.cs`
- `backend/RouteWeather.API/Options/ForecastSourcesOptions.cs`
- `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs`
- `backend/RouteWeather.Core/Models/ConsensusReport.cs`
- `backend/RouteWeather.Tests/Sources/OpenMeteoClientTests.cs`
- `backend/RouteWeather.Tests/Grading/ConsensusCalculatorTests.cs`
- `backend/RouteWeather.Tests/Services/ConditionsAggregatorTests.cs` (or extend existing)
- `frontend/src/app/components/consensus-badge/consensus-badge.ts|.html|.scss|.spec.ts`

---

## API contract changes

`GET /api/routes` (list)
```jsonc
{
  // existing fields...
  "consensus": { "level": "high" | "medium" | "low" | null }
}
```

`GET /api/routes/{slug}` (detail)
```jsonc
{
  // existing fields...
  "consensus": {
    "level": "high",
    "worstFactor": "Precipitation",
    "coefficientOfVariationByFactor": { "wind": 0.08, "temperature": 0.04, "precipitation": 0.12 },
    "sourcesReporting": 4,
    "sourcesAttempted": 5
  },
  "perSourceForecast": [
    { "sourceName": "NWS",             "windMph": 22.1, "tempF": 28.4, "precipitationProbabilityPct": 35, "fetchedAt": "..." },
    { "sourceName": "OpenMeteo-GFS",   "windMph": 19.8, "tempF": 29.0, "precipitationProbabilityPct": 30, "fetchedAt": "..." },
    // ...
  ]
}
```

Existing fields are unchanged. `consensus` is null only if 0 sources reported (matches the existing `grade: null` case).

---

## Data model

No DB schema changes. The existing `forecast_cache` table already keys by `(routeId, sourceName)` — we just write more rows (one per source name).

---

## Implementation steps (backend first)

1. **Interfaces** — `IForecastSource`, `ISnowpackSource`, `SourceSnapshot` record. Compile-only.
2. **Port NwsClient** — implement `IForecastSource`. Compile + run existing tests.
3. **Port SnotelClient** — implement `ISnowpackSource`. Compile + tests.
4. **OpenMeteoClient** — combined HTTP call + per-model mapping. Unit test the mapping against a canned response.
5. **4 logical IForecastSource adapters** — each calls the shared client.
6. **ConsensusCalculator** — pure computation. Heavy unit test coverage (single source, identical sources, divergent sources, zero-mean precip).
7. **ConditionsAggregator refactor** — fan out; use per-source TTL; blend via ConsensusCalculator. Unit test best-effort failure path with a stubbed source.
8. **DTO extensions** — `Consensus` + `PerSourceForecast` on `RouteConditions`. Update the controller mapping.
9. **Config + DI** — `ForecastSourcesOptions`, `Program.cs` wiring, `DailyCallCounter`. Add `OpenMeteo` HttpClient registration.
10. **Backend build + test verify** — `dotnet test`.
11. **Frontend interface update** — `route-conditions.ts` add `Consensus`. TS will surface every spec that builds a `RouteSummary` literal; update fixtures.
12. **ConsensusBadge component** — standalone signal-based, takes `consensus()` as `input.required<Consensus | null>()`. Maps level → color/text. Renders "N of M sources reporting" and worst factor name when level is medium/low.
13. **Wire badge** — into peak-detail header + each route-list card.
14. **Frontend tests** — Vitest. Anchor on attrs/sections per the static-page rule. Update `RouteSummary` fixture builders.
15. **Full build + test pipeline** — backend + frontend.

---

## Edge cases & error handling

- **All sources fail** → existing null-grade path; `consensus = null`.
- **Single source responds** → consensus level "high" with a `singleSource: true` flag (or just rely on `sourcesReporting === 1`); UI shows "1 of M sources reporting" badge.
- **Source returns degenerate data** (NaN, infinity from upstream malformed JSON) → filtered out before consensus calc; same path as a failed source.
- **Zero-mean precipitation across all sources** (everyone says 0%) → CV undefined; treat as perfect consensus on that factor.
- **Open-Meteo rate limit reached** (HTTP 429) → log + best-effort fallback; the 4 logical sources all drop out for this fetch; SourcesReporting = 1 (NWS).
- **Open-Meteo timeout** (15s default) → drop the 4 logical sources; same as failure.
- **Hourly grid mismatch** between NWS and Open-Meteo → snap Open-Meteo to NWS hour boundaries by nearest-hour matching.
- **Cache stampede on cold start** with 500 routes → existing per-slug `SemaphoreSlim Gates` in `ConditionsAggregator` already serializes per route; the combined Open-Meteo client's per-(lat,lon) dedupe adds a second layer.

---

## Test plan

### Backend

**ConsensusCalculator** (`backend/RouteWeather.Tests/Grading/ConsensusCalculatorTests.cs`):
- 5 sources, all identical → Level=High, worstFactor=any (lowest CV).
- 5 sources, wind CV=0.4, others tight → Level=Low, worstFactor="Wind".
- 2 sources, moderate spread → Level=Medium reproducibly.
- 1 source → Level=High, SourcesReporting=1.
- 0 sources → `ConsensusReport` is null (or aggregator skips).
- All sources report precip=0 → no NaN; CV treated as 0.
- Weighted blend respects per-source `Weight` (use 2.0 weight on one source and verify blended mean shifts).

**OpenMeteoClient** (`backend/RouteWeather.Tests/Sources/OpenMeteoClientTests.cs`):
- Canned JSON response with 4 model arrays → mapper returns 4 snapshots keyed by source name.
- Each snapshot has wind/temp/precip pulled from the first hourly entry and Next48Hours populated.
- Concurrent calls for the same (lat,lon) result in **one** HTTP request (dedupe test).

**ConditionsAggregator** (extend existing test or new file):
- 2 forecast sources, 1 fails → blended grade computed from the 1 successful; `SourcesReporting=1, SourcesAttempted=2`.
- All forecast sources disabled in config → only NWS-like path... actually, all forecast sources disabled means grade is null. Test the empty path.
- Per-source TTL respected: cache row with `expiresAtUtc` in future returns cached, doesn't call source.

**GradeCalculator** — no new tests; should be unchanged. Existing tests must still pass.

### Frontend (Vitest)

**ConsensusBadge** (`frontend/src/app/components/consensus-badge/consensus-badge.spec.ts`):
- Renders `data-level="high"` attribute when level is high.
- Shows "N of M sources reporting" text region (anchor on the `[data-testid="sources-count"]` element, not the literal copy).
- Shows worst factor name when level is medium/low.
- Hides itself (renders nothing) when consensus is null.

**peak-detail.spec.ts**:
- Update RouteDetail fixture to include `consensus` and `perSourceForecast` (memory: tests must include every field on RouteSummary/RouteDetail).
- Badge renders inside the grade card region.

**route-list.spec.ts**:
- Each route card includes the badge for routes with consensus.

---

## Verification commands

```powershell
# Backend build (Core-only because API may be running per memory)
dotnet build C:\Users\pgowd\Documents\Big_Route_Weather\backend\RouteWeather.Core\RouteWeather.Core.csproj

# Full backend build + test
dotnet build
dotnet test --verbosity normal

# Frontend
cd frontend; npm test; cd ..
```

Manual checklist after pipeline:
- Open peak-detail for a route → consensus badge appears.
- Check `/api/routes/{slug}` response includes `consensus` + `perSourceForecast`.
- Disable a source via appsettings → that source no longer appears in `perSourceForecast`.
- Force an Open-Meteo timeout (e.g. point to bad URL) → page still loads with NWS-only and shows "1 of 5 sources reporting".
- Log output shows daily Open-Meteo call counter.

---

## Complexity assessment

**Solo build.** The work is mechanically straightforward — interfaces, one new client, one calculator, one badge component. The blast radius is contained (forecast fetch + grade pipeline + one DTO + one UI component). No DB migration. No new deploy infra. Backend-first order keeps the contract stable for the frontend.
