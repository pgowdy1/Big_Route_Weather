using RouteWeather.Core.Models;
using RouteWeather.Data;
using RouteWeather.Data.Entities;

namespace RouteWeather.API.Tests;

public static class TestData
{
    public static RouteEntity Route(int id = 1, string slug = "mt-test", string mountain = "Mt Test") => new()
    {
        Id = id,
        Slug = slug,
        Mountain = mountain,
        RouteName = "SW Ridge",
        SummitElevationFt = 12000,
        SummitLat = 43.5,
        SummitLon = -110.8,
        ClassDifficulty = "2",
        SnotelStationTriplet = "999:WY:SNTL",
        RangeId = 1,
    };

    public static RangeEntity Range(int id = 1, string slug = "test-range") => new()
    {
        Id = id,
        Slug = slug,
        Name = "Test Range",
        Color = "#ff0000",
        PerimeterGeoJson = "{}",
        DisplayOrder = 1,
    };

    /// Benign weather that grades well, with enough hourly data for window grades.
    /// Captures wall-clock UtcNow at call time — tests asserting exact time
    /// boundaries should build their own snapshots with explicit timestamps.
    public static WeatherSnapshot Snapshot() => new(
        WindMph: 5,
        TempF: 30,
        PrecipitationProbabilityPct: 10,
        Next48Hours: Enumerable.Range(0, 48)
            .Select(i => new HourlyForecast(DateTimeOffset.UtcNow.AddHours(i), 30, 5, 10, "Clear"))
            .ToList());

    /// Minimal RouteConditions for controller tests (grade present, no weather detail).
    public static RouteConditions Conditions(RouteEntity r, bool isStale) => new(
        new RouteWeather.Core.Models.Route(
            r.Slug, r.Mountain, r.RouteName, r.SummitElevationFt,
            r.SummitLat, r.SummitLon, r.ClassDifficulty, r.SnotelStationTriplet),
        Grade.B,
        85,
        Array.Empty<Driver>(),
        Array.Empty<FactorScore>(),
        "test",
        DateTimeOffset.UtcNow,
        isStale,
        null,
        null,
        null,
        new SourceFreshness(null, null),
        null,
        null);

    public static async Task SeedRoutesAsync(TestDbContextFactory factory, params RouteEntity[] routes)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!db.Ranges.Any()) db.Ranges.Add(Range());
        db.Routes.AddRange(routes);
        await db.SaveChangesAsync();
    }
}
