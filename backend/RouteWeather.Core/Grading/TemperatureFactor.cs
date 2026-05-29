namespace RouteWeather.Core.Grading;

public static class TemperatureFactor
{
    public const double Weight = 0.15;

    public static int Score(double summitTempF)
    {
        if (summitTempF >= 20 && summitTempF <= 60) return 100;
        if (summitTempF < 20)
            return ScoringMath.LinearBetween(summitTempF, goodValue: 20, badValue: -20);
        return ScoringMath.LinearBetween(summitTempF, goodValue: 60, badValue: 90);
    }

    public static string Detail(double summitTempF) =>
        $"Summit {summitTempF:0}°F";
}
