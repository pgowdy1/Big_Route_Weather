using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class SnowRelevance
{
    public record Activation(bool RecentSnowActive, bool SnowpackActive);

    public static Activation Evaluate(WeatherSnapshot? weather, SnowpackSnapshot? snowpack) =>
        new(
            RecentSnowActive: HasRecentSnow(snowpack) || IsSnowExpected(weather),
            SnowpackActive: HasSnowOnGround(snowpack));

    public static bool HasSnowOnGround(SnowpackSnapshot? snowpack) =>
        snowpack is not null && snowpack.SnowDepthIn > 0;

    public static bool HasRecentSnow(SnowpackSnapshot? snowpack) =>
        snowpack is not null && snowpack.NewSnowLast7DaysIn > 0;

    private const int LikelyPrecipPct = 50;

    public static bool IsSnowExpected(WeatherSnapshot? weather)
    {
        var hourly = weather?.Hourly;
        if (hourly is null || hourly.Count == 0) return false;

        foreach (var h in hourly)
        {
            if (h.PrecipitationProbabilityPct >= LikelyPrecipPct &&
                !string.IsNullOrEmpty(h.ShortForecast) &&
                h.ShortForecast.Contains("snow", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
