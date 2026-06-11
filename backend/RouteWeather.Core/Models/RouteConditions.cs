namespace RouteWeather.Core.Models;

public record Driver(string Label, string Severity);

public record WindowGrade(
    Grade? Grade,
    int? OverallScore,
    int HoursCovered,
    IReadOnlyList<FactorScore> Factors,
    IReadOnlyList<Driver> Drivers,
    string Rationale
);

public record WindowGrades(
    WindowGrade Next12h,
    WindowGrade Next24h,
    WindowGrade Next48h
);

public record SourceFreshness(DateTimeOffset? NwsFetchedAt, DateTimeOffset? SnotelFetchedAt, DateTimeOffset? AirQualityFetchedAt = null);

public record PerSourceForecast(
    string SourceName,
    double WindMph,
    double TempF,
    int? PrecipitationProbabilityPct,
    DateTimeOffset FetchedAt,
    double? MaxGustMph = null,
    double? MaxCapeJkg = null
);

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
    SnowpackSnapshot? Snowpack,
    WindowGrades? WindowGrades,
    SourceFreshness Sources,
    ConsensusReport? Consensus,
    IReadOnlyList<PerSourceForecast>? PerSourceForecast,
    AirQualitySnapshot? AirQuality = null
);
