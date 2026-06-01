using RouteWeather.Core.Models;

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

    public static (Grade? Cap, string Reason) Cap(double summitTempF)
    {
        if (summitTempF < -15 || summitTempF > 95) return (Grade.D, $"summit {summitTempF:0}°F");
        if (summitTempF < 0 || summitTempF > 85) return (Grade.C, $"summit {summitTempF:0}°F");
        if (summitTempF < 10 || summitTempF > 75) return (Grade.B, $"summit {summitTempF:0}°F");
        return (null, string.Empty);
    }
}
