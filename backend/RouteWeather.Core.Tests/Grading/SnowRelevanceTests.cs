using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class SnowRelevanceTests
{
    private static WeatherSnapshot WeatherWith(params HourlyForecast[] hours) =>
        new(WindMph: 5, TempF: 50, PrecipitationProbabilityPct: 0, Hourly: hours);

    private static HourlyForecast Hour(string shortForecast, double tempF = 60, int precip = 0) =>
        new(Time: DateTimeOffset.UtcNow, TempF: tempF, WindMph: 5, PrecipitationProbabilityPct: precip, ShortForecast: shortForecast);

    private static SnowpackSnapshot Snowpack(double depthIn, double newSnow = 0) => new(
        SnowWaterEquivalentIn: 5,
        SnowDepthIn: depthIn,
        NewSnowLast7DaysIn: newSnow,
        PercentOfNormalSwe: 100,
        StationTriplet: "TEST:CO:SNTL",
        DailyDepthIn: Array.Empty<DailyDepthPoint>());

    [Fact]
    public void Depth_with_recent_snow_keeps_both_active()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Sunny")), Snowpack(depthIn: 12, newSnow: 3));
        Assert.True(act.RecentSnowActive);
        Assert.True(act.SnowpackActive);
    }

    [Fact]
    public void Lingering_depth_without_recent_or_forecast_snow_keeps_snowpack_only()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Sunny")), Snowpack(depthIn: 12));
        Assert.False(act.RecentSnowActive);
        Assert.True(act.SnowpackActive);
    }

    [Fact]
    public void Depth_zero_and_clear_forecast_deactivates_both()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Sunny")), Snowpack(depthIn: 0));
        Assert.False(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Depth_zero_with_likely_snow_forecast_activates_recent_only()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Snow likely", precip: 60)), Snowpack(depthIn: 0));
        Assert.True(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Slight_chance_of_snow_does_not_trigger()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Slight Chance Snow Showers", precip: 17)), Snowpack(depthIn: 0));
        Assert.False(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Chance_of_snow_below_likely_threshold_does_not_trigger()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Chance Snow Showers", precip: 33)), Snowpack(depthIn: 0));
        Assert.False(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Cold_wet_hour_without_snow_wording_does_not_trigger()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Cloudy", tempF: 28, precip: 80)), Snowpack(depthIn: 0));
        Assert.False(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Cold_but_dry_does_not_signal_snow()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Clear", tempF: 20, precip: 10)), Snowpack(depthIn: 0));
        Assert.False(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Warm_and_wet_does_not_signal_snow()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Rain", tempF: 55, precip: 80)), Snowpack(depthIn: 0));
        Assert.False(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Null_snowpack_no_forecast_deactivates_both()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Sunny")), snowpack: null);
        Assert.False(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Null_snowpack_with_snow_forecast_activates_recent_only()
    {
        var act = SnowRelevance.Evaluate(WeatherWith(Hour("Heavy snow", precip: 80)), snowpack: null);
        Assert.True(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Null_weather_with_depth_and_recent_snow_activates_both()
    {
        var act = SnowRelevance.Evaluate(weather: null, Snowpack(depthIn: 10, newSnow: 2));
        Assert.True(act.RecentSnowActive);
        Assert.True(act.SnowpackActive);
    }

    [Fact]
    public void Null_weather_with_depth_but_no_recent_snow_keeps_snowpack_only()
    {
        var act = SnowRelevance.Evaluate(weather: null, Snowpack(depthIn: 10));
        Assert.False(act.RecentSnowActive);
        Assert.True(act.SnowpackActive);
    }

    [Fact]
    public void Null_weather_no_depth_deactivates_both()
    {
        var act = SnowRelevance.Evaluate(weather: null, Snowpack(depthIn: 0));
        Assert.False(act.RecentSnowActive);
        Assert.False(act.SnowpackActive);
    }

    [Fact]
    public void Mixed_hours_likely_snow_anywhere_activates_recent()
    {
        var weather = WeatherWith(
            Hour("Sunny"),
            Hour("Partly cloudy"),
            Hour("Snow likely", precip: 70),
            Hour("Sunny"));
        var act = SnowRelevance.Evaluate(weather, Snowpack(depthIn: 0));
        Assert.True(act.RecentSnowActive);
    }
}
