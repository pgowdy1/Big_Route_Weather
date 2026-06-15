# Precipitation Consensus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the thin 2-source `MAX`-PoP precipitation signal with a per-hour, confidence-weighted multi-source consensus so uncorroborated single-model spikes no longer cap the route grade.

**Architecture:** Every forecast source casts a per-hour precip *vote* in [0,1] — NWS/GFS vote with their reported probability, the three amount-only models vote via a soft ramp on hourly QPF. The weighted average of votes (NWS upweighted via a config knob) becomes the hour's consensus probability and replaces `PrecipitationProbabilityPct` in the blended hourly series, so the existing cap/score/window machinery keeps working on a number that finally reflects all five sources.

**Tech Stack:** C# / .NET 10, xUnit. Changes span `RouteWeather.Core` (consensus + vote helper) and `RouteWeather.API` (options + DI wiring + appsettings).

**Spec:** `docs/superpowers/specs/2026-06-15-precip-consensus-design.md`

**Branch:** `feature/precip-consensus` (already created off `dev`).

**Build/test note:** The dev API may be running and holding a lock on the built Core DLL. Always build and test by **explicit csproj path** (as every command below does) — do NOT run `dotnet build`/`dotnet test` from `backend/`, and do NOT start or stop the servers.

---

## File Structure

**Create:**
- `backend/RouteWeather.Core/Grading/PrecipVote.cs` — pure function: one source's per-hour precip → [0,1] vote (PoP passthrough or QPF ramp). One responsibility, no dependencies beyond models/sources.
- `backend/RouteWeather.Core.Tests/Grading/PrecipVoteTests.cs` — unit tests for the vote/ramp.
- `backend/RouteWeather.API.Tests/Options/ForecastSourcesOptionsTests.cs` — tests for the new NWS precip-vote-weight accessor.

**Modify:**
- `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs` — add `EffectivePrecipWeight` to `ConsensusInput`; add `HourlyPrecipConsensus`; rewire `BlendHourly` and the `BlendSnapshots` headline.
- `backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs` — new consensus-behavior facts.
- `backend/RouteWeather.API/Options/ForecastSourcesOptions.cs` — add `PrecipVoteWeight` to `SourceOptions` + `PrecipVoteWeightFor`.
- `backend/RouteWeather.API/Services/ConditionsAggregator.cs` — pass the precip vote weight into `ConsensusInput`.
- `backend/RouteWeather.API/appsettings.json` — add `"PrecipVoteWeight": 1.75` to the NWS source.

**Not touched:** upstream fetch/parse code, `PrecipitationFactor` cap/score thresholds, the amount-consensus path, `WindowGradeCalculator`. The muted low-confidence UI indicator is a separate follow-on, out of scope here.

---

## Task 1: PrecipVote helper (per-source vote)

**Files:**
- Create: `backend/RouteWeather.Core/Grading/PrecipVote.cs`
- Test: `backend/RouteWeather.Core.Tests/Grading/PrecipVoteTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/RouteWeather.Core.Tests/Grading/PrecipVoteTests.cs`:

```csharp
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class PrecipVoteTests
{
    private static SourceSnapshot Src(bool reportsPop) =>
        new("S",
            new WeatherSnapshot(0, 0, 0, Array.Empty<HourlyForecast>()),
            DateTimeOffset.UtcNow,
            reportsPop ? ForecastFactors.All : ForecastFactors.WindAndTemperatureOnly);

    private static HourlyForecast Hr(int pop, double? amount) =>
        new(DateTimeOffset.UtcNow, TempF: 0, WindMph: 0, PrecipitationProbabilityPct: pop,
            ShortForecast: "", PrecipitationIn: amount);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.005, 0.0)]   // at the dry floor
    [InlineData(0.0275, 0.5)]  // midpoint of 0.005..0.05
    [InlineData(0.05, 1.0)]    // at the certain ceiling
    [InlineData(0.1, 1.0)]     // above the ceiling
    public void Ramp_maps_qpf_to_vote(double qpf, double expected)
    {
        Assert.Equal(expected, PrecipVote.Ramp(qpf), 3);
    }

    [Fact]
    public void For_probability_source_votes_with_pop()
    {
        Assert.Equal(0.6, PrecipVote.For(Src(reportsPop: true), Hr(pop: 60, amount: null))!.Value, 3);
    }

    [Fact]
    public void For_amount_source_votes_via_ramp()
    {
        Assert.Equal(1.0, PrecipVote.For(Src(reportsPop: false), Hr(pop: 0, amount: 0.1))!.Value, 3);
    }

    [Fact]
    public void For_amount_source_with_no_amount_abstains()
    {
        Assert.Null(PrecipVote.For(Src(reportsPop: false), Hr(pop: 0, amount: null)));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~PrecipVoteTests"`
Expected: FAIL to compile — `PrecipVote` does not exist.

- [ ] **Step 3: Write the minimal implementation**

Create `backend/RouteWeather.Core/Grading/PrecipVote.cs`:

```csharp
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;

namespace RouteWeather.Core.Grading;

/// <summary>
/// Converts a single source's per-hour precip into a [0,1] "vote" for the
/// confidence-weighted consensus. Probability sources (NWS, GFS) vote with their
/// PoP; amount-only models vote via a soft ramp on hourly QPF, so the field all
/// five sources report drives a genuine multi-source agreement signal.
/// </summary>
public static class PrecipVote
{
    // QPF (inches/hour) at/below LoIn reads as dry; at/above HiIn as certain precip.
    public const double LoIn = 0.005;
    public const double HiIn = 0.05;

    /// <summary>Vote in [0,1], or null when the source has no precip signal this hour.</summary>
    public static double? For(SourceSnapshot source, HourlyForecast hour)
    {
        if (source.ActiveFactors.Contains(ForecastFactors.Precipitation))
            return Math.Clamp(hour.PrecipitationProbabilityPct / 100.0, 0.0, 1.0);
        return hour.PrecipitationIn is null ? null : Ramp(hour.PrecipitationIn.Value);
    }

    public static double Ramp(double qpfInPerHr)
    {
        if (qpfInPerHr <= LoIn) return 0.0;
        if (qpfInPerHr >= HiIn) return 1.0;
        return (qpfInPerHr - LoIn) / (HiIn - LoIn);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~PrecipVoteTests"`
Expected: PASS (8 tests: 5 Theory cases + 3 Facts).

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/PrecipVote.cs backend/RouteWeather.Core.Tests/Grading/PrecipVoteTests.cs
git commit -m "feat(grading): add PrecipVote per-source precip vote helper

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: ConsensusInput precip vote weight

**Files:**
- Modify: `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs:6`
- Test: `backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs`

- [ ] **Step 1: Write the failing test**

Append this fact to `backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs` (inside the class, before the closing brace):

```csharp
    [Fact]
    public void EffectivePrecipWeight_overridesWeight_whenSet_elseFallsBack()
    {
        var src = new SourceSnapshot("NWS", Snapshot(10, 30, 20), DateTimeOffset.UtcNow, ForecastFactors.All);

        var withOverride = new ConsensusInput(src, Weight: 1.0, PrecipVoteWeight: 1.75);
        var withoutOverride = new ConsensusInput(src, Weight: 1.2);

        Assert.Equal(1.75, withOverride.EffectivePrecipWeight);
        Assert.Equal(1.2, withoutOverride.EffectivePrecipWeight);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~EffectivePrecipWeight"`
Expected: FAIL to compile — `ConsensusInput` has no `PrecipVoteWeight` parameter or `EffectivePrecipWeight` property.

- [ ] **Step 3: Write the minimal implementation**

In `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs`, replace line 6:

```csharp
public record ConsensusInput(SourceSnapshot Source, double Weight);
```

with:

```csharp
public record ConsensusInput(SourceSnapshot Source, double Weight, double? PrecipVoteWeight = null)
{
    // Precip voting uses a source-specific weight (NWS is upweighted); falls back to Weight.
    public double EffectivePrecipWeight => PrecipVoteWeight ?? Weight;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~EffectivePrecipWeight"`
Expected: PASS. The optional parameter keeps every existing `new ConsensusInput(source, weight)` call compiling.

- [ ] **Step 5: Commit**

```bash
git add backend/RouteWeather.Core/Grading/ConsensusCalculator.cs backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs
git commit -m "feat(grading): add EffectivePrecipWeight to ConsensusInput

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Per-hour precip consensus + headline cleanup

This is the core change. `BlendHourly` computes each blended hour's PoP from the weighted vote of **all** sources (via `PrecipVote`), and `BlendSnapshots` derives the headline PoP from the blended hourly series (falling back to the old weighted mean only when there is no hourly data, which keeps existing empty-fixture tests valid).

**Files:**
- Modify: `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs` (`BlendSnapshots`, `BlendHourly`, new private method)
- Test: `backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs`

- [ ] **Step 1: Add the shared precip-fixture builder to the test class**

Append this helper to `ConsensusCalculatorTests` (inside the class). It builds a single-hour source where the hour carries both a PoP and an amount, with the right ActiveFactors and an optional precip vote weight:

```csharp
    // Single-hour source at a fixed time. reportsPop=true => NWS/GFS-style (votes via PoP);
    // reportsPop=false => amount-only model (votes via QPF ramp).
    private static ConsensusInput PrecipInput(
        string name, int popPct, double? amountIn, bool reportsPop,
        double weight = 1.0, double? precipVoteWeight = null)
    {
        var time = DateTimeOffset.Parse("2026-06-15T20:00:00Z");
        var active = reportsPop ? ForecastFactors.All : ForecastFactors.WindAndTemperatureOnly;
        var hour = new HourlyForecast(time, TempF: 50, WindMph: 10,
            PrecipitationProbabilityPct: popPct, ShortForecast: "", PrecipitationIn: amountIn);
        var snap = new WeatherSnapshot(WindMph: 10, TempF: 50,
            PrecipitationProbabilityPct: popPct, Next48Hours: new[] { hour }, PrecipAmountIn: amountIn);
        return new ConsensusInput(new SourceSnapshot(name, snap, DateTimeOffset.UtcNow, active),
            weight, precipVoteWeight);
    }
```

- [ ] **Step 2: Write the failing regression test (the original bug)**

Append to `ConsensusCalculatorTests`:

```csharp
    [Fact]
    public void Lone_model_precip_spike_is_discounted_toward_consensus()
    {
        // GFS over-forecasts (55%), NWS calm (15%), the other three models dry.
        // Old behavior: mean(NWS, GFS) = 35% -> caps grade at B. New: diluted to 14%.
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            PrecipInput("NWS",             popPct: 15, amountIn: null, reportsPop: true,  precipVoteWeight: 1.75),
            PrecipInput("OpenMeteo-GFS",   popPct: 55, amountIn: 0.0,  reportsPop: true),
            PrecipInput("OpenMeteo-ECMWF", popPct: 0,  amountIn: 0.0,  reportsPop: false),
            PrecipInput("OpenMeteo-ICON",  popPct: 0,  amountIn: 0.0,  reportsPop: false),
            PrecipInput("OpenMeteo-HRRR",  popPct: 0,  amountIn: 0.0,  reportsPop: false, weight: 1.2),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 5);
        Assert.Equal(14, result.Blended!.PrecipitationProbabilityPct);
        Assert.Null(PrecipitationFactor.Cap(result.Blended.PrecipitationProbabilityPct).Cap);
    }
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~Lone_model_precip_spike"`
Expected: FAIL — current code yields `mean(15, 55) = 35`, not 14 (and `Cap` would be Grade.B, not null).

- [ ] **Step 4: Implement the per-hour consensus**

In `backend/RouteWeather.Core/Grading/ConsensusCalculator.cs`, in `BlendHourly`, delete the now-unused `precipInputs` local (currently line 87):

```csharp
        var precipInputs = Active(inputs, ForecastFactors.Precipitation);
```

and replace the per-hour precip line (currently line 95):

```csharp
            var precip = HourlyMean(precipInputs, hour, h => h.PrecipitationProbabilityPct, baseline[i].PrecipitationProbabilityPct);
```

with:

```csharp
            var precip = HourlyPrecipConsensus(inputs, hour, baseline[i].PrecipitationProbabilityPct);
```

Then add this new private method to the class (place it directly after `HourlyMean`):

```csharp
    // Confidence-weighted precip consensus for one hour. Every source votes (PoP
    // sources via probability, amount-only models via a QPF ramp), weighted by
    // EffectivePrecipWeight. Sources with no precip signal this hour abstain, so an
    // uncorroborated single-model spike is diluted toward the multi-source agreement.
    // Returns 0..100; falls back to the heaviest source's PoP only if nobody votes.
    private static double HourlyPrecipConsensus(
        IReadOnlyList<ConsensusInput> inputs,
        DateTimeOffset target,
        double fallback)
    {
        var sum = 0.0;
        var weight = 0.0;
        foreach (var input in inputs)
        {
            var match = FindNearestHour(input.Source.Snapshot.Next48Hours, target);
            if (match is null) continue;
            var vote = PrecipVote.For(input.Source, match);
            if (vote is null) continue;
            var w = input.EffectivePrecipWeight;
            sum += vote.Value * w;
            weight += w;
        }
        return weight <= 0 ? fallback : sum * 100.0 / weight;
    }
```

- [ ] **Step 5: Run the regression test to verify it passes**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter "FullyQualifiedName~Lone_model_precip_spike"`
Expected: PASS. (`0.15*1.75 + 0.55*1.0 = 0.8125`, total weight `5.95`, `0.8125*100/5.95 = 13.66 -> 14`.)

- [ ] **Step 6: Fix the headline to match (BlendSnapshots)**

In `BlendSnapshots`, remove the now-unused precip mean (currently line 49):

```csharp
        var precip = WeightedMean(precipInputs, s => s.PrecipitationProbabilityPct);
```

Then change the snapshot's `PrecipitationProbabilityPct` (currently line 63) from:

```csharp
            PrecipitationProbabilityPct: (int)Math.Round(precip),
```

to derive from the blended hourly series, with the old weighted mean kept only as an empty-data fallback:

```csharp
            PrecipitationProbabilityPct: blendedHours.Count == 0
                ? (int)Math.Round(WeightedMean(precipInputs, s => s.PrecipitationProbabilityPct))
                : blendedHours.Max(h => h.PrecipitationProbabilityPct),
```

Leave `var precipInputs = Active(inputs, ForecastFactors.Precipitation);` in `BlendSnapshots` (line 45) — it is now used by the fallback above. (The CV/consensus-report path in `ComputeCvByFactor` is intentionally unchanged.)

- [ ] **Step 7: Add the remaining behavior tests**

Append to `ConsensusCalculatorTests`:

```csharp
    [Fact]
    public void Corroborated_storm_drives_consensus_up_and_caps_the_grade()
    {
        // All five wet: NWS 50% & GFS 50% PoP, three models at 0.1" (ramp -> 1.0).
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            PrecipInput("NWS",             popPct: 50, amountIn: null, reportsPop: true,  precipVoteWeight: 1.75),
            PrecipInput("OpenMeteo-GFS",   popPct: 50, amountIn: 0.1,  reportsPop: true),
            PrecipInput("OpenMeteo-ECMWF", popPct: 0,  amountIn: 0.1,  reportsPop: false),
            PrecipInput("OpenMeteo-ICON",  popPct: 0,  amountIn: 0.1,  reportsPop: false),
            PrecipInput("OpenMeteo-HRRR",  popPct: 0,  amountIn: 0.1,  reportsPop: false, weight: 1.2),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 5);
        Assert.Equal(77, result.Blended!.PrecipitationProbabilityPct);
        Assert.Equal(Grade.D, PrecipitationFactor.Cap(result.Blended.PrecipitationProbabilityPct).Cap);
    }

    [Fact]
    public void Lone_nws_pop_with_dry_models_is_discounted()
    {
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            PrecipInput("NWS",             popPct: 40, amountIn: null, reportsPop: true,  precipVoteWeight: 1.75),
            PrecipInput("OpenMeteo-GFS",   popPct: 0,  amountIn: 0.0,  reportsPop: true),
            PrecipInput("OpenMeteo-ECMWF", popPct: 0,  amountIn: 0.0,  reportsPop: false),
            PrecipInput("OpenMeteo-ICON",  popPct: 0,  amountIn: 0.0,  reportsPop: false),
            PrecipInput("OpenMeteo-HRRR",  popPct: 0,  amountIn: 0.0,  reportsPop: false, weight: 1.2),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 5);
        Assert.Equal(12, result.Blended!.PrecipitationProbabilityPct); // 0.40*1.75/5.95 -> 11.76
        Assert.Null(PrecipitationFactor.Cap(result.Blended.PrecipitationProbabilityPct).Cap);
    }

    [Fact]
    public void Single_available_source_is_used_at_face_value()
    {
        // No corroboration possible -> do not discount our only data.
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            PrecipInput("NWS", popPct: 40, amountIn: null, reportsPop: true, precipVoteWeight: 1.75),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 5);
        Assert.Equal(40, result.Blended!.PrecipitationProbabilityPct);
    }

    [Fact]
    public void Mild_day_grades_clear_no_precip_cap()
    {
        // NWS 10%, GFS 5%, every model dry -> low consensus, no cap.
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            PrecipInput("NWS",             popPct: 10, amountIn: null, reportsPop: true,  precipVoteWeight: 1.75),
            PrecipInput("OpenMeteo-GFS",   popPct: 5,  amountIn: 0.0,  reportsPop: true),
            PrecipInput("OpenMeteo-ECMWF", popPct: 0,  amountIn: 0.0,  reportsPop: false),
            PrecipInput("OpenMeteo-ICON",  popPct: 0,  amountIn: 0.0,  reportsPop: false),
            PrecipInput("OpenMeteo-HRRR",  popPct: 0,  amountIn: 0.0,  reportsPop: false, weight: 1.2),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 5);
        Assert.Equal(4, result.Blended!.PrecipitationProbabilityPct); // 0.10*1.75 + 0.05 = 0.225 -> 0.225*100/5.95
        Assert.Null(PrecipitationFactor.Cap(result.Blended.PrecipitationProbabilityPct).Cap);
    }

    [Fact]
    public void Nws_vote_weight_amplifies_lone_nws_signal()
    {
        // NWS 50%, all models dry. Heavier NWS vote raises the consensus.
        var calc = new ConsensusCalculator();
        ConsensusInput[] Build(double nwsPrecipWeight) => new[]
        {
            PrecipInput("NWS",             popPct: 50, amountIn: null, reportsPop: true,  precipVoteWeight: nwsPrecipWeight),
            PrecipInput("OpenMeteo-GFS",   popPct: 0,  amountIn: 0.0,  reportsPop: true),
            PrecipInput("OpenMeteo-ECMWF", popPct: 0,  amountIn: 0.0,  reportsPop: false),
            PrecipInput("OpenMeteo-ICON",  popPct: 0,  amountIn: 0.0,  reportsPop: false),
            PrecipInput("OpenMeteo-HRRR",  popPct: 0,  amountIn: 0.0,  reportsPop: false, weight: 1.2),
        };
        var atOne = calc.Compute(Build(1.0), 5).Blended!.PrecipitationProbabilityPct;
        var atSeventyFive = calc.Compute(Build(1.75), 5).Blended!.PrecipitationProbabilityPct;
        Assert.Equal(10, atOne);          // 0.5*1.0/5.2  -> 9.6
        Assert.Equal(15, atSeventyFive);  // 0.5*1.75/5.95 -> 14.7
    }

    [Fact]
    public void Nws_vote_weight_damps_when_nws_disagrees_with_wet_models()
    {
        // NWS dry, four models wet. Heavier NWS vote pulls the consensus DOWN.
        var calc = new ConsensusCalculator();
        ConsensusInput[] Build(double nwsPrecipWeight) => new[]
        {
            PrecipInput("NWS",             popPct: 0,   amountIn: null, reportsPop: true,  precipVoteWeight: nwsPrecipWeight),
            PrecipInput("OpenMeteo-GFS",   popPct: 100, amountIn: 0.1,  reportsPop: true),
            PrecipInput("OpenMeteo-ECMWF", popPct: 0,   amountIn: 0.1,  reportsPop: false),
            PrecipInput("OpenMeteo-ICON",  popPct: 0,   amountIn: 0.1,  reportsPop: false),
            PrecipInput("OpenMeteo-HRRR",  popPct: 0,   amountIn: 0.1,  reportsPop: false, weight: 1.2),
        };
        var atOne = calc.Compute(Build(1.0), 5).Blended!.PrecipitationProbabilityPct;
        var atSeventyFive = calc.Compute(Build(1.75), 5).Blended!.PrecipitationProbabilityPct;
        Assert.True(atSeventyFive < atOne, $"expected heavier NWS to lower consensus: {atSeventyFive} vs {atOne}");
    }
```

- [ ] **Step 8: Run the full Core test project**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj`
Expected: PASS, including the pre-existing empty-hourly facts `Single_source_reports_high_consensus_and_passes_through_values` (PoP 20) and `Sources_with_partial_active_factors_excluded_from_those_factors` (blended PoP 28) — both hit the empty-`blendedHours` fallback, so they still pass unchanged.

- [ ] **Step 9: Commit**

```bash
git add backend/RouteWeather.Core/Grading/ConsensusCalculator.cs backend/RouteWeather.Core.Tests/Grading/ConsensusCalculatorTests.cs
git commit -m "feat(grading): per-hour confidence-weighted precip consensus

Blend per-hour precip from a weighted vote across all five sources (PoP
sources via probability, amount-only models via a QPF ramp) so an
uncorroborated single-model spike no longer caps the grade. Derive the
headline PoP from the blended hourly series.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Wire the NWS precip vote weight through config

**Files:**
- Modify: `backend/RouteWeather.API/Options/ForecastSourcesOptions.cs`
- Modify: `backend/RouteWeather.API/Services/ConditionsAggregator.cs:212-216`
- Modify: `backend/RouteWeather.API/appsettings.json:15`
- Test: `backend/RouteWeather.API.Tests/Options/ForecastSourcesOptionsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/RouteWeather.API.Tests/Options/ForecastSourcesOptionsTests.cs`:

```csharp
using RouteWeather.API.Options;
using Xunit;

namespace RouteWeather.API.Tests;

public class ForecastSourcesOptionsTests
{
    [Fact]
    public void PrecipVoteWeightFor_usesConfiguredValue_whenSet()
    {
        var opts = new ForecastSourcesOptions
        {
            Sources = { new SourceOptions { Name = "NWS", Weight = 1.0, PrecipVoteWeight = 1.75 } },
        };
        Assert.Equal(1.75, opts.PrecipVoteWeightFor("NWS"));
    }

    [Fact]
    public void PrecipVoteWeightFor_fallsBackToWeight_whenUnset()
    {
        var opts = new ForecastSourcesOptions
        {
            Sources = { new SourceOptions { Name = "OpenMeteo-HRRR", Weight = 1.2 } },
        };
        Assert.Equal(1.2, opts.PrecipVoteWeightFor("OpenMeteo-HRRR"));
    }

    [Fact]
    public void PrecipVoteWeightFor_defaultsToOne_whenSourceMissing()
    {
        var opts = new ForecastSourcesOptions();
        Assert.Equal(1.0, opts.PrecipVoteWeightFor("Nonexistent"));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter "FullyQualifiedName~ForecastSourcesOptionsTests"`
Expected: FAIL to compile — `SourceOptions.PrecipVoteWeight` and `ForecastSourcesOptions.PrecipVoteWeightFor` do not exist.

- [ ] **Step 3: Implement the options accessor**

In `backend/RouteWeather.API/Options/ForecastSourcesOptions.cs`, add a method to `ForecastSourcesOptions` (next to `WeightFor`):

```csharp
    public double PrecipVoteWeightFor(string name) => For(name)?.PrecipVoteWeight ?? WeightFor(name);
```

and add a property to `SourceOptions` (next to `Weight`):

```csharp
    public double? PrecipVoteWeight { get; set; }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj --filter "FullyQualifiedName~ForecastSourcesOptionsTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Pass the weight into the consensus inputs**

In `backend/RouteWeather.API/Services/ConditionsAggregator.cs`, update the `consensusInputs` projection (currently lines 212-216) to supply the precip vote weight:

```csharp
        var consensusInputs = liveForecasts
            .Select(r => new ConsensusInput(
                new SourceSnapshot(r.SourceName, r.Snapshot!, r.FetchedAt ?? DateTimeOffset.UtcNow, r.ActiveFactors),
                _options.WeightFor(r.SourceName),
                _options.PrecipVoteWeightFor(r.SourceName)))
            .ToList();
```

- [ ] **Step 6: Set the NWS weight in config**

In `backend/RouteWeather.API/appsettings.json`, change the NWS source line (line 15) from:

```json
      { "Name": "NWS",             "Enabled": true, "Weight": 1.0, "CacheTtlMinutes": 60 },
```

to:

```json
      { "Name": "NWS",             "Enabled": true, "Weight": 1.0, "PrecipVoteWeight": 1.75, "CacheTtlMinutes": 60 },
```

- [ ] **Step 7: Run the full API test project**

Run: `dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj`
Expected: PASS (new options tests green; `ConditionsAggregatorTests` and the rest unchanged — they assert grade/staleness/caching, not precip values).

- [ ] **Step 8: Commit**

```bash
git add backend/RouteWeather.API/Options/ForecastSourcesOptions.cs backend/RouteWeather.API/Services/ConditionsAggregator.cs backend/RouteWeather.API/appsettings.json backend/RouteWeather.API.Tests/Options/ForecastSourcesOptionsTests.cs
git commit -m "feat(grading): configurable NWS precip vote weight (1.75)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Final verification

**Files:** none (verification only)

- [ ] **Step 1: Run both test projects**

Run:
```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```
Expected: both PASS with no skips. If a build file-lock error appears, the dev API is running — that is expected; the csproj-path commands above build into the test projects' own output and should still succeed. Do not stop the servers.

- [ ] **Step 2: Confirm the spec's worked example by inspection**

Confirm `Lone_model_precip_spike_is_discounted_toward_consensus` asserts `14` and `Cap == null` — this is the spec's headline example (GFS 55 / NWS 15 → 14%, no cap) and the test that would have caught the original bug.

- [ ] **Step 3: Mark the spec shipped (optional)**

If desired, note in `docs/superpowers/specs/2026-06-15-precip-consensus-design.md` that the backend signal is implemented and the UI low-confidence indicator remains the open follow-on. Commit any such doc edit separately.

---

## Self-Review Notes (for the implementer)

- **Type consistency:** `PrecipVote.For` returns `double?` (null = abstain) and is consumed by `HourlyPrecipConsensus`, which skips nulls. `EffectivePrecipWeight` is the single weight source for precip voting. `PrecipVoteWeightFor` mirrors `WeightFor`.
- **Why existing tests stay green:** the two pre-existing facts that assert blended PoP use empty `Next48Hours`, so they exercise the `blendedHours.Count == 0` fallback (old weighted mean). All hourly-bearing window/grade tests build `WeatherSnapshot` directly and never go through `ConsensusCalculator`.
- **Out of scope (do not implement here):** the muted low-confidence UI indicator; any change to `PrecipitationFactor` thresholds, the amount blend, or upstream fetching.
