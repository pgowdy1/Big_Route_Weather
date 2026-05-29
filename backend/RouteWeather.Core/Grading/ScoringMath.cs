namespace RouteWeather.Core.Grading;

internal static class ScoringMath
{
    public static int Clamp(double v) => (int)Math.Round(Math.Clamp(v, 0, 100));

    public static int LinearBetween(double value, double goodValue, double badValue)
    {
        if (goodValue == badValue) return value <= goodValue ? 100 : 0;
        var t = (value - goodValue) / (badValue - goodValue);
        return Clamp(100 * (1 - t));
    }
}
