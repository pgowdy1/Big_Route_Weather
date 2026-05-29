namespace RouteWeather.Core.Models;

public record FactorScore(
    string Name,
    int Score,
    double Weight,
    string Detail
);
