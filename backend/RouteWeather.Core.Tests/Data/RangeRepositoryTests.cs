using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;
using Xunit;

namespace RouteWeather.Core.Tests.Data;

public class RangeRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_returns_ranges_ordered_by_displayOrder()
    {
        var factory = InMemoryFactory();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.Ranges.AddRange(
                new RangeEntity { Slug = "b", Name = "B", Color = "#fff", PerimeterGeoJson = "{}", DisplayOrder = 2 },
                new RangeEntity { Slug = "a", Name = "A", Color = "#fff", PerimeterGeoJson = "{}", DisplayOrder = 1 });
            await seed.SaveChangesAsync();
        }

        var repo = new RangeRepository(factory);
        var ranges = await repo.GetAllAsync();

        Assert.Equal(new[] { "a", "b" }, ranges.Select(r => r.Slug));
    }

    private static IDbContextFactory<RouteWeatherContext> InMemoryFactory()
    {
        var opts = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(opts);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<RouteWeatherContext>
    {
        private readonly DbContextOptions<RouteWeatherContext> _opts;
        public TestDbContextFactory(DbContextOptions<RouteWeatherContext> opts) => _opts = opts;
        public RouteWeatherContext CreateDbContext() => new(_opts);
    }
}
