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
        Hourly: Array.Empty<HourlyForecast>());

    private static WeatherSnapshot Terrible() => new(
        WindMph: 80,
        TempF: -20,
        PrecipitationProbabilityPct: 100,
        Hourly: Array.Empty<HourlyForecast>());

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
        var bad = new WeatherSnapshot(WindMph: 60, TempF: 40, PrecipitationProbabilityPct: 0, Hourly: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(bad, PerfectSnowpack());
        Assert.Equal("negative", result.Drivers[0].Severity);
    }

    private static SnowpackSnapshot WinterSnowpackWithTraceRecent() =>
        PerfectSnowpack() with { NewSnowLast7DaysIn = 0.1 };

    [Fact]
    public void Wind_above_20_holds_grade_at_B_even_when_otherwise_perfect()
    {
        // Under rebalanced weights wind 21 (score 63, wt 0.20) drags an otherwise
        // perfect winter profile to a natural 89 -> B, so the >20 B-cap is redundant
        // (cap only binds when natural is strictly better) and no "Capped" prefix shows.
        var windy = new WeatherSnapshot(WindMph: 21, TempF: 45, PrecipitationProbabilityPct: 0, Hourly: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(windy, WinterSnowpackWithTraceRecent());
        Assert.Equal(Grade.B, result.Grade);
        Assert.Equal(89, result.OverallScore);
        Assert.DoesNotContain("Capped", result.Rationale);
        Assert.Contains("21 mph", result.Rationale);
    }

    [Fact]
    public void Precip_above_30_holds_grade_at_B_even_when_otherwise_perfect()
    {
        // 35% precip (score 56, wt 0.18) yields a natural 88 -> B; the >30 B-cap is
        // redundant with the natural grade, so no "Capped" prefix shows.
        var wet = new WeatherSnapshot(WindMph: 5, TempF: 45, PrecipitationProbabilityPct: 35, Hourly: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(wet, WinterSnowpackWithTraceRecent());
        Assert.Equal(Grade.B, result.Grade);
        Assert.Equal(88, result.OverallScore);
        Assert.DoesNotContain("Capped", result.Rationale);
    }

    [Fact]
    public void Worst_cap_aligns_with_natural_grade_when_multiple_factors_trigger()
    {
        // Wind 22 (B-cap) + precip 80 (score 0, D-cap). Natural weighted mean is 63 -> D,
        // which already matches the worst (D) cap, so the cap is redundant and the
        // rationale is the natural-D form. Precipitation is the worst factor.
        var bad = new WeatherSnapshot(WindMph: 22, TempF: 45, PrecipitationProbabilityPct: 80, Hourly: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(bad, WinterSnowpackWithTraceRecent());
        Assert.Equal(Grade.D, result.Grade);
        Assert.Equal(63, result.OverallScore);
        Assert.DoesNotContain("Capped", result.Rationale);
        Assert.Contains("Precipitation", result.Rationale);
    }

    [Fact]
    public void Cap_does_not_raise_grade_above_natural()
    {
        var bad = new WeatherSnapshot(WindMph: 22, TempF: 100, PrecipitationProbabilityPct: 100, Hourly: Array.Empty<HourlyForecast>());
        var result = GradeCalculator.Compute(bad, TerribleSnowpack());
        Assert.Equal(Grade.F, result.Grade);
    }

    [Fact]
    public void Cold_temp_extreme_triggers_temperature_cap()
    {
        var freezing = new WeatherSnapshot(WindMph: 5, TempF: -5, PrecipitationProbabilityPct: 0, Hourly: Array.Empty<HourlyForecast>());
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
        var windy = new WeatherSnapshot(WindMph: 25, TempF: 45, PrecipitationProbabilityPct: 0, Hourly: Array.Empty<HourlyForecast>());
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
            Hourly: new[] { new HourlyForecast(DateTimeOffset.UtcNow, 60, 5, 0, "Sunny") });
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
            Hourly: new[] { new HourlyForecast(DateTimeOffset.UtcNow, 60, 5, 0, "Sunny") });

        var weatherOnly = GradeCalculator.Compute(summer, snowpack: null);
        var withDryPack = GradeCalculator.Compute(summer, DryRouteSnowpack());

        Assert.Equal(weatherOnly.OverallScore, withDryPack.OverallScore);
        Assert.Equal(weatherOnly.Grade, withDryPack.Grade);
    }

    [Fact]
    public void Lingering_depth_without_new_snow_keeps_snowpack_active_recent_inactive()
    {
        var summer = new WeatherSnapshot(WindMph: 5, TempF: 60, PrecipitationProbabilityPct: 0,
            Hourly: new[] { new HourlyForecast(DateTimeOffset.UtcNow, 60, 5, 0, "Sunny") });
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
            Hourly: new[] { new HourlyForecast(DateTimeOffset.UtcNow, 30, 5, 60, "Snow showers") });
        var result = GradeCalculator.Compute(snowy, DryRouteSnowpack());

        var recent = result.Factors.Single(f => f.Name == "Recent snow");
        var pack = result.Factors.Single(f => f.Name == "Snowpack");
        Assert.True(recent.IsActive);
        Assert.False(pack.IsActive);
    }

    [Fact]
    public void Existing_snowpack_keeps_both_active()
    {
        var weather = new WeatherSnapshot(WindMph: 5, TempF: 40, PrecipitationProbabilityPct: 0, Hourly: Array.Empty<HourlyForecast>());
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

    private static WeatherSnapshot Weather(
        double wind = 5, double temp = 50, int precip = 0,
        double? gust = null, double? cape = null, double? amountIn = null)
    {
        var hours = Enumerable.Range(0, 24)
            .Select(i => new HourlyForecast(
                DateTimeOffset.UtcNow.AddHours(i), temp, wind, precip, "Sunny"))
            .ToList();
        return new WeatherSnapshot(wind, temp, precip, hours,
            MaxGustMph: gust, MaxCapeJkg: cape, PrecipAmountIn: amountIn);
    }

    [Fact]
    public void NoCapeData_addsNoThunderstormFactor()
    {
        var result = GradeCalculator.Compute(Weather(), null);
        Assert.DoesNotContain(result.Factors, f => f.Name == "Thunderstorm");
    }

    [Fact]
    public void LowCape_thunderstormFactorPresentButInactive()
    {
        var result = GradeCalculator.Compute(Weather(cape: 100), null);
        var f = Assert.Single(result.Factors, x => x.Name == "Thunderstorm");
        Assert.False(f.IsActive);
    }

    [Fact]
    public void HighCape_capsGradeAndLeadsDrivers()
    {
        var result = GradeCalculator.Compute(Weather(cape: 2200), null);
        Assert.Equal(Grade.D, result.Grade);
        Assert.Equal("Storm risk", result.Drivers[0].Label);
        Assert.Equal("negative", result.Drivers[0].Severity);
    }

    [Fact]
    public void SmallGust_gustFactorPresentButInactive()
    {
        var result = GradeCalculator.Compute(Weather(gust: 15), null);
        var f = Assert.Single(result.Factors, x => x.Name == "Gusts");
        Assert.False(f.IsActive);
    }

    [Fact]
    public void StrongGust_activeAndCaps()
    {
        var result = GradeCalculator.Compute(Weather(gust: 60), null);
        var f = Assert.Single(result.Factors, x => x.Name == "Gusts");
        Assert.True(f.IsActive);
        Assert.Equal(Grade.D, result.Grade);
    }

    [Fact]
    public void PrecipAmount_dragsPrecipitationScore()
    {
        // 20% prob alone -> 75; with 1.0" in a 24h window -> amountScore 0 -> min 0.
        var result = GradeCalculator.Compute(Weather(precip: 20, amountIn: 1.0), null);
        var f = Assert.Single(result.Factors, x => x.Name == "Precipitation");
        Assert.Equal(0, f.Score);
    }

    [Fact]
    public void Weights_matchRebalancedValues()
    {
        Assert.Equal(0.20, WindFactor.Weight);
        Assert.Equal(0.12, TemperatureFactor.Weight);
        Assert.Equal(0.18, PrecipitationFactor.Weight);
        Assert.Equal(0.20, ThunderstormFactor.Weight);
        Assert.Equal(0.10, GustFactor.Weight);
        Assert.Equal(0.10, RecentSnowFactor.Weight);
        Assert.Equal(0.10, SnowpackFactor.Weight);
    }

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

    [Fact]
    public void CappingAqi_inNeutralScoreBand_doesNotDoubleListDriver()
    {
        // AQI 160 scores 56 (neutral band) yet caps at C; the capped factor must
        // appear exactly once as a negative driver, not also as a neutral one.
        var result = GradeCalculator.Compute(Weather(), null, new AirQualitySnapshot(160, 60));
        Assert.Equal(Grade.C, result.Grade);
        Assert.Single(result.Drivers, d => d.Label == "Poor air quality");
        Assert.DoesNotContain(result.Drivers, d => d.Label == "Reduced air quality");
    }
}
