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

    private static SnowpackSnapshot WinterSnowpackWithTraceRecent() =>
        PerfectSnowpack() with { NewSnowLast7DaysIn = 0.1 };

    [Fact]
    public void Wind_above_20_caps_grade_at_B_even_when_otherwise_perfect()
    {
        var windy = new WeatherSnapshot(WindMph: 21, TempF: 45, PrecipitationProbabilityPct: 0, Next48Hours: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(windy, WinterSnowpackWithTraceRecent());
        Assert.Equal(Grade.B, result.Grade);
        Assert.Contains("Capped at B", result.Rationale);
        Assert.Contains("21 mph", result.Rationale);
    }

    [Fact]
    public void Precip_above_30_caps_grade_at_B_even_when_otherwise_perfect()
    {
        var wet = new WeatherSnapshot(WindMph: 5, TempF: 45, PrecipitationProbabilityPct: 35, Next48Hours: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(wet, WinterSnowpackWithTraceRecent());
        Assert.Equal(Grade.B, result.Grade);
        Assert.Contains("Capped at B", result.Rationale);
    }

    [Fact]
    public void Worst_cap_wins_when_multiple_factors_trigger()
    {
        var bad = new WeatherSnapshot(WindMph: 22, TempF: 45, PrecipitationProbabilityPct: 80, Next48Hours: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(bad, WinterSnowpackWithTraceRecent());
        Assert.Equal(Grade.D, result.Grade);
        Assert.Contains("Capped at D", result.Rationale);
    }

    [Fact]
    public void Cap_does_not_raise_grade_above_natural()
    {
        var bad = new WeatherSnapshot(WindMph: 22, TempF: 100, PrecipitationProbabilityPct: 100, Next48Hours: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(bad, TerribleSnowpack());
        Assert.Equal(Grade.F, result.Grade);
    }

    [Fact]
    public void Cold_temp_extreme_triggers_temperature_cap()
    {
        var freezing = new WeatherSnapshot(WindMph: 5, TempF: -5, PrecipitationProbabilityPct: 0, Next48Hours: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(freezing, PerfectSnowpack());
        Assert.True((int)result.Grade >= (int)Grade.C, $"Expected at least C cap from -5°F, got {result.Grade}");
        Assert.Contains("Capped at", result.Rationale);
    }

    [Fact]
    public void Fresh_snow_on_rock_caps_grade()
    {
        var perfectWeather = Perfect();
        var snowyRock = new SnowpackSnapshot(
            SnowWaterEquivalentIn: 5,
            SnowDepthIn: 20,
            NewSnowLast7DaysIn: 2.1,
            PercentOfNormalSwe: 100,
            StationTriplet: "TEST:CO:SNTL",
            DailyDepthIn: Array.Empty<DailyDepthPoint>());
        var result = GradeCalculator.Compute(perfectWeather, snowyRock);
        Assert.Equal(Grade.B, result.Grade);
        Assert.Contains("Capped at B", result.Rationale);
    }

    [Fact]
    public void Capped_factor_surfaces_as_first_driver()
    {
        var windy = new WeatherSnapshot(WindMph: 25, TempF: 45, PrecipitationProbabilityPct: 0, Next48Hours: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(windy, PerfectSnowpack());
        Assert.NotEmpty(result.Drivers);
        Assert.Equal("High winds", result.Drivers[0].Label);
        Assert.Equal("negative", result.Drivers[0].Severity);
    }

    private static SnowpackSnapshot DryRouteSnowpack(double newSnow = 0) => new(
        SnowWaterEquivalentIn: 0,
        SnowDepthIn: 0,
        NewSnowLast7DaysIn: newSnow,
        PercentOfNormalSwe: 100,
        StationTriplet: "TEST:CO:SNTL",
        DailyDepthIn: Array.Empty<DailyDepthPoint>());

    [Fact]
    public void Summer_dry_route_marks_snow_factors_inactive()
    {
        var summer = new WeatherSnapshot(WindMph: 5, TempF: 60, PrecipitationProbabilityPct: 0,
            Next48Hours: new[] { new HourlyForecast(DateTimeOffset.UtcNow, 60, 5, 0, "Sunny") });
        var result = GradeCalculator.Compute(summer, DryRouteSnowpack());

        var recent = result.Factors.Single(f => f.Name == "Recent snow");
        var pack = result.Factors.Single(f => f.Name == "Snowpack");
        Assert.False(recent.IsActive);
        Assert.False(pack.IsActive);
    }

    [Fact]
    public void Summer_dry_route_excludes_snow_factors_from_weight_sum()
    {
        var summer = new WeatherSnapshot(WindMph: 5, TempF: 60, PrecipitationProbabilityPct: 0,
            Next48Hours: new[] { new HourlyForecast(DateTimeOffset.UtcNow, 60, 5, 0, "Sunny") });

        var weatherOnly = GradeCalculator.Compute(summer, snowpack: null);
        var withDryPack = GradeCalculator.Compute(summer, DryRouteSnowpack());

        Assert.Equal(weatherOnly.OverallScore, withDryPack.OverallScore);
        Assert.Equal(weatherOnly.Grade, withDryPack.Grade);
    }

    [Fact]
    public void Lingering_depth_without_new_snow_keeps_snowpack_active_recent_inactive()
    {
        var summer = new WeatherSnapshot(WindMph: 5, TempF: 60, PrecipitationProbabilityPct: 0,
            Next48Hours: new[] { new HourlyForecast(DateTimeOffset.UtcNow, 60, 5, 0, "Sunny") });
        var lingering = new SnowpackSnapshot(
            SnowWaterEquivalentIn: 2,
            SnowDepthIn: 8,
            NewSnowLast7DaysIn: 0,
            PercentOfNormalSwe: 100,
            StationTriplet: "TEST:CO:SNTL",
            DailyDepthIn: Array.Empty<DailyDepthPoint>());

        var result = GradeCalculator.Compute(summer, lingering);

        var recent = result.Factors.Single(f => f.Name == "Recent snow");
        var pack = result.Factors.Single(f => f.Name == "Snowpack");
        Assert.False(recent.IsActive);
        Assert.True(pack.IsActive);
        Assert.DoesNotContain("Capped", result.Rationale);
    }

    [Fact]
    public void Forecast_snow_keeps_recent_active_but_snowpack_inactive()
    {
        var snowy = new WeatherSnapshot(WindMph: 5, TempF: 30, PrecipitationProbabilityPct: 60,
            Next48Hours: new[] { new HourlyForecast(DateTimeOffset.UtcNow, 30, 5, 60, "Snow showers") });
        var result = GradeCalculator.Compute(snowy, DryRouteSnowpack());

        var recent = result.Factors.Single(f => f.Name == "Recent snow");
        var pack = result.Factors.Single(f => f.Name == "Snowpack");
        Assert.True(recent.IsActive);
        Assert.False(pack.IsActive);
    }

    [Fact]
    public void Existing_snowpack_keeps_both_active()
    {
        var weather = new WeatherSnapshot(WindMph: 5, TempF: 40, PrecipitationProbabilityPct: 0, Next48Hours: Array.Empty<HourlyForecast>());
        var winter = new SnowpackSnapshot(
            SnowWaterEquivalentIn: 8,
            SnowDepthIn: 30,
            NewSnowLast7DaysIn: 1,
            PercentOfNormalSwe: 100,
            StationTriplet: "TEST:CO:SNTL",
            DailyDepthIn: Array.Empty<DailyDepthPoint>());
        var result = GradeCalculator.Compute(weather, winter);

        Assert.True(result.Factors.Single(f => f.Name == "Recent snow").IsActive);
        Assert.True(result.Factors.Single(f => f.Name == "Snowpack").IsActive);
    }
}
