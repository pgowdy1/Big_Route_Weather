using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class WindowGradeCalculatorTests
{
    private static SnowpackSnapshot CleanSnowpack() => new(
        SnowWaterEquivalentIn: 5,
        SnowDepthIn: 20,
        NewSnowLast7DaysIn: 0,
        PercentOfNormalSwe: 100,
        StationTriplet: "TEST:CO:SNTL",
        DailyDepthIn: Array.Empty<DailyDepthPoint>());

    private static HourlyForecast Hour(int hour, double tempF, double windMph, int precipPct) =>
        new(
            Time: DateTimeOffset.UnixEpoch.AddHours(hour),
            TempF: tempF,
            WindMph: windMph,
            PrecipitationProbabilityPct: precipPct,
            ShortForecast: "Sunny");

    private static WeatherSnapshot Snapshot(IReadOnlyList<HourlyForecast> hourly) =>
        new(
            WindMph: hourly.Max(h => h.WindMph),
            TempF: hourly.Min(h => h.TempF),
            PrecipitationProbabilityPct: hourly.Max(h => h.PrecipitationProbabilityPct),
            Next48Hours: hourly);

    [Fact]
    public void Late_window_precip_spike_does_not_affect_12h_grade()
    {
        var hourly = Enumerable.Range(0, 48)
            .Select(i => Hour(i, tempF: 40, windMph: 5, precipPct: i >= 24 ? 90 : 0))
            .ToList();
        var weather = Snapshot(hourly);

        var grades = WindowGradeCalculator.Compute(weather, CleanSnowpack());

        Assert.Equal(Grade.A, grades.Next12h.Grade);
        Assert.True(grades.Next48h.OverallScore < grades.Next12h.OverallScore);
        Assert.Equal(12, grades.Next12h.HoursCovered);
        Assert.Equal(24, grades.Next24h.HoursCovered);
        Assert.Equal(48, grades.Next48h.HoursCovered);
    }

    [Fact]
    public void Uniform_forecast_produces_equal_window_grades()
    {
        var hourly = Enumerable.Range(0, 48)
            .Select(i => Hour(i, tempF: 40, windMph: 5, precipPct: 0))
            .ToList();
        var weather = Snapshot(hourly);

        var grades = WindowGradeCalculator.Compute(weather, CleanSnowpack());

        Assert.Equal(grades.Next12h.Grade, grades.Next24h.Grade);
        Assert.Equal(grades.Next24h.Grade, grades.Next48h.Grade);
        Assert.Equal(grades.Next12h.OverallScore, grades.Next48h.OverallScore);
    }

    [Fact]
    public void Partial_forecast_reports_actual_hours_covered()
    {
        var hourly = Enumerable.Range(0, 18)
            .Select(i => Hour(i, tempF: 40, windMph: 5, precipPct: 0))
            .ToList();
        var weather = Snapshot(hourly);

        var grades = WindowGradeCalculator.Compute(weather, CleanSnowpack());

        Assert.Equal(12, grades.Next12h.HoursCovered);
        Assert.Equal(18, grades.Next24h.HoursCovered);
        Assert.Equal(18, grades.Next48h.HoursCovered);
        Assert.NotNull(grades.Next48h.Grade);
    }

    [Fact]
    public void Empty_forecast_with_snowpack_grades_from_snowpack_only()
    {
        var weather = new WeatherSnapshot(
            WindMph: 0,
            TempF: 0,
            PrecipitationProbabilityPct: 0,
            Next48Hours: Array.Empty<HourlyForecast>());

        var grades = WindowGradeCalculator.Compute(weather, CleanSnowpack());

        Assert.NotNull(grades.Next12h.Grade);
        Assert.Equal(0, grades.Next12h.HoursCovered);
        Assert.Equal(0, grades.Next48h.HoursCovered);
    }

    [Fact]
    public void Null_weather_and_null_snowpack_returns_no_grade()
    {
        var grades = WindowGradeCalculator.Compute(null, null);

        Assert.Null(grades.Next12h.Grade);
        Assert.Null(grades.Next24h.Grade);
        Assert.Null(grades.Next48h.Grade);
        Assert.Equal(0, grades.Next12h.HoursCovered);
    }

    [Fact]
    public void Aggregate_computesNewHeadlines_fromSlice()
    {
        var hours = new List<HourlyForecast>();
        for (var i = 0; i < 12; i++)
        {
            hours.Add(new HourlyForecast(
                DateTimeOffset.UnixEpoch.AddHours(i), 50, 5, 0, "Sunny",
                GustMph: 10 + i,          // max 21
                CapeJkg: 100 * i,         // max 1100 in slice
                PrecipitationIn: 0.1));   // sum 1.2
        }
        var weather = new WeatherSnapshot(5, 50, 0, hours);

        var grades = WindowGradeCalculator.Compute(weather, null);
        var factors12 = grades.Next12h.Factors;

        Assert.Contains(factors12, f => f.Name == "Thunderstorm" && f.IsActive);
        Assert.Contains(factors12, f => f.Name == "Gusts" && !f.IsActive); // 21 < 25
        var precip = Assert.Single(factors12, f => f.Name == "Precipitation");
        Assert.Equal(0, precip.Score); // 1.2" >= 0.5" bad threshold for 12h
    }

    [Fact]
    public void Aggregate_allNullNewFields_addsNoNewFactors()
    {
        var hours = Enumerable.Range(0, 12)
            .Select(i => new HourlyForecast(DateTimeOffset.UnixEpoch.AddHours(i), 50, 5, 0, "Sunny"))
            .ToList();
        var weather = new WeatherSnapshot(5, 50, 0, hours);

        var grades = WindowGradeCalculator.Compute(weather, null);

        Assert.DoesNotContain(grades.Next12h.Factors, f => f.Name == "Thunderstorm");
        Assert.DoesNotContain(grades.Next12h.Factors, f => f.Name == "Gusts");
    }
}
