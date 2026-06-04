namespace RouteWeather.Core.Sources;

public static class ForecastFactors
{
    public const string Wind = "Wind";
    public const string Temperature = "Temperature";
    public const string Precipitation = "Precipitation";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Wind, Temperature, Precipitation,
    };

    public static readonly IReadOnlySet<string> WindAndTemperatureOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Wind, Temperature,
    };
}
