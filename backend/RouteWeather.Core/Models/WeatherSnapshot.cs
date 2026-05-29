namespace RouteWeather.Core.Models;

public record WeatherSnapshot(
    double WindMph,
    double TempF,
    int PrecipitationProbabilityPct,
    IReadOnlyList<HourlyForecast> Next48Hours
);

public record HourlyForecast(
    DateTimeOffset Time,
    double TempF,
    double WindMph,
    int PrecipitationProbabilityPct,
    string ShortForecast
);
