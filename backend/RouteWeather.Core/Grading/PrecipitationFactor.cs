using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class PrecipitationFactor
{
    public const double Weight = 0.20;

    public static int Score(int precipProbabilityPct) =>
        ScoringMath.LinearBetween(precipProbabilityPct, goodValue: 0, badValue: 80);

    public static string Detail(int precipProbabilityPct) =>
        $"{precipProbabilityPct}% chance of precip";

    public static (Grade? Cap, string Reason) Cap(int precipProbabilityPct)
    {
        if (precipProbabilityPct > 90) return (Grade.F, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 70) return (Grade.D, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 50) return (Grade.C, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 30) return (Grade.B, $"{precipProbabilityPct}% chance of precip");
        return (null, string.Empty);
    }
}
