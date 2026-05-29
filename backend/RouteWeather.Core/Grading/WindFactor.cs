namespace RouteWeather.Core.Grading;

public static class WindFactor
{
    public const double Weight = 0.25;

    public static int Score(double summitWindMph) =>
        ScoringMath.LinearBetween(summitWindMph, goodValue: 10, badValue: 50);

    public static string Detail(double summitWindMph) =>
        $"Sustained {summitWindMph:0} mph at summit";
}
