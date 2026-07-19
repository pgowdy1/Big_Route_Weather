using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RouteWeather.API.Options;
using RouteWeather.API.Services;
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class ConditionsAggregatorTests
{
    // Must mirror ConditionsAggregator.JsonOpts — rows written here are read by production code.
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed class Harness
    {
        public TestDbContextFactory DbFactory { get; }
        public FakeForecastSource Forecast { get; } = new();
        public FakeSnowpackSource Snowpack { get; } = new();
        public FakeAirQualitySource AirQuality { get; } = new();
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
                    new SourceOptions { Name = "AirQuality", Enabled = true, Weight = 1.0, CacheTtlMinutes = 180 },
                ],
            };

            Aggregator = new ConditionsAggregator(
                new[] { Forecast },
                new[] { Snowpack },
                new[] { AirQuality },
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

        public async Task AddAirQualityRowAsync(string payloadJson, DateTime fetchedAtUtc, DateTime expiresAtUtc)
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            db.CachedForecasts.Add(new CachedForecastEntity
            {
                RouteId = Route.Id,
                Source = "AirQuality",
                PayloadJson = payloadJson,
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

    [Fact]
    public async Task CacheOnly_CorruptPayload_OtherSourceStillGrades_NoThrow()
    {
        var h = new Harness(nameof(CacheOnly_CorruptPayload_OtherSourceStillGrades_NoThrow));
        var snowpack = new SnowpackSnapshot(
            SnowWaterEquivalentIn: 12.0,
            SnowDepthIn: 40.0,
            NewSnowLast7DaysIn: 6.0,
            PercentOfNormalSwe: 110.0,
            StationTriplet: "999:WY:SNTL",
            DailyDepthIn: Array.Empty<DailyDepthPoint>());

        await using (var db = await h.DbFactory.CreateDbContextAsync())
        {
            db.CachedForecasts.Add(new CachedForecastEntity
            {
                RouteId = h.Route.Id,
                Source = "NWS",
                PayloadJson = "{ not valid json",
                FetchedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(50),
            });
            db.CachedForecasts.Add(new CachedForecastEntity
            {
                RouteId = h.Route.Id,
                Source = "SNOTEL",
                PayloadJson = JsonSerializer.Serialize(snowpack, JsonOpts),
                FetchedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(50),
            });
            await db.SaveChangesAsync();
        }

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.Grade); // corrupt NWS degrades to "source absent"; SNOTEL still grades
        Assert.Equal(0, h.Forecast.FetchCount);
        Assert.Equal(0, h.Snowpack.FetchCount);
    }

    [Fact]
    public async Task ReadThrough_fetchesAndCachesAirQuality()
    {
        var h = new Harness(nameof(ReadThrough_fetchesAndCachesAirQuality));
        h.AirQuality.Result = new AirQualitySnapshot(42, 5.0);

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.ReadThrough);

        Assert.True(conditions.AirQuality is { UsAqi: 42 });
        Assert.NotNull(conditions.Sources.AirQualityFetchedAt);

        await using var db = await h.DbFactory.CreateDbContextAsync();
        var row = db.CachedForecasts.SingleOrDefault(r => r.RouteId == h.Route.Id && r.Source == "AirQuality");
        Assert.NotNull(row);
    }

    [Fact]
    public async Task CacheOnly_readsAirQualityRow_withoutFetching()
    {
        var h = new Harness(nameof(CacheOnly_readsAirQualityRow_withoutFetching));
        // Fresh forecast row so the route grades, plus a fresh AirQuality row.
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));
        await h.AddAirQualityRowAsync(
            """{"usAqi":80,"pm25":12.0}""",
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.AirQuality);
        Assert.Equal(80, conditions.AirQuality!.UsAqi);
        Assert.Equal(0, h.AirQuality.FetchCount);
    }

    [Fact]
    public async Task StaleAirQuality_doesNotFlagRouteStale()
    {
        var h = new Harness(nameof(StaleAirQuality_doesNotFlagRouteStale));
        // Fresh forecast keeps the route un-stale.
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));
        // AirQuality past its TTL but well within the 24h serve-stale window.
        await h.AddAirQualityRowAsync(
            """{"usAqi":80,"pm25":12.0}""",
            fetchedAtUtc: DateTime.UtcNow.AddHours(-2),
            expiresAtUtc: DateTime.UtcNow.AddHours(-1));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.AirQuality);
        Assert.False(conditions.IsStale);
    }

    [Fact]
    public async Task FailedAirQuality_yieldsNullAirQuality_gradeUnaffected()
    {
        var h = new Harness(nameof(FailedAirQuality_yieldsNullAirQuality_gradeUnaffected));
        h.Forecast.OnFetch = () => TestData.Snapshot(); // explicit live forecast → grade does not rest on the fake's default
        h.AirQuality.Result = null; // upstream AQI failed/unavailable

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.ReadThrough);

        Assert.Null(conditions.AirQuality);
        Assert.NotNull(conditions.Grade);
    }

    [Fact]
    public async Task HighAirQuality_capsServedGrade()
    {
        var h = new Harness(nameof(HighAirQuality_capsServedGrade));
        await h.AddForecastRowAsync(
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));
        await h.AddAirQualityRowAsync(
            """{"usAqi":250,"pm25":80.0}""",
            fetchedAtUtc: DateTime.UtcNow.AddMinutes(-10),
            expiresAtUtc: DateTime.UtcNow.AddMinutes(50));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.Equal(Grade.F, conditions.Grade);
        Assert.Contains("air quality", conditions.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CacheOnly_WithForecastRow_ComputesClimbWindowsAndHourlyScores()
    {
        var h = new Harness(nameof(CacheOnly_WithForecastRow_ComputesClimbWindowsAndHourlyScores));
        await h.AddForecastRowAsync(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(55));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.NotNull(conditions.Windows);
        Assert.NotNull(conditions.HourlyScores);
        Assert.Equal(48, conditions.HourlyScores!.Count);      // one score per series hour
        // Benign fixture should clear the B bar every hour. If this assert alone fails,
        // the fixture's 30°F hours score sub-B on the hourly path — relax to
        // Assert.Contains(conditions.HourlyScores!, q => q.Qualifies) rather than touching
        // the shared TestData.Snapshot() fixture, and report that you did so.
        Assert.All(conditions.HourlyScores!, q => Assert.True(q.Qualifies));
        Assert.NotEmpty(conditions.Windows!);
    }

    [Fact]
    public async Task CacheOnly_NoRows_HasNoWindows()
    {
        var h = new Harness(nameof(CacheOnly_NoRows_HasNoWindows));

        var conditions = await h.Aggregator.GetConditionsAsync(h.Route, FetchMode.CacheOnly);

        Assert.True(conditions.Windows is null || conditions.Windows.Count == 0);
    }
}
