namespace RouteWeather.Core.Sources;

public static class ForecastFactors
{
    public const string Wind = "Wind";
    public const string Temperature = "Temperature";
    public const string Precipitation = "Precipitation";

    // Names match FactorScore display names so ConsensusReport.WorstFactor aligns with UI
    // factor names. These are NOT ActiveFactors-gated — they blend / report on value presence.
    public const string Gust = "Gusts";
    public const string Cape = "Thunderstorm";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Wind, Temperature, Precipitation,
    };

    public static readonly IReadOnlySet<string> WindAndTemperatureOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Wind, Temperature,
    };
}
