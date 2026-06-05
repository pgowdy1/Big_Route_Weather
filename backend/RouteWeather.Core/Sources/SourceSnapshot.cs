using RouteWeather.Core.Models;

namespace RouteWeather.Core.Sources;

public record SourceSnapshot(
    string SourceName,
    WeatherSnapshot Snapshot,
    DateTimeOffset FetchedAt,
    IReadOnlySet<string> ActiveFactors
);
