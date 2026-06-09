using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RouteWeather.API.Options;
using RouteWeather.API.Services;
using RouteWeather.Core.Grading;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class ConditionsAggregatorTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed class Harness
    {
        public TestDbContextFactory DbFactory { get; }
        public FakeForecastSource Forecast { get; } = new();
        public FakeSnowpackSource Snowpack { get; } = new();
        public MemoryCache Memory { get; } = new(new MemoryCacheOptions());
        public RouteEntity Route { get; } = TestData.Route();
        public ConditionsAggregator Aggregator { get; }

        public Harness(string dbName)
        {
            DbFactory = new TestDbContextFactory(dbName);
            TestData.SeedRoutesAsync(DbFactory, Route).GetAwaiter().GetResult();

            var sourceOptions = new ForecastSourcesOptions
            {
                Sources =
                [
                    new SourceOptions { Name = "NWS", Enabled = true, Weight = 1.0, CacheTtlMinutes = 60 },
                    new SourceOptions { Name = "SNOTEL", Enabled = true, Weight = 1.0, CacheTtlMinutes = 60 },
                ],
            };

            Aggregator = new ConditionsAggregator(
                new[] { Forecast },
                new[] { Snowpack },
                new ForecastCacheRepository(DbFactory),
                Microsoft.Extensions.Options.Options.Create(sourceOptions),
                Microsoft.Extensions.Options.Options.Create(new WarmerOptions()),
                new ConsensusCalculator(0.25, 0.50),
                Memory,
                NullLogger<ConditionsAggregator>.Instance);
        }

        public async Task AddForecastRowAsync(DateTime fetchedAtUtc, DateTime expiresAtUtc)
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            db.CachedForecasts.Add(new CachedForecastEntity
            {
                RouteId = Route.Id,
                Source = "NWS",
                PayloadJson = JsonSerializer.Serialize(TestData.Snapshot(), JsonOpts),
                FetchedAtUtc = fetchedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CacheOnly_NoRows_ReturnsNullGrade_AndNeverCallsUpstream()
    {
        var h = new Harness(nameof(CacheOnly_NoRows_ReturnsNullGrade_AndNeverCallsUpstream));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.Null(conditions.Grade);
        Assert.Equal(0, h.Forecast.FetchCount);
        Assert.Equal(0, h.Snowpack.FetchCount);
    }

    [Fact]
    public async Task CacheOnly_ExpiredRowWithin24h_ServesGradeMarkedStale_WithoutUpstream()
    {
        var h = new Harness(nameof(CacheOnly_ExpiredRowWithin24h_ServesGradeMarkedStale_WithoutUpstream));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddHours(-2),
            expiresAtUtc: DateTime.UtcNow.AddHours(-1)); // expired but well inside 24h

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.Grade);
        Assert.True(conditions.IsStale);
        Assert.Equal(0, h.Forecast.FetchCount);
    }

    [Fact]
    public async Task CacheOnly_RowOlderThan24h_IsTreatedAsMissing()
    {
        var h = new Harness(nameof(CacheOnly_RowOlderThan24h_IsTreatedAsMissing));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddHours(-25),
            expiresAtUtc: DateTime.UtcNow.AddHours(-24));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.Null(conditions.Grade);
        Assert.Equal(0, h.Forecast.FetchCount);
    }

    [Fact]
    public async Task CacheOnly_FreshRow_IsNotStale()
    {
        var h = new Harness(nameof(CacheOnly_FreshRow_IsNotStale));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.Grade);
        Assert.False(conditions.IsStale);
    }

    [Fact]
    public async Task CacheOnly_MemoryHit_SkipsSqlite()
    {
        var h = new Harness(nameof(CacheOnly_MemoryHit_SkipsSqlite));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));

        var first = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);
        Assert.NotNull(first.Grade);

        // Wipe SQLite; a memory hit must still serve the conditions.
        await using (var db = await h.DbFactory.CreateDbContextAsync())
        {
            db.CachedForecasts.RemoveRange(db.CachedForecasts);
            await db.SaveChangesAsync();
        }

        var second = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);
        Assert.NotNull(second.Grade);
    }

    [Fact]
    public async Task ReadThrough_FetchesUpstream_ThenCacheOnlyServesFromMemory()
    {
        var h = new Harness(nameof(ReadThrough_FetchesUpstream_ThenCacheOnlyServesFromMemory));

        var warmed = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.ReadThrough);
        Assert.NotNull(warmed.Grade);
        Assert.False(warmed.IsStale);
        Assert.Equal(1, h.Forecast.FetchCount);

        var read = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);
        Assert.NotNull(read.Grade);
        Assert.Equal(1, h.Forecast.FetchCount); // no additional upstream call
    }

    [Fact]
    public async Task GetManyCacheOnly_MixedRoutes_GradesOnlyThoseWithData()
    {
        var h = new Harness(nameof(GetManyCacheOnly_MixedRoutes_GradesOnlyThoseWithData));
        var bare = TestData.Route(id: 2, slug: "mt-bare", mountain: "Mt Bare");
        await TestData.SeedRoutesAsync(h.DbFactory, bare);
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50)); // row belongs to h.Route (id 1)

        var pairs = await h.Aggregator.GetManyCacheOnlyAsync(new[] { h.Route, bare });

        Assert.Equal(2, pairs.Count);
        Assert.NotNull(pairs.Single(p => p.Route.Slug == "mt-test").Conditions.Grade);
        Assert.Null(pairs.Single(p => p.Route.Slug == "mt-bare").Conditions.Grade);
        Assert.Equal(0, h.Forecast.FetchCount);
    }
}
