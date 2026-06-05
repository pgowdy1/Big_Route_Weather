using RouteWeather.Core.Models;

namespace RouteWeather.Core.Sources;

public interface IForecastSource
{
    string Name { get; }

    IReadOnlySet<string> ActiveFactors { get; }

    Task<WeatherSnapshot?> FetchAsync(double lat, double lon, CancellationToken ct);
}
