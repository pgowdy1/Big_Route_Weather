namespace RouteWeather.Core.Grading;

public static class RecentSnowFactor
{
    public const double Weight = 0.20;

    public static int Score(double newSnowLast7DaysIn) =>
        ScoringMath.LinearBetween(newSnowLast7DaysIn, goodValue: 0, badValue: 6);

    public static string Detail(double newSnowLast7DaysIn) =>
        $"{newSnowLast7DaysIn:0.0}\" new snow in last 7 days";
}
