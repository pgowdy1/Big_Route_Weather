using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class ForecastCacheRepositoryTests
{
    private static async Task AddRowAsync(TestDbContextFactory factory, int routeId, string source, DateTime expiresAtUtc)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.CachedForecasts.Add(new CachedForecastEntity
        {
            RouteId = routeId,
            Source = source,
            PayloadJson = "{}",
            FetchedAtUtc = DateTime.UtcNow.AddHours(-2),
            ExpiresAtUtc = expiresAtUtc,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetForRouteAsync_ReturnsOnlyThatRoutesRows_IncludingExpired()
    {
        var factory = new TestDbContextFactory(nameof(GetForRouteAsync_ReturnsOnlyThatRoutesRows_IncludingExpired));
        await AddRowAsync(factory, routeId: 1, "NWS", DateTime.UtcNow.AddHours(-1));   // expired
        await AddRowAsync(factory, routeId: 1, "SNOTEL", DateTime.UtcNow.AddHours(1)); // fresh
        await AddRowAsync(factory, routeId: 2, "NWS", DateTime.UtcNow.AddHours(1));    // other route

        var repo = new ForecastCacheRepository(factory);
        var rows = await repo.GetForRouteAsync(1);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.RouteId));
    }

    [Fact]
    public async Task GetAllLatestAsync_ReturnsAllRowsAcrossRoutes()
    {
        var factory = new TestDbContextFactory(nameof(GetAllLatestAsync_ReturnsAllRowsAcrossRoutes));
        await AddRowAsync(factory, routeId: 1, "NWS", DateTime.UtcNow.AddHours(-1));
        await AddRowAsync(factory, routeId: 2, "NWS", DateTime.UtcNow.AddHours(1));
        await AddRowAsync(factory, routeId: 2, "SNOTEL", DateTime.UtcNow.AddHours(1));

        var repo = new ForecastCacheRepository(factory);
        var rows = await repo.GetAllLatestAsync();

        Assert.Equal(3, rows.Count);
    }
}
