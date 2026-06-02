# Plan: Seasonal Snow Weighting

**Feature:** Routes with no current snowpack and no forecast snow shouldn't have snow-related factors dragging the grade math. Reweight to zero in summer-like conditions; re-enable when there's snow on the ground or in the forecast.

**Branch:** `feature/seasonal-snow-weighting`
**Scope:** Medium — backend grading logic + tests + small frontend display tweak.
**Layers:** Full-stack (backend grading change drives a UI presentation tweak).

## Decisions

| Question | Answer |
| --- | --- |
| Trigger | Depth + forecast: drop snow factors when depth = 0 AND no snow forecast in 48h |
| Depth cutoff | Exactly 0 inches |
| Forecast snow detection | `ShortForecast` contains "snow" AND `PrecipitationProbabilityPct ≥ 50` (NWS "Likely" threshold) — skips "Slight chance" (10–20%) and "Chance" (30–50%) snow wording |
| UI for inactive factors | Show factor cards at bottom of factor list, dimmed, labeled "Not a factor today" |
| Grid view | No change — drivers list will naturally drop snow when inactive |
| If snow is forecast (depth still 0) | Re-enable RecentSnowFactor only; SnowpackFactor stays inactive |
| SNOTEL null | Treat as "no snow" — skip both factors unless forecast says otherwise |
| Cache | Recompute relevance per request; no cache shape change |

## Behavior matrix

RecentSnow is gated on actual recent snow OR forecast snow — independent of depth. Snowpack is gated on depth.

| SnowDepthIn | NewSnowLast7DaysIn | Snow forecast? | RecentSnowFactor | SnowpackFactor |
| --- | --- | --- | --- | --- |
| > 0 | > 0 | any | active | active |
| > 0 | 0 | yes | active | active |
| > 0 | 0 | no | **inactive** | active |
| 0 or null | > 0 | any | active | inactive |
| 0 or null | 0 | yes | active | inactive |
| 0 or null | 0 | no | inactive | inactive |

When a factor is "inactive", it's surfaced to the UI but contributes 0 weight to the overall score and does not generate a cap.

## Files

**Backend (changes):**
- `backend/RouteWeather.Core/Grading/GradeCalculator.cs` — decide snow factor activation; mark inactive factors; exclude them from weight sum and cap candidates.
- `backend/RouteWeather.Core/Models/FactorScore.cs` — add `IsActive` flag (default true to keep existing call sites).
- `backend/RouteWeather.Core.Tests/Grading/GradeCalculatorTests.cs` — new tests for the activation matrix.

**Backend (new):**
- `backend/RouteWeather.Core/Grading/SnowRelevance.cs` — pure function: `IsSnowExpected(weather)`, `HasSnowOnGround(snowpack)`, `Evaluate(weather, snowpack) -> (recentActive, snowpackActive)`. Keeps the heuristic in one place.
- `backend/RouteWeather.Core.Tests/Grading/SnowRelevanceTests.cs` — unit tests on the pure function.

**Frontend (changes):**
- `frontend/src/app/models/route-conditions.ts` — extend `FactorScore` with `isActive: boolean`.
- `frontend/src/app/pages/peak-detail/peak-detail.html` — split factor list into active + inactive; dim inactive cards with "Not a factor today" badge.
- `frontend/src/app/pages/peak-detail/peak-detail.scss` — add `.factor-card.inactive` style (opacity, label).
- `frontend/src/app/pages/peak-detail/peak-detail.ts` — computed signals `activeFactors`, `inactiveFactors` derived from `detail()?.factors`.

**Frontend (tests):**
- `frontend/src/app/pages/peak-detail/peak-detail.spec.ts` — assert inactive factors render with the dim class and label.

## API contract

The `FactorScore` JSON shape gains one field, default `true` for back-compat:

```json
{
  "name": "Recent snow",
  "score": 100,
  "weight": 0.2,
  "detail": "0.0\" new snow in last 7 days",
  "isActive": false
}
```

Inactive factors still appear in `factors[]` and `windowGrades.*.factors[]`. They are excluded from:
- Driver computation (already handled — they score 100 so wouldn't be a negative driver)
- Weight sum in the overall score calculation
- Cap candidate list (RecentSnow won't generate a cap when inactive)

## Implementation steps

1. **Add `SnowRelevance` module** with pure `Evaluate` returning `(bool recentActive, bool snowpackActive)`. Cover: weather-null, snowpack-null, depth=0 + no forecast, depth=0 + ShortForecast contains "snow", depth=0 + cold-and-wet hour, depth>0.
2. **Add `IsActive` to `FactorScore`** with default `true` to leave callers untouched.
3. **Update `GradeCalculator.Compute`** to call `SnowRelevance.Evaluate`, mark snow factors `IsActive` accordingly, skip inactive factors from weight sum and cap candidates. Keep them in the factor list so they surface to the UI.
4. **Update `WindowGradeCalculator`** — already passes the same `weather`+`snowpack` into `GradeCalculator.Compute`, so the per-window slice will use that window's forecast. Verify the 12h/24h windows behave reasonably (a 12h window with no snow hours should also flip RecentSnow off if depth is 0).
5. **Backend tests** — add tests for: depth=0 + clear forecast → both inactive; depth=0 + snowy forecast → RecentSnow active, Snowpack inactive; depth>0 → both active; weight redistribution proven by overall score.
6. **Frontend types** — add `isActive` to `FactorScore` interface.
7. **Frontend detail view** — split factor cards into active (top) + inactive (bottom, dimmed), add label.
8. **Frontend tests** — assert inactive cards render with class + label.

## Edge cases

- `weather` is null but `snowpack` has depth > 0 → snowpack-only path: SnowpackFactor active, RecentSnow active (snow on the ground regardless of forecast).
- `weather` is null and `snowpack` is null → unchanged: no factors, Grade.F + "no data".
- `snowpack` is null but forecast contains snow → RecentSnow active (since snow incoming), SnowpackFactor inactive (no observation).
- Depth = 0 today but cap-relevant `NewSnowLast7DaysIn` > 0 (very unusual — depth dropped to zero in 7 days) → if forecast clear, RecentSnow inactive. The melted-already case is correctly handled by ignoring stale recent-snow signal.
- 12h window where forecast is clear, but 48h window contains a snow hour → 12h window will mark RecentSnow inactive while 48h marks it active. Acceptable; that's the point of windowed grades.

## Test plan

**Backend (xUnit):**
- `SnowRelevanceTests`:
  - Depth > 0 → both active
  - Depth 0, no forecast snow → both inactive
  - Depth 0, ShortForecast "Snow showers" → recent active, snowpack inactive
  - Depth 0, hour with 32°F + 40% precip → recent active, snowpack inactive
  - Snowpack null, no forecast snow → both inactive
  - Snowpack null, forecast snow → recent active, snowpack inactive
- `GradeCalculatorTests` additions:
  - Summer perfect weather, depth 0, no forecast snow → snow factors are present but `IsActive=false`; overall score equals the weighted average of weather factors only; cap candidates ignore RecentSnow.
  - Forecasted snow with depth 0 → RecentSnow `IsActive=true`, SnowpackFactor `IsActive=false`.

**Frontend (Vitest):**
- `peak-detail.spec.ts`:
  - Given a detail payload with one inactive factor, render assertions: factor still in the DOM, has `inactive` class, and shows "Not a factor today" text.

## Verification commands

```powershell
# Backend build (Core only — backend may be running per memory)
dotnet build "C:\Users\pgowd\Documents\Big_Route_Weather\backend\RouteWeather.Core\RouteWeather.Core.csproj"
dotnet test "C:\Users\pgowd\Documents\Big_Route_Weather\backend\RouteWeather.Core.Tests\RouteWeather.Core.Tests.csproj"

# Frontend (run from frontend/)
npm test
npx ng build
```

## Complexity assessment

**Path A (solo).** All changes are touching well-understood modules (single calculator file, one factor model field, one component template/style). No new external dependencies. Recommended `/new-feature` style — single agent, incremental edits.
