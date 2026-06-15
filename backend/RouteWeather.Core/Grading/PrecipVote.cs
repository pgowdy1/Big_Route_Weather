using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;

namespace RouteWeather.Core.Grading;

/// <summary>
/// Converts a single source's per-hour precip into a [0,1] "vote" for the
/// confidence-weighted consensus. Probability sources (NWS, GFS) vote with their
/// PoP; amount-only models vote via a soft ramp on hourly QPF, so the field all
/// five sources report drives a genuine multi-source agreement signal.
/// </summary>
public static class PrecipVote
{
    // QPF (inches/hour) at/below LoIn reads as dry; at/above HiIn as certain precip.
    public const double LoIn = 0.005;
    public const double HiIn = 0.05;

    /// <summary>Vote in [0,1], or null when the source has no precip signal this hour.</summary>
    public static double? For(SourceSnapshot source, HourlyForecast hour)
    {
        if (source.ActiveFactors.Contains(ForecastFactors.Precipitation))
            return Math.Clamp(hour.PrecipitationProbabilityPct / 100.0, 0.0, 1.0);
        return hour.PrecipitationIn is null ? null : Ramp(hour.PrecipitationIn.Value);
    }

    public static double Ramp(double qpfInPerHr)
    {
        if (qpfInPerHr <= LoIn) return 0.0;
        if (qpfInPerHr >= HiIn) return 1.0;
        return (qpfInPerHr - LoIn) / (HiIn - LoIn);
    }
}
