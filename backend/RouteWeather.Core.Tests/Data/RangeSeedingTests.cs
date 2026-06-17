using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using Xunit;

namespace RouteWeather.Core.Tests.Data;

public class RangeSeedingTests
{
    [Fact]
    public async Task Seeds_all_six_ranges_with_expected_route_counts()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        Assert.Equal(6, await db.Ranges.CountAsync());
        Assert.Equal(124, await db.Routes.CountAsync());

        async Task<int> CountIn(string slug)
        {
            var id = await db.Ranges.Where(r => r.Slug == slug).Select(r => r.Id).SingleAsync();
            return await db.Routes.CountAsync(r => r.RangeId == id);
        }

        Assert.Equal(58, await CountIn("colorado-14ers"));
        Assert.Equal(25, await CountIn("cascades"));
        Assert.Equal(20, await CountIn("sierra-nevada"));
        Assert.Equal(11, await CountIn("wasatch"));

        Assert.All(await db.Routes.ToListAsync(), r => Assert.NotEqual(0, r.RangeId));

        var slugs = await db.Routes.Select(r => r.Slug).ToListAsync();
        Assert.Equal(slugs.Count, slugs.Distinct().Count()); // no duplicate slugs
    }

    [Fact]
    public async Task Every_range_has_valid_GeoJSON_polygon_and_hex_color()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var ranges = await db.Ranges.ToListAsync();
        Assert.NotEmpty(ranges);

        foreach (var r in ranges)
        {
            Assert.Matches("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", r.Color);

            using var doc = JsonDocument.Parse(r.PerimeterGeoJson);
            Assert.Equal("Polygon", doc.RootElement.GetProperty("type").GetString());
            var coords = doc.RootElement.GetProperty("coordinates");
            Assert.True(coords.GetArrayLength() >= 1);
            Assert.True(coords[0].GetArrayLength() >= 4); // a closed polygon needs >=4 points
        }
    }

    [Fact]
    public async Task SeedAsync_is_idempotent()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);
        await RouteSeeder.SeedAsync(db);

        Assert.Equal(6, await db.Ranges.CountAsync());
        Assert.Equal(124, await db.Routes.CountAsync());
    }

    [Fact]
    public async Task Every_peak_falls_within_its_range_polygon()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var rangesById = (await db.Ranges.ToListAsync()).ToDictionary(r => r.Id);
        var routes = await db.Routes.ToListAsync();

        foreach (var route in routes)
        {
            var range = rangesById[route.RangeId];
            using var doc = JsonDocument.Parse(range.PerimeterGeoJson);
            var ring = doc.RootElement.GetProperty("coordinates")[0];
            var poly = new List<(double Lng, double Lat)>();
            foreach (var p in ring.EnumerateArray())
            {
                poly.Add((p[0].GetDouble(), p[1].GetDouble()));
            }

            Assert.True(
                PointInPolygon(route.SummitLon, route.SummitLat, poly),
                $"{route.Mountain} ({route.SummitLat}, {route.SummitLon}) is outside the {range.Name} polygon.");
        }
    }

    private static bool PointInPolygon(double x, double y, IReadOnlyList<(double X, double Y)> poly)
    {
        // Ray-casting algorithm. Works for any simple polygon (convex or not).
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var (xi, yi) = poly[i];
            var (xj, yj) = poly[j];
            bool intersects = ((yi > y) != (yj > y)) &&
                              (x < (xj - xi) * (y - yi) / (yj - yi) + xi);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    private static RouteWeatherContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RouteWeatherContext(opts);
    }
}
