namespace RouteWeather.Core.Models;

public record SnowpackSnapshot(
    double SnowWaterEquivalentIn,
    double SnowDepthIn,
    double NewSnowLast7DaysIn,
    double PercentOfNormalSwe
);
