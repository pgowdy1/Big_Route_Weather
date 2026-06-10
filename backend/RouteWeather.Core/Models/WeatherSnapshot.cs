namespace RouteWeather.Core.Models;

public record WeatherSnapshot(
    double WindMph,
    double TempF,
    int PrecipitationProbabilityPct,
    IReadOnlyList<HourlyForecast> Next48Hours,
    double? MaxGustMph = null,
    double? MaxCapeJkg = null,
    double? PrecipAmountIn = null
);

public record HourlyForecast(
    DateTimeOffset Time,
    double TempF,
    double WindMph,
    int PrecipitationProbabilityPct,
    string ShortForecast,
    double? GustMph = null,
    double? CapeJkg = null,
    double? PrecipitationIn = null,
    int? CloudCoverPct = null,
    double? VisibilityMiles = null,
    double? ApparentTempF = null
);
