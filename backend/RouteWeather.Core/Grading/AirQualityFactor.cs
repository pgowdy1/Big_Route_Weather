using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class AirQualityFactor
{
    public const double Weight = 0.15;

    // US EPA AQI bands: 0-50 Good, 51-100 Moderate, 101-150 Unhealthy for
    // sensitive groups, 151-200 Unhealthy, 201-300 Very Unhealthy, 301+ Hazardous.
    // Silent until 101 — below that, air is not a real factor on a big objective,
    // and an always-on AQI factor would dilute every other weight.
    public const int ActiveFloorUsAqi = 101;

    public static bool IsActive(int usAqi) => usAqi >= ActiveFloorUsAqi;

    // goodValue 50 sits well below the 101 active floor, so the score is already
    // a drag (~80) the instant the factor activates — bad air can only lower the
    // grade, never inflate it, and it never reads as a positive driver.
    public static int Score(int usAqi) =>
        ScoringMath.LinearBetween(usAqi, goodValue: 50, badValue: 300);

    public static string Detail(int usAqi) => $"US AQI {usAqi}";

    public static (Grade? Cap, string Reason) Cap(int usAqi)
    {
        if (usAqi >= 201) return (Grade.F, $"air quality index {usAqi}");
        if (usAqi >= 151) return (Grade.C, $"air quality index {usAqi}");
        return (null, string.Empty);
    }
}
