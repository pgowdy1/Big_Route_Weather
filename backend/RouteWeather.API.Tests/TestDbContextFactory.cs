using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;

namespace RouteWeather.API.Tests;

/// IDbContextFactory over EF InMemory so repositories run unmodified in tests.
/// Each test should use a unique dbName to stay isolated.
public sealed class TestDbContextFactory : IDbContextFactory<RouteWeatherContext>
{
    private readonly DbContextOptions<RouteWeatherContext> _options;

    public TestDbContextFactory(string dbName)
    {
        _options = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
    }

    public RouteWeatherContext CreateDbContext() => new(_options);

    public Task<RouteWeatherContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult(CreateDbContext());
}
