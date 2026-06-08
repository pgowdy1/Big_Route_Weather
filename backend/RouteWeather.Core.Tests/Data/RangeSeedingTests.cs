using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using Xunit;

namespace RouteWeather.Core.Tests.Data;

public class RangeSeedingTests
{
    [Fact]
    public async Task Seeds_all_six_ranges_and_eighty_seven_routes()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        Assert.Equal(6, await db.Ranges.CountAsync());
        Assert.Equal(87, await db.Routes.CountAsync());

        var coloradoId = await db.Ranges.Where(r => r.Slug == "colorado-14ers").Select(r => r.Id).SingleAsync();
        Assert.Equal(58, await db.Routes.CountAsync(r => r.RangeId == coloradoId));

        Assert.All(await db.Routes.ToListAsync(), r => Assert.NotEqual(0, r.RangeId));
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
        Assert.Equal(87, await db.Routes.CountAsync());
    }

    private static RouteWeatherContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RouteWeatherContext(opts);
    }
}
