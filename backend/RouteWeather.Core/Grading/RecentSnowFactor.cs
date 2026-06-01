using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class RecentSnowFactor
{
    public const double Weight = 0.20;

    public static int Score(double newSnowLast7DaysIn) =>
        ScoringMath.LinearBetween(newSnowLast7DaysIn, goodValue: 0, badValue: 4);

    public static string Detail(double newSnowLast7DaysIn) =>
        $"{newSnowLast7DaysIn:0.0}\" new snow in last 7 days";

    public static (Grade? Cap, string Reason) Cap(double newSnowLast7DaysIn)
    {
        if (newSnowLast7DaysIn > 8) return (Grade.D, $"{newSnowLast7DaysIn:0.0}\" new snow on rock");
        if (newSnowLast7DaysIn > 4) return (Grade.C, $"{newSnowLast7DaysIn:0.0}\" new snow on rock");
        if (newSnowLast7DaysIn > 2) return (Grade.B, $"{newSnowLast7DaysIn:0.0}\" new snow on rock");
        return (null, string.Empty);
    }
}
