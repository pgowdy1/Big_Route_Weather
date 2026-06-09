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
            // Suffix with a GUID: EF InMemory stores are keyed process-wide by name,
            // so identical test names (or parallel runs) must never share state.
            // Sharing within one test happens via this factory INSTANCE, not the name.
            .UseInMemoryDatabase($"{dbName}-{Guid.NewGuid():N}")
            .Options;
    }

    public RouteWeatherContext CreateDbContext() => new(_options);

    public Task<RouteWeatherContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult(CreateDbContext());
}
