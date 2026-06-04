using RouteWeather.Core.Models;

namespace RouteWeather.Core.Sources;

public interface ISnowpackSource
{
    string Name { get; }

    Task<SnowpackSnapshot?> FetchAsync(string stationTriplet, CancellationToken ct);
}
