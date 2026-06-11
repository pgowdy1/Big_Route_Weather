using RouteWeather.API.Services;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using RouteWeather.Data.Entities;

namespace RouteWeather.API.Tests;

/// Counting fake — FetchCount > 0 in a CacheOnly test means the inversion is broken.
public sealed class FakeForecastSource : IForecastSource
{
    public string Name { get; init; } = "NWS";
    public IReadOnlySet<string> ActiveFactors { get; init; } = ForecastFactors.All;
    public int FetchCount;
    public Func<WeatherSnapshot?> OnFetch { get; set; } = () => TestData.Snapshot();

    public Task<WeatherSnapshot?> FetchAsync(ForecastLocation location, CancellationToken ct)
    {
        FetchCount++;
        return Task.FromResult(OnFetch());
    }
}

public sealed class FakeSnowpackSource : ISnowpackSource
{
    public string Name { get; init; } = "SNOTEL";
    public int FetchCount;
    public Func<SnowpackSnapshot?> OnFetch { get; set; } = () => null;

    public Task<SnowpackSnapshot?> FetchAsync(string stationTriplet, CancellationToken ct)
    {
        FetchCount++;
        return Task.FromResult(OnFetch());
    }
}

public sealed class FakeAirQualitySource : IAirQualitySource
{
    public string Name => "AirQuality";
    public AirQualitySnapshot? Result { get; set; } = new(42, 5.0);
    public int FetchCount { get; private set; }

    public Task<AirQualitySnapshot?> FetchAsync(double lat, double lon, CancellationToken ct)
    {
        FetchCount++;
        return Task.FromResult(Result);
    }
}

public sealed class FakeConditionsAggregator : IConditionsAggregator
{
    public Func<RouteEntity, Core.Models.RouteConditions> OnGet { get; set; } =
        r => TestData.Conditions(r, isStale: false);
    public int Calls;
    public List<FetchMode> ModesSeen { get; } = new();

    public Task<Core.Models.RouteConditions> GetConditionsAsync(RouteEntity routeEntity, FetchMode mode, CancellationToken ct = default)
    {
        Calls++;
        ModesSeen.Add(mode);
        return Task.FromResult(OnGet(routeEntity));
    }

    public Task<IReadOnlyList<RouteConditionsPair>> GetManyCacheOnlyAsync(IReadOnlyList<RouteEntity> routes, CancellationToken ct = default)
    {
        Calls++;
        IReadOnlyList<RouteConditionsPair> pairs = routes.Select(r => new RouteConditionsPair(r, OnGet(r))).ToList();
        return Task.FromResult(pairs);
    }
}
