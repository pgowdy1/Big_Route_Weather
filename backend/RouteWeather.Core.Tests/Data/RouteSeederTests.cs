using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using RouteWeather.Data.Entities;
using Xunit;

namespace RouteWeather.Core.Tests.Data;

public class RouteSeederTests
{
    private static RouteWeatherContext NewContext() =>
        new(new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static readonly string[] ExpectedGlaciated =
    {
        "mount-rainier", "mount-hood", "mount-adams", "mount-baker", "mount-shasta",
        "glacier-peak", "mount-shuksan", "mount-stuart", "forbidden-peak", "dragontail-peak",
        "eldorado-peak", "sahale-peak", "bonanza-peak", "goode-mountain", "sloan-peak",
        "silver-star-mountain", "north-sister", "mount-jefferson", "north-palisade", "mount-sill",
        "middle-palisade", "mount-lyell", "mount-ritter", "banner-peak", "mount-darwin",
        "mount-conness", "gannett-peak", "mount-helen", "mount-sacagawea",
    };

    [Fact]
    public async Task Seeds_exactly_the_expected_glaciated_peaks()
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var glaciated = await db.Routes.Where(r => r.IsGlaciated).Select(r => r.Slug).ToListAsync();

        Assert.Equal(29, glaciated.Count);
        Assert.Equal(ExpectedGlaciated.OrderBy(s => s), glaciated.OrderBy(s => s));
    }

    [Theory]
    [InlineData("pikes-peak")]
    [InlineData("south-sister")]
    [InlineData("mount-st-helens")]
    [InlineData("mount-whitney")]
    public async Task Walk_up_peaks_are_not_glaciated(string slug)
    {
        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);

        var route = await db.Routes.SingleAsync(r => r.Slug == slug);
        Assert.False(route.IsGlaciated);
    }

    [Fact]
    public async Task Reconciles_IsGlaciated_on_existing_rows()
    {
        await using var db = NewContext();
        // First seed populates the catalog.
        await RouteSeeder.SeedAsync(db);
        // Corrupt two rows the way a pre-migration DB would look (all false).
        var rainier = await db.Routes.SingleAsync(r => r.Slug == "mount-rainier");
        var pikes = await db.Routes.SingleAsync(r => r.Slug == "pikes-peak");
        rainier.IsGlaciated = false;   // should be true
        pikes.IsGlaciated = true;      // should be false
        await db.SaveChangesAsync();

        // Second seed must reconcile both back to the catalog.
        await RouteSeeder.SeedAsync(db);

        Assert.True((await db.Routes.SingleAsync(r => r.Slug == "mount-rainier")).IsGlaciated);
        Assert.False((await db.Routes.SingleAsync(r => r.Slug == "pikes-peak")).IsGlaciated);
    }
}
