using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class WindFactor
{
    public const double Weight = 0.20;

    public static int Score(double summitWindMph) =>
        ScoringMath.LinearBetween(summitWindMph, goodValue: 10, badValue: 40);

    public static string Detail(double summitWindMph) =>
        $"Sustained {summitWindMph:0} mph at summit";

    public static (Grade? Cap, string Reason) Cap(double summitWindMph)
    {
        if (summitWindMph > 50) return (Grade.F, $"summit winds {summitWindMph:0} mph");
        if (summitWindMph > 40) return (Grade.D, $"summit winds {summitWindMph:0} mph");
        if (summitWindMph > 30) return (Grade.C, $"summit winds {summitWindMph:0} mph");
        if (summitWindMph > 20) return (Grade.B, $"summit winds {summitWindMph:0} mph");
        return (null, string.Empty);
    }
}
