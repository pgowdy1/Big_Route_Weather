using RouteWeather.Core.Models;

namespace RouteWeather.Core.Sources;

public interface IAirQualitySource
{
    string Name { get; }

    Task<AirQualitySnapshot?> FetchAsync(double lat, double lon, CancellationToken ct);
}
