using RouteWeather.Core.Services;
using Xunit;

namespace RouteWeather.Core.Tests.Services;

public class SolarCalculatorTests
{
    // Mt Rainier summit.
    private const double Lat = 46.853;
    private const double Lon = -121.760;

    [Fact]
    public void SummerSolstice_rainier_matchesKnownTimes()
    {
        var d = SolarCalculator.ComputeUtc(Lat, Lon, new DateOnly(2026, 6, 20));

        Assert.NotNull(d);
        // Sunrise ~05:11 PDT = 12:11 UTC; sunset ~21:11 PDT = 04:11 UTC next day. ±10 min.
        AssertWithin(d!.SunriseUtc, DateTimeOffset.Parse("2026-06-20T12:11:00Z"), minutes: 10);
        AssertWithin(d.SunsetUtc, DateTimeOffset.Parse("2026-06-21T04:11:00Z"), minutes: 10);
        Assert.InRange(d.DaylightHours, 15.7, 16.3);
    }

    [Fact]
    public void Equinox_daylightIsNearTwelveHours()
    {
        var d = SolarCalculator.ComputeUtc(Lat, Lon, new DateOnly(2026, 3, 20));
        Assert.NotNull(d);
        Assert.InRange(d!.DaylightHours, 11.8, 12.4);
    }

    [Fact]
    public void NextDaylight_afterSunset_rollsToTomorrow()
    {
        // 06:00 UTC on Jun 21 is ~23:00 PDT Jun 20 — past sunset.
        var next = SolarCalculator.NextDaylight(Lat, Lon, DateTimeOffset.Parse("2026-06-21T06:00:00Z"));
        Assert.NotNull(next);
        Assert.True(next!.SunsetUtc > DateTimeOffset.Parse("2026-06-21T06:00:00Z"));
    }

    [Fact]
    public void PolarNight_returnsNull() =>
        Assert.Null(SolarCalculator.ComputeUtc(80.0, 0.0, new DateOnly(2026, 12, 21)));

    private static void AssertWithin(DateTimeOffset actual, DateTimeOffset expected, int minutes) =>
        Assert.True((actual - expected).Duration() <= TimeSpan.FromMinutes(minutes),
            $"Expected {expected:u} ±{minutes}m but got {actual:u}");
}
