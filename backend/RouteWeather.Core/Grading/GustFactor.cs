using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class GustFactor
{
    public const double Weight = 0.10;

    // Below this, gusts are noise around sustained wind — an always-on twin of
    // WindFactor would permanently dilute every other weight.
    public const double ActiveFloorMph = 25;

    public static bool IsActive(double maxGustMph) => maxGustMph >= ActiveFloorMph;

    public static int Score(double maxGustMph) =>
        ScoringMath.LinearBetween(maxGustMph, goodValue: ActiveFloorMph, badValue: 55);

    public static string Detail(double maxGustMph) =>
        $"Gusts to {maxGustMph:0} mph";

    public static (Grade? Cap, string Reason) Cap(double maxGustMph)
    {
        if (maxGustMph > 70) return (Grade.F, $"gusts to {maxGustMph:0} mph");
        if (maxGustMph > 55) return (Grade.D, $"gusts to {maxGustMph:0} mph");
        if (maxGustMph > 45) return (Grade.C, $"gusts to {maxGustMph:0} mph");
        return (null, string.Empty);
    }
}
