namespace RouteWeather.Core.Grading;

public static class PrecipitationFactor
{
    public const double Weight = 0.20;

    public static int Score(int precipProbabilityPct) =>
        ScoringMath.Clamp(100 - precipProbabilityPct);

    public static string Detail(int precipProbabilityPct) =>
        $"{precipProbabilityPct}% chance of precip";
}
