using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class GradeCalculatorTests
{
    private static WeatherSnapshot Perfect() => new(
        WindMph: 5,
        TempF: 45,
        PrecipitationProbabilityPct: 0,
        Next48Hours: Array.Empty<HourlyForecast>());

    private static WeatherSnapshot Terrible() => new(
        WindMph: 80,
        TempF: -20,
        PrecipitationProbabilityPct: 100,
        Next48Hours: Array.Empty<HourlyForecast>());

    private static SnowpackSnapshot PerfectSnowpack() => new(
        SnowWaterEquivalentIn: 5,
        SnowDepthIn: 20,
        NewSnowLast7DaysIn: 0,
        PercentOfNormalSwe: 100,
        StationTriplet: "TEST:CO:SNTL",
        DailyDepthIn: Array.Empty<DailyDepthPoint>());

    private static SnowpackSnapshot TerribleSnowpack() => new(
        SnowWaterEquivalentIn: 12,
        SnowDepthIn: 60,
        NewSnowLast7DaysIn: 12,
        PercentOfNormalSwe: 250,
        StationTriplet: "TEST:CO:SNTL",
        DailyDepthIn: Array.Empty<DailyDepthPoint>());

    [Fact]
    public void Returns_A_when_all_factors_perfect()
    {
        var result = GradeCalculator.Compute(Perfect(), PerfectSnowpack());
        Assert.Equal(Grade.A, result.Grade);
        Assert.Equal(100, result.OverallScore);
    }

    [Fact]
    public void Returns_F_when_all_factors_terrible()
    {
        var result = GradeCalculator.Compute(Terrible(), TerribleSnowpack());
        Assert.Equal(Grade.F, result.Grade);
    }

    [Fact]
    public void Returns_no_data_when_both_inputs_null()
    {
        var result = GradeCalculator.Compute(null, null);
        Assert.Equal(Grade.F, result.Grade);
        Assert.Equal(0, result.OverallScore);
        Assert.Empty(result.Factors);
    }

    [Fact]
    public void Redistributes_weight_when_snowpack_missing()
    {
        var result = GradeCalculator.Compute(Perfect(), null);
        Assert.Equal(Grade.A, result.Grade);
        Assert.Equal(3, result.Factors.Count);
    }

    [Fact]
    public void Drivers_lead_with_negatives_when_present()
    {
        var bad = new WeatherSnapshot(WindMph: 60, TempF: 40, PrecipitationProbabilityPct: 0, Next48Hours: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(bad, PerfectSnowpack());
        Assert.Equal("negative", result.Drivers[0].Severity);
    }
}
