using RouteWeather.Core.Models;

namespace RouteWeather.Core.Grading;

public static class WindowGradeCalculator
{
    public static WindowGrades Compute(WeatherSnapshot? weather, SnowpackSnapshot? snowpack)
    {
        return new WindowGrades(
            Next12h: GradeWindow(weather, snowpack, 12),
            Next24h: GradeWindow(weather, snowpack, 24),
            Next48h: GradeWindow(weather, snowpack, 48));
    }

    private static WindowGrade GradeWindow(WeatherSnapshot? weather, SnowpackSnapshot? snowpack, int hours)
    {
        var hourly = weather?.Next48Hours ?? Array.Empty<HourlyForecast>();
        var slice = hourly.Take(hours).ToList();
        var windowed = slice.Count == 0 ? null : Aggregate(slice);

        if (windowed is null && snowpack is null)
        {
            return new WindowGrade(
                Grade: null,
                OverallScore: null,
                HoursCovered: 0,
                Factors: Array.Empty<FactorScore>(),
                Drivers: Array.Empty<Driver>(),
                Rationale: "No forecast or snowpack data for this window.");
        }

        var result = GradeCalculator.Compute(windowed, snowpack);
        return new WindowGrade(
            Grade: result.Grade,
            OverallScore: result.OverallScore,
            HoursCovered: slice.Count,
            Factors: result.Factors,
            Drivers: result.Drivers,
            Rationale: result.Rationale);
    }

    private static WeatherSnapshot Aggregate(IReadOnlyList<HourlyForecast> slice)
    {
        var gusts = slice.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var capes = slice.Where(h => h.CapeJkg.HasValue).Select(h => h.CapeJkg!.Value).ToList();
        var amounts = slice.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new(
            WindMph: slice.Max(h => h.WindMph),
            TempF: slice.Min(h => h.TempF),
            PrecipitationProbabilityPct: slice.Max(h => h.PrecipitationProbabilityPct),
            Next48Hours: slice,
            MaxGustMph: gusts.Count == 0 ? null : gusts.Max(),
            MaxCapeJkg: capes.Count == 0 ? null : capes.Max(),
            PrecipAmountIn: amounts.Count == 0 ? null : amounts.Sum());
    }
}
