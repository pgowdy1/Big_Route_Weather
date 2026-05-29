namespace RouteWeather.Core.Grading;

public static class SnowpackFactor
{
    public const double Weight = 0.20;

    public static int Score(double percentOfNormalSwe)
    {
        if (percentOfNormalSwe >= 80 && percentOfNormalSwe <= 120) return 100;
        if (percentOfNormalSwe < 80)
            return ScoringMath.LinearBetween(percentOfNormalSwe, goodValue: 80, badValue: 30);
        return ScoringMath.LinearBetween(percentOfNormalSwe, goodValue: 120, badValue: 200);
    }

    public static string Detail(double sweIn, double percentOfNormal) =>
        $"SWE {sweIn:0.0}\" ({percentOfNormal:0}% of normal)";
}
