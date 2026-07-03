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
    public static GradeResult Compute(WeatherSnapshot? weather, SnowpackSnapshot? snowpack, AirQualitySnapshot? airQuality = null)
    {
        var factors = new List<FactorScore>();
        var capCandidates = new List<(Grade Cap, string Reason, string FactorName)>();
        var snow = SnowRelevance.Evaluate(weather, snowpack);

        if (weather is not null)
        {
            factors.Add(new FactorScore(
                "Wind",
                WindFactor.Score(weather.WindMph),
                WindFactor.Weight,
                WindFactor.Detail(weather.WindMph)));
            AddCap(capCandidates, "Wind", WindFactor.Cap(weather.WindMph));

            factors.Add(new FactorScore(
                "Temperature",
                TemperatureFactor.Score(weather.TempF),
                TemperatureFactor.Weight,
                TemperatureFactor.Detail(weather.TempF)));
            AddCap(capCandidates, "Temperature", TemperatureFactor.Cap(weather.TempF));

            var windowHours = weather.Next48Hours.Count;
            factors.Add(new FactorScore(
                "Precipitation",
                PrecipitationFactor.Score(weather.PrecipitationProbabilityPct, weather.PrecipAmountIn, windowHours),
                PrecipitationFactor.Weight,
                PrecipitationFactor.Detail(weather.PrecipitationProbabilityPct, weather.PrecipAmountIn)));
            AddCap(capCandidates, "Precipitation", PrecipitationFactor.Cap(weather.PrecipitationProbabilityPct));

            if (weather.MaxCapeJkg is double cape)
            {
                var capeActive = ThunderstormFactor.IsActive(cape);
                factors.Add(new FactorScore(
                    "Thunderstorm",
                    ThunderstormFactor.Score(cape),
                    ThunderstormFactor.Weight,
                    capeActive ? ThunderstormFactor.Detail(cape) : ThunderstormFactor.InactiveDetail,
                    IsActive: capeActive));
                if (capeActive)
                    AddCap(capCandidates, "Thunderstorm", ThunderstormFactor.Cap(cape));
            }

            if (weather.MaxGustMph is double gust)
            {
                var gustActive = GustFactor.IsActive(gust);
                factors.Add(new FactorScore(
                    "Gusts",
                    GustFactor.Score(gust),
                    GustFactor.Weight,
                    gustActive ? GustFactor.Detail(gust) : GustFactor.InactiveDetail,
                    IsActive: gustActive));
                if (gustActive)
                    AddCap(capCandidates, "Gusts", GustFactor.Cap(gust));
            }
        }

        if (snowpack is not null)
        {
            factors.Add(new FactorScore(
                "Recent snow",
                RecentSnowFactor.Score(snowpack.NewSnowLast7DaysIn),
                RecentSnowFactor.Weight,
                RecentSnowFactor.Detail(snowpack.NewSnowLast7DaysIn),
                IsActive: snow.RecentSnowActive));
            if (snow.RecentSnowActive)
                AddCap(capCandidates, "Recent snow", RecentSnowFactor.Cap(snowpack.NewSnowLast7DaysIn));

            factors.Add(new FactorScore(
                "Snowpack",
                SnowpackFactor.Score(snowpack.PercentOfNormalSwe),
                SnowpackFactor.Weight,
                SnowpackFactor.Detail(snowpack.SnowWaterEquivalentIn, snowpack.PercentOfNormalSwe),
                IsActive: snow.SnowpackActive));
        }

        // AQI is a grade modifier, never a standalone grade: only fold it in once
        // at least one weather/snowpack factor exists (factors.Count > 0). Silent
        // below 101, so no card and no drag on clean/moderate air.
        if (airQuality is not null && factors.Count > 0 && AirQualityFactor.IsActive(airQuality.UsAqi))
        {
            factors.Add(new FactorScore(
                "Air quality",
                AirQualityFactor.Score(airQuality.UsAqi),
                AirQualityFactor.Weight,
                AirQualityFactor.Detail(airQuality.UsAqi)));
            AddCap(capCandidates, "Air quality", AirQualityFactor.Cap(airQuality.UsAqi));
        }

        var activeFactors = factors.Where(f => f.IsActive).ToList();
        if (activeFactors.Count == 0)
        {
            var emptyRationale = factors.Count == 0
                ? "No weather or snowpack data available."
                : "No active factors to grade.";
            return new GradeResult(Models.Grade.F, 0, factors, Array.Empty<Driver>(), emptyRationale);
        }

        var totalWeight = activeFactors.Sum(f => f.Weight);
        var weighted = activeFactors.Sum(f => f.Score * f.Weight);
        var overallScore = (int)Math.Round(weighted / totalWeight);
        var naturalGrade = GradeMapping.FromScore(overallScore);

        var worstCap = capCandidates.Count == 0
            ? default((Grade Cap, string Reason, string FactorName)?)
            : capCandidates.OrderByDescending(c => (int)c.Cap).First();

        var capApplied = worstCap.HasValue && (int)worstCap.Value.Cap > (int)naturalGrade;
        var grade = capApplied ? worstCap!.Value.Cap : naturalGrade;

        var drivers = BuildDrivers(activeFactors, capApplied ? worstCap : null);
        var rationale = capApplied
            ? $"Capped at {worstCap!.Value.Cap} — {worstCap.Value.Reason}. {BuildRationale(grade, activeFactors)}"
            : BuildRationale(grade, activeFactors);

        return new GradeResult(grade, overallScore, factors, drivers, rationale);
    }

    private static void AddCap(
        List<(Grade Cap, string Reason, string FactorName)> list,
        string factorName,
        (Grade? Cap, string Reason) result)
    {
        if (result.Cap.HasValue)
            list.Add((result.Cap.Value, result.Reason, factorName));
    }

    private static IReadOnlyList<Driver> BuildDrivers(
        IReadOnlyList<FactorScore> factors,
        (Grade Cap, string Reason, string FactorName)? appliedCap)
    {
        static string SeverityFor(int score) =>
            score <= 50 ? "negative" : score >= 85 ? "positive" : "neutral";

        var negatives = factors.Where(f => f.Score <= 50).OrderBy(f => f.Score).ToList();
        var positives = factors.Where(f => f.Score >= 85).OrderByDescending(f => f.Score).ToList();
        var neutrals  = factors.Where(f => f.Score > 50 && f.Score < 85).OrderBy(f => f.Score).ToList();

        var ordered = negatives.Concat(neutrals).Concat(positives)
            .Take(3)
            .Select(f => new Driver(LabelFor(f, SeverityFor(f.Score)), SeverityFor(f.Score)))
            .ToList();

        if (appliedCap is null) return ordered;

        var capFactor = factors.FirstOrDefault(f => f.Name == appliedCap.Value.FactorName);
        if (capFactor is null) return ordered;

        // Remove the cap factor's existing driver in ANY severity form before
        // forcing it to the front as a negative. A three-label factor (AQI,
        // Thunderstorm, Gusts) can sit in its neutral band while still capping the
        // grade; matching only negative/positive would leave the neutral driver in
        // place and list the same factor twice with conflicting severities.
        ordered.RemoveAll(d =>
            d.Label == LabelFor(capFactor, "negative") ||
            d.Label == LabelFor(capFactor, "neutral") ||
            d.Label == LabelFor(capFactor, "positive"));
        ordered.Insert(0, new Driver(LabelFor(capFactor, "negative"), "negative"));
        if (ordered.Count > 3) ordered.RemoveAt(ordered.Count - 1);
        return ordered;
    }

    private static string LabelFor(FactorScore f, string severity) => f.Name switch
    {
        "Wind" => severity == "negative" ? "High winds" : "Calm winds",
        "Temperature" => severity == "negative" ? "Extreme temps" : "Comfortable temps",
        "Precipitation" => severity == "negative" ? "Wet weather" : "Clear skies",
        "Recent snow" => severity == "negative" ? "Fresh snow on rock" : "Dry rock",
        "Snowpack" => severity == "negative" ? "Out-of-season snowpack" : "Typical snowpack",
        "Thunderstorm" => severity == "negative" ? "Storm risk" : severity == "neutral" ? "Some instability" : "Low storm risk",
        "Gusts" => severity == "negative" ? "Strong gusts" : severity == "neutral" ? "Gusty" : "Manageable gusts",
        // "Clean air" is unreachable — an active AQI factor always scores <= 80 (< the 85 positive floor); kept for switch symmetry.
        "Air quality" => severity == "negative" ? "Poor air quality" : severity == "neutral" ? "Reduced air quality" : "Clean air",
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
