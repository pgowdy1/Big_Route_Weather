using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class ThunderstormFactor
{
    public const double Weight = 0.20;

    // Calibrated for marine-influenced ranges where CAPE rarely exceeds ~1500 J/kg;
    // deliberately lower than Plains-storm conventions.
    public const double ActiveFloorJkg = 200;

    public static bool IsActive(double maxCapeJkg) => maxCapeJkg >= ActiveFloorJkg;

    public static int Score(double maxCapeJkg) =>
        ScoringMath.LinearBetween(maxCapeJkg, goodValue: 200, badValue: 2000);

    public static string Detail(double maxCapeJkg) =>
        $"Peak instability {maxCapeJkg:0} J/kg CAPE";

    public static (Grade? Cap, string Reason) Cap(double maxCapeJkg)
    {
        if (maxCapeJkg >= 2000) return (Grade.D, $"storm energy {maxCapeJkg:0} J/kg");
        if (maxCapeJkg >= 1000) return (Grade.C, $"storm energy {maxCapeJkg:0} J/kg");
        return (null, string.Empty);
    }
}
