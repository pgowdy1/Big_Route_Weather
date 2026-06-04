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
        Next48Hours: Array.Empty<HourlyForecast>());

    private static ConsensusInput Input(string name, double wind, double temp, int precip, double weight = 1.0, IReadOnlySet<string>? active = null) =>
        new(new SourceSnapshot(name, Snapshot(wind, temp, precip), DateTimeOffset.UtcNow, active ?? ForecastFactors.All), weight);

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
    public void Wide_wind_spread_yields_low_consensus_with_wind_as_worst_factor()
    {
        var calc = new ConsensusCalculator(highMaxCv: 0.15, mediumMaxCv: 0.35);
        var inputs = new[]
        {
            Input("A", 5,  30, 20),
            Input("B", 25, 30, 20),
            Input("C", 45, 30, 20),
        };
        var result = calc.Compute(inputs, sourcesAttempted: 3);
        Assert.Equal(ConsensusLevel.Low, result.Consensus!.Level);
        Assert.Equal("Wind", result.Consensus.WorstFactor);
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
}
