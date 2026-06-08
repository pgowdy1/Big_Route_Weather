namespace RouteWeather.Core.Models;

public enum ConsensusLevel
{
    High,
    Medium,
    Low
}

public record ConsensusReport(
    ConsensusLevel Level,
    string? WorstFactor,
    IReadOnlyDictionary<string, double> CoefficientOfVariationByFactor,
    int SourcesReporting,
    int SourcesAttempted
);
