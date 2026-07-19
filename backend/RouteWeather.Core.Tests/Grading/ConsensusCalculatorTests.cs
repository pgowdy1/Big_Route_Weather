using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class ConsensusCalculatorTests
{
    private static WeatherSnapshot Snapshot(double wind, double temp, int precip) => new(
        WindMph: wind,
        TempF: temp,
        PrecipitationProbabilityPct: precip,
        Hourly: Array.Empty<HourlyForecast>());

    private static ConsensusInput Input(string name, double wind, double temp, int precip, double weight = 1.0, IReadOnlySet<string>? active = null) =>
        new(new SourceSnapshot(name, Snapshot(wind, temp, precip), DateTimeOffset.UtcNow, active ?? ForecastFactors.All), weight);

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
    public void Empty_input_returns_null_ensemble()
    {
        var calc = new ConsensusCalculator();
        var result = calc.Compute(Array.Empty<ConsensusInput>(), sourcesAttempted: 5);
        Assert.Null(result.Blended);
        Assert.Null(result.Consensus);
    }

    [Fact]
    public void Single_source_reports_high_consensus_and_passes_through_values()
    {
        var calc = new ConsensusCalculator();
        var result = calc.Compute(new[] { Input("NWS", 10, 30, 20) }, sourcesAttempted: 5);
        Assert.NotNull(result.Blended);
        Assert.Equal(10, result.Blended!.WindMph);
        Assert.Equal(30, result.Blended.TempF);
        Assert.Equal(20, result.Blended.PrecipitationProbabilityPct);
        Assert.NotNull(result.Consensus);
        Assert.Equal(ConsensusLevel.High, result.Consensus!.Level);
        Assert.Equal(1, result.Consensus.SourcesReporting);
        Assert.Equal(5, result.Consensus.SourcesAttempted);
        Assert.Null(result.Consensus.WorstFactor);
    }

    [Fact]
    public void Identical_sources_yield_high_consensus()
    {
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            Input("A", 10, 30, 20),
            Input("B", 10, 30, 20),
            Input("C", 10, 30, 20),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 3);
        Assert.Equal(ConsensusLevel.High, result.Consensus!.Level);
    }

    [Fact]
    public void One_factor_wide_spread_does_not_tank_overall_consensus()
    {
        // Mean-CV resolution: temp + precip agree perfectly, so a wide wind spread alone
        // averages out to "still mostly aligned" and the overall level stays High.
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            Input("A", 5,  30, 20),
            Input("B", 25, 30, 20),
            Input("C", 45, 30, 20),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 3);
        Assert.Equal(ConsensusLevel.High, result.Consensus!.Level);
    }

    [Fact]
    public void All_factors_diverging_yields_low_consensus()
    {
        // Wide spread across every factor pushes the mean CV above the medium cap.
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            Input("A", 5,  10, 5),
            Input("B", 25, 35, 50),
            Input("C", 50, 60, 95),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 3);
        Assert.Equal(ConsensusLevel.Low, result.Consensus!.Level);
        Assert.NotNull(result.Consensus.WorstFactor);
    }

    [Fact]
    public void Small_absolute_spread_below_floor_counts_as_agreement()
    {
        // Wind 8 vs 10 vs 12: CV alone would say ~17%, but the 4 mph absolute spread
        // is below the 5 mph floor — call it agreement.
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            Input("A", 8,  30, 20),
            Input("B", 10, 30, 20),
            Input("C", 12, 30, 20),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 3);
        Assert.Equal(0, result.Consensus!.CoefficientOfVariationByFactor["Wind"]);
        Assert.Equal(ConsensusLevel.High, result.Consensus.Level);
    }

    [Fact]
    public void Weighted_blend_shifts_toward_heavier_source()
    {
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            Input("A", 10, 30, 20, weight: 1.0),
            Input("B", 30, 30, 20, weight: 3.0),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 2);
        Assert.Equal(25, result.Blended!.WindMph);
    }

    [Fact]
    public void All_zero_precip_does_not_produce_nan()
    {
        var calc = new ConsensusCalculator();
        var inputs = new[]
        {
            Input("A", 10, 30, 0),
            Input("B", 10, 30, 0),
            Input("C", 10, 30, 0),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 3);
        Assert.False(double.IsNaN(result.Consensus!.CoefficientOfVariationByFactor["Precipitation"]));
        Assert.Equal(ConsensusLevel.High, result.Consensus.Level);
    }

    [Fact]
    public void Sources_with_partial_active_factors_excluded_from_those_factors()
    {
        var calc = new ConsensusCalculator();
        // NWS + GFS contribute precip; ECMWF/ICON/HRRR contribute only wind+temp.
        var inputs = new[]
        {
            Input("NWS",   10, 30, 25),
            Input("GFS",   10, 30, 31),
            Input("ECMWF", 10, 30, 0, active: ForecastFactors.WindAndTemperatureOnly),
            Input("ICON",  10, 30, 0, active: ForecastFactors.WindAndTemperatureOnly),
            Input("HRRR",  10, 30, 0, active: ForecastFactors.WindAndTemperatureOnly),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 5);

        // Blended precip = mean of NWS (25) and GFS (31) only.
        Assert.Equal(28, result.Blended!.PrecipitationProbabilityPct);
        // Precip CV computed from only 2 active sources, not skewed by the zeros.
        var precipCv = result.Consensus!.CoefficientOfVariationByFactor[ForecastFactors.Precipitation];
        Assert.True(precipCv < 0.2, $"precip CV should reflect tight 25 vs 31 agreement, got {precipCv}");
    }

    [Fact]
    public void Reports_sources_reporting_vs_attempted_when_some_dropped()
    {
        var calc = new ConsensusCalculator();
        var inputs = new[] { Input("A", 10, 30, 20), Input("B", 12, 32, 25) };
        var result = calc.Compute(inputs, sourcesAttempted: 5);
        Assert.Equal(2, result.Consensus!.SourcesReporting);
        Assert.Equal(5, result.Consensus.SourcesAttempted);
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

    [Fact]
    public void Cv_belowCapeSpreadFloor_countsAsAgreement()
    {
        var calc = new ConsensusCalculator();
        var result = calc.Compute(new[]
        {
            InputWith("A", 10, 50, 0, cape: 400),
            InputWith("B", 10, 50, 0, cape: 550), // spread 150 < 200 J/kg floor
        }, 2);
        Assert.Equal(0, result.Consensus!.CoefficientOfVariationByFactor[ForecastFactors.Cape]);
    }

    [Fact]
    public void EffectivePrecipWeight_overridesWeight_whenSet_elseFallsBack()
    {
        var src = new SourceSnapshot("NWS", Snapshot(10, 30, 20), DateTimeOffset.UtcNow, ForecastFactors.All);

        var withOverride = new ConsensusInput(src, Weight: 1.0, PrecipVoteWeight: 1.75);
        var withoutOverride = new ConsensusInput(src, Weight: 1.2);

        Assert.Equal(1.75, withOverride.EffectivePrecipWeight);
        Assert.Equal(1.2, withoutOverride.EffectivePrecipWeight);
    }

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
            PrecipitationProbabilityPct: popPct, Hourly: new[] { hour }, PrecipAmountIn: amountIn);
        return new ConsensusInput(new SourceSnapshot(name, snap, DateTimeOffset.UtcNow, active),
            weight, precipVoteWeight);
    }

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
        Assert.Equal(14, result.Blended!.PrecipitationProbabilityPct); // 0.15*1.75 + 0.55*1.0 = 0.8125; /5.95*100 -> 13.66
        Assert.Null(PrecipitationFactor.Cap(result.Blended.PrecipitationProbabilityPct).Cap);
    }

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
        Assert.Equal(77, result.Blended!.PrecipitationProbabilityPct); // 0.875+0.5+1.0+1.0+1.2 = 4.575; /5.95*100 -> 76.9
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

    private static ConsensusInput ConstantSeriesInput(string name, double wind, double temp, int hours, double weight)
    {
        var t0 = DateTimeOffset.Parse("2026-07-20T06:00:00Z");
        var series = new List<HourlyForecast>(hours);
        for (var i = 0; i < hours; i++)
            series.Add(new HourlyForecast(t0.AddHours(i), temp, wind, 0, "Clear"));
        var snap = new WeatherSnapshot(wind, temp, 0, series);
        return new ConsensusInput(
            new SourceSnapshot(name, snap, DateTimeOffset.UtcNow, ForecastFactors.All), weight);
    }

    [Fact]
    public void Blend_extends_to_longest_series_when_high_weight_source_is_short()
    {
        // A: heavy weight but short horizon (48h); B: light weight, deep horizon (168h).
        // The blend axis must follow the DEEPEST series, so the blended series reaches
        // 168h; past hour 48 only B still votes. Weight governs voting, never reach.
        var calc = new ConsensusCalculator();
        var result = calc.Compute(new[]
        {
            ConstantSeriesInput("A", wind: 10, temp: 40, hours: 48,  weight: 2.0),
            ConstantSeriesInput("B", wind: 10, temp: 60, hours: 168, weight: 1.0),
        }, sourcesAttempted: 2);

        Assert.NotNull(result.Blended);
        Assert.Equal(168, result.Blended!.Hourly.Count);
        Assert.Equal(47, result.Blended.Hourly[10].TempF);   // (40*2 + 60*1)/3 = 46.67 -> 47, both vote
        Assert.Equal(60, result.Blended.Hourly[100].TempF);  // past A's horizon, B alone votes
        Assert.Equal(47, result.Blended.TempF);              // headline scalar still the first-48h weighted mean
    }
}
