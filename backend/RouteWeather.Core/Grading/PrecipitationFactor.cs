using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class PrecipitationFactor
{
    public const double Weight = 0.20;

    // Trace forecasts below this don't drag the score.
    public const double AmountEngageFloorIn = 0.05;
    // The amount bad-threshold normalized to a 24h window; scales linearly by hours.
    public const double BadAmountInPer24h = 1.0;

    public static int Score(int precipProbabilityPct) =>
        ScoringMath.LinearBetween(precipProbabilityPct, goodValue: 0, badValue: 80);

    public static int Score(int precipProbabilityPct, double? amountIn, int windowHours)
    {
        var probScore = Score(precipProbabilityPct);
        if (amountIn is null || amountIn.Value < AmountEngageFloorIn || windowHours <= 0)
            return probScore;

        var badAmount = BadAmountInPer24h * windowHours / 24.0;
        var amountScore = ScoringMath.LinearBetween(amountIn.Value, goodValue: 0, badValue: badAmount);
        return Math.Min(probScore, amountScore);
    }

    public static string Detail(int precipProbabilityPct) =>
        $"{precipProbabilityPct}% chance of precip";

    public static string Detail(int precipProbabilityPct, double? amountIn) =>
        amountIn is not null && amountIn.Value >= AmountEngageFloorIn
            ? $"{precipProbabilityPct}% chance of precip, ~{amountIn.Value:0.0#}\" expected"
            : Detail(precipProbabilityPct);

    public static (Grade? Cap, string Reason) Cap(int precipProbabilityPct)
    {
        if (precipProbabilityPct > 90) return (Grade.F, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 70) return (Grade.D, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 50) return (Grade.C, $"{precipProbabilityPct}% chance of precip");
        if (precipProbabilityPct > 30) return (Grade.B, $"{precipProbabilityPct}% chance of precip");
        return (null, string.Empty);
    }
}
