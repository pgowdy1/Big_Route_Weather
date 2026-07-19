using System.Text.Json.Serialization;

namespace RouteWeather.Core.Models;

/// <summary>
/// Invariant: the scalar headline fields (WindMph, TempF, PrecipitationProbabilityPct,
/// MaxGustMph, MaxCapeJkg, PrecipAmountIn) always describe the FIRST HeadlineHours of
/// Hourly, even when Hourly extends to the full 7-day horizon. Builders (NWS parser,
/// OpenMeteo client, ConsensusCalculator) enforce this; HeadlineInvariantTests pins it.
/// The JSON name "next48Hours" is kept so per-source rows already persisted in the
/// SQLite forecast cache keep deserializing across the rename.
/// </summary>
public record WeatherSnapshot(
    double WindMph,
    double TempF,
    int PrecipitationProbabilityPct,
    [property: JsonPropertyName("next48Hours")] IReadOnlyList<HourlyForecast> Hourly,
    double? MaxGustMph = null,
    double? MaxCapeJkg = null,
    double? PrecipAmountIn = null)
{
    /// <summary>Hours the scalar headline fields describe (and the visible-window grades cover).</summary>
    public const int HeadlineHours = 48;

    /// <summary>First <paramref name="windowHours"/> of a series, anchored on the first
    /// hour's timestamp (time-based, not count-based — sparse series must not reach past
    /// the wall-clock window). Empty input → empty.</summary>
    public static IReadOnlyList<HourlyForecast> Window(IReadOnlyList<HourlyForecast> hours, int windowHours)
    {
        if (hours.Count == 0) return hours;
        var cutoff = hours[0].Time.AddHours(windowHours);
        return hours.Where(h => h.Time < cutoff).ToList();
    }

    /// <summary>The headline window: Window(hours, HeadlineHours).</summary>
    public static IReadOnlyList<HourlyForecast> HeadlineWindow(IReadOnlyList<HourlyForecast> hours) =>
        Window(hours, HeadlineHours);
}

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
