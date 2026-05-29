namespace RouteWeather.Core.Models;

public record Driver(string Label, string Severity);

public record RouteConditions(
    Route Route,
    Grade? Grade,
    int? OverallScore,
    IReadOnlyList<Driver> Drivers,
    IReadOnlyList<FactorScore> Factors,
    string Rationale,
    DateTimeOffset UpdatedAt,
    bool IsStale,
    WeatherSnapshot? Weather,
    SnowpackSnapshot? Snowpack
);
