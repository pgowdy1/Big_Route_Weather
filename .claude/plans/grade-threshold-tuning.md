# Grade Threshold Tuning

**Branch:** `feature/grade-threshold-tuning`
**Scope:** Small/medium — backend grading logic only
**Layers:** Backend (Core grading package + tests)
**Complexity:** Solo (single-context build)

## Goal

Tune the grade calculator so dangerous or very uncomfortable conditions disqualify high grades. Today the grade is a single weighted average — a perfect snowpack and mild temps can mask 25mph summit winds or 40% precip. We want:

- **Hard caps** — a single dangerous factor caps the overall grade regardless of the weighted average
- **Tighter curves** — factor scores ramp to zero earlier so factor-level signals match real-world severity

## Threshold spec

### Wind (sustained summit mph)
| Value | Cap | Score curve |
|---|---|---|
| > 20 mph | B | LinearBetween(value, 10, 40) — was 10/50 |
| > 30 mph | C | |
| > 40 mph | D | |
| > 50 mph | F | |

### Precipitation probability (%)
| Value | Cap | Score curve |
|---|---|---|
| > 30% | B | LinearBetween(pct, 0, 80) — was 100-pct (i.e. 0/100) |
| > 50% | C | |
| > 70% | D | |
| > 90% | F | |

### Temperature (summit °F) — symmetric extremes
| Value | Cap | Score curve |
|---|---|---|
| < 10 °F or > 75 °F | B | Unchanged (20-60 comfortable; 0 at -20 / 90) |
| < 0 °F or > 85 °F | C | |
| < -15 °F or > 95 °F | D | |

### Fresh snow on rock (inches, last 7 days)
| Value | Cap | Score curve |
|---|---|---|
| > 2 in | B | LinearBetween(value, 0, 4) — was 0/6 |
| > 4 in | C | |
| > 8 in | D | |

### Snowpack (% of normal SWE)
No hard cap. Existing curve unchanged.

## Cap application rule

1. Compute the weighted overall score and natural grade as today.
2. Collect cap candidates from each factor (return `(Grade?, reasonText)` per factor).
3. Pick the **worst** cap (highest enum value — A < B < C < D < F).
4. Effective grade = `max(naturalGrade, worstCap)`.
5. If a cap was applied (worstCap > naturalGrade):
   - Rationale text becomes: `Capped at {grade} — {reason}. {original rationale}`
   - The cap reason factor surfaces as a negative driver at position 0 (synthetic entry if it isn't already)

## Affected files

### Modified
- `backend/RouteWeather.Core/Grading/WindFactor.cs` — tighten curve (badValue 50→40), add `Cap(double)` returning `(Grade?, string)`
- `backend/RouteWeather.Core/Grading/PrecipitationFactor.cs` — tighten curve (use LinearBetween(pct, 0, 80)), add `Cap(int)`
- `backend/RouteWeather.Core/Grading/TemperatureFactor.cs` — add `Cap(double)` (curve unchanged)
- `backend/RouteWeather.Core/Grading/RecentSnowFactor.cs` — tighten curve (badValue 6→4), add `Cap(double)`
- `backend/RouteWeather.Core/Grading/GradeCalculator.cs` — collect caps, apply worst, augment rationale + drivers
- `backend/RouteWeather.Core.Tests/Grading/WindFactorTests.cs` — update for tightened curve + add Cap tests
- `backend/RouteWeather.Core.Tests/Grading/PrecipitationFactorTests.cs` — update curve test data + add Cap tests
- `backend/RouteWeather.Core.Tests/Grading/TemperatureFactorTests.cs` — add Cap tests
- `backend/RouteWeather.Core.Tests/Grading/RecentSnowFactorTests.cs` — update for tightened curve + add Cap tests
- `backend/RouteWeather.Core.Tests/Grading/GradeCalculatorTests.cs` — add cap-application tests; verify existing tests still hold

### New
None.

## API contract

No external API changes. `GradeResult` still has `Grade / OverallScore / Factors / Drivers / Rationale`. Existing endpoints unaffected. Frontend renders whatever comes back, including the updated rationale text — no frontend changes needed.

## Implementation steps

1. Add `Cap` static methods on each factor (wind, precip, temp, snow). Each returns `(Grade?, string Reason)`.
2. Tighten curves: wind badValue 40; precip LinearBetween(pct, 0, 80); recent snow badValue 4.
3. Update `GradeCalculator.Compute` to collect caps, pick the worst, and apply.
4. When cap > natural, prepend rationale and ensure cap factor appears as first driver.
5. Update factor unit tests for new curve constants.
6. Add new tests for each `Cap` method (thresholds + boundary values).
7. Add `GradeCalculator` tests showing cap behavior (e.g., perfect everything + 25mph wind → B).
8. Run `dotnet test` from `backend/`, fix until green.
9. Run frontend `npm test` from `frontend/` — should be unaffected; verifies regression.

## Edge cases

- **Both wind and precip trigger caps** → pick the worse one (e.g., wind 25 = cap B, precip 80% = cap D → effective D).
- **Cap is *softer* than natural** (e.g., wind = 22mph → cap B, but overall score 55 → natural D). Use `max`, so effective grade stays D. Test this.
- **Temperature symmetric** — both cold (< 10) and hot (> 75) trigger the same cap. Test both sides.
- **Snowpack missing** → no `RecentSnow` cap candidate. Cap selection still works on weather factors only.
- **Window grades** call `GradeCalculator.Compute` per window — caps apply per window automatically.
- **All factors missing** → existing "No data" path still returns F + empty result.

## Test plan

### Curve tests (updated)
- `WindFactor.Score(40)` → 0 (was test target 50)
- `PrecipitationFactor.Score(40)` → ~50 (instead of 60)
- `RecentSnowFactor.Score(4)` → 0 (was test target 6)

### Cap tests (new, one per factor)
- Wind cap returns `null` at 20, `B` at 21, `C` at 31, `D` at 41, `F` at 51.
- Precip cap returns `null` at 30, `B` at 31, `C` at 51, `D` at 71, `F` at 91.
- Temp cap returns `null` at 20 and 60, `B` at 9 and 76, `C` at -1 and 86, `D` at -16 and 96. Symmetric.
- Recent snow cap returns `null` at 2, `B` at 2.1, `C` at 4.1, `D` at 8.1.

### Integration tests (new)
- Perfect-everything + 25mph wind → effective grade B, rationale contains "Capped at B".
- Perfect-everything + 35% precip → effective grade B.
- Mixed-bad scenario (60mph wind + 80% precip + perfect snowpack) → effective grade F, worst cap wins.
- 22mph wind + otherwise-D-score conditions → grade stays D (cap doesn't *raise* grade).

## Verification commands

```bash
# from backend/
dotnet build
dotnet test --verbosity normal

# from frontend/
npm test
```

## Notes

- Cap reason strings should use the same phrasing as `Detail()` so the rationale reads cleanly (e.g., `"Capped at B — sustained 25 mph at summit."`).
- This is the right time to confirm `LabelFor` covers all factor names — we're not adding new factor names, so existing mapping holds.
