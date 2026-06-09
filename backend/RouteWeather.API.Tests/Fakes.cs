using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;

namespace RouteWeather.API.Tests;

/// Counting fake — FetchCount > 0 in a CacheOnly test means the inversion is broken.
public sealed class FakeForecastSource : IForecastSource
{
    public string Name { get; init; } = "NWS";
    public IReadOnlySet<string> ActiveFactors { get; init; } = ForecastFactors.All;
    public int FetchCount;
    public Func<WeatherSnapshot?> OnFetch { get; set; } = () => TestData.Snapshot();

    public Task<WeatherSnapshot?> FetchAsync(double lat, double lon, CancellationToken ct)
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
