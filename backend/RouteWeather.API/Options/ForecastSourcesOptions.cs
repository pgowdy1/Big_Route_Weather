namespace RouteWeather.API.Options;

public class ForecastSourcesOptions
{
    public ConsensusThresholdsOptions ConsensusThresholds { get; set; } = new();
    public List<SourceOptions> Sources { get; set; } = new();

    public SourceOptions? For(string name) =>
        Sources.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool IsEnabled(string name) => For(name)?.Enabled ?? true;

    public double WeightFor(string name) => For(name)?.Weight ?? 1.0;

    public TimeSpan TtlFor(string name)
    {
        var minutes = For(name)?.CacheTtlMinutes ?? 60;
        return TimeSpan.FromMinutes(minutes);
    }
}

public class SourceOptions
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public double Weight { get; set; } = 1.0;
    public int CacheTtlMinutes { get; set; } = 60;
}

public class ConsensusThresholdsOptions
{
    public double HighMaxCv { get; set; } = 0.15;
    public double MediumMaxCv { get; set; } = 0.35;
}
