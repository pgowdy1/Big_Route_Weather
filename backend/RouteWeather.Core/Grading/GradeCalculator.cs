using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public record GradeResult(
    Grade Grade,
    int OverallScore,
    IReadOnlyList<FactorScore> Factors,
    IReadOnlyList<Driver> Drivers,
    string Rationale
);

public static class GradeCalculator
{
    public static GradeResult Compute(WeatherSnapshot? weather, SnowpackSnapshot? snowpack)
    {
        var factors = new List<FactorScore>();

        if (weather is not null)
        {
            factors.Add(new FactorScore(
                "Wind",
                WindFactor.Score(weather.WindMph),
                WindFactor.Weight,
                WindFactor.Detail(weather.WindMph)));

            factors.Add(new FactorScore(
                "Temperature",
                TemperatureFactor.Score(weather.TempF),
                TemperatureFactor.Weight,
                TemperatureFactor.Detail(weather.TempF)));

            factors.Add(new FactorScore(
                "Precipitation",
                PrecipitationFactor.Score(weather.PrecipitationProbabilityPct),
                PrecipitationFactor.Weight,
                PrecipitationFactor.Detail(weather.PrecipitationProbabilityPct)));
        }

        if (snowpack is not null)
        {
            factors.Add(new FactorScore(
                "Recent snow",
                RecentSnowFactor.Score(snowpack.NewSnowLast7DaysIn),
                RecentSnowFactor.Weight,
                RecentSnowFactor.Detail(snowpack.NewSnowLast7DaysIn)));

            factors.Add(new FactorScore(
                "Snowpack",
                SnowpackFactor.Score(snowpack.PercentOfNormalSwe),
                SnowpackFactor.Weight,
                SnowpackFactor.Detail(snowpack.SnowWaterEquivalentIn, snowpack.PercentOfNormalSwe)));
        }

        if (factors.Count == 0)
        {
            return new GradeResult(
                Models.Grade.F,
                0,
                Array.Empty<FactorScore>(),
                Array.Empty<Driver>(),
                "No weather or snowpack data available.");
        }

        var totalWeight = factors.Sum(f => f.Weight);
        var weighted = factors.Sum(f => f.Score * f.Weight);
        var overallScore = (int)Math.Round(weighted / totalWeight);
        var grade = GradeMapping.FromScore(overallScore);

        var drivers = BuildDrivers(factors);
        var rationale = BuildRationale(grade, factors);

        return new GradeResult(grade, overallScore, factors, drivers, rationale);
    }

    private static IReadOnlyList<Driver> BuildDrivers(IReadOnlyList<FactorScore> factors)
    {
        static string SeverityFor(int score) =>
            score <= 50 ? "negative" : score >= 85 ? "positive" : "neutral";

        var negatives = factors.Where(f => f.Score <= 50).OrderBy(f => f.Score).ToList();
        var positives = factors.Where(f => f.Score >= 85).OrderByDescending(f => f.Score).ToList();
        var neutrals  = factors.Where(f => f.Score > 50 && f.Score < 85).OrderBy(f => f.Score).ToList();

        return negatives.Concat(neutrals).Concat(positives)
            .Take(3)
            .Select(f => new Driver(LabelFor(f, SeverityFor(f.Score)), SeverityFor(f.Score)))
            .ToList();
    }

    private static string LabelFor(FactorScore f, string severity) => f.Name switch
    {
        "Wind" => severity == "negative" ? "High winds" : "Calm winds",
        "Temperature" => severity == "negative" ? "Extreme temps" : "Comfortable temps",
        "Precipitation" => severity == "negative" ? "Wet weather" : "Clear skies",
        "Recent snow" => severity == "negative" ? "Fresh snow on rock" : "Dry rock",
        "Snowpack" => severity == "negative" ? "Out-of-season snowpack" : "Typical snowpack",
        _ => f.Name,
    };

    private static string BuildRationale(Grade grade, IReadOnlyList<FactorScore> factors)
    {
        var worst = factors.OrderBy(f => f.Score).First();
        return grade switch
        {
            Models.Grade.A => "Excellent conditions across the board. Send it.",
            Models.Grade.B => $"Solid day overall — keep an eye on: {worst.Detail.ToLowerInvariant()}.",
            Models.Grade.C => $"Mixed conditions. {worst.Name} is the limiting factor ({worst.Detail.ToLowerInvariant()}). Plan an early turnaround.",
            Models.Grade.D => $"Marginal. {worst.Name} is poor ({worst.Detail.ToLowerInvariant()}). Consider postponing.",
            _ => $"Bad day to be high. Driving issue: {worst.Detail.ToLowerInvariant()}.",
        };
    }
}
