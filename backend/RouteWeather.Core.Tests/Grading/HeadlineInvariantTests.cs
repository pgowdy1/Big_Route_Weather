using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class HeadlineInvariantTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 20, 6, 0, 0, TimeSpan.Zero);

    /// Varied-but-plausible series: benign first 48h, deteriorating back half —
    /// exactly the case where a leak past 48h would change the headline.
    /// lateSnow additionally makes hours 96+ read as snow showers, to exercise
    /// the SnowRelevance headline gate.
    private static List<HourlyForecast> Series(int hours, bool lateSnow = false) =>
        Enumerable.Range(0, hours).Select(i => new HourlyForecast(
            Time: T0.AddHours(i),
            TempF: i < 48 ? 45 : 20,
            WindMph: i < 48 ? 8 : 45,
            PrecipitationProbabilityPct: i < 48 ? 10 : (lateSnow && i >= 96 ? 80 : 90),
            ShortForecast: lateSnow && i >= 96 ? "Snow showers" : "Test",
            GustMph: i < 48 ? 12 : 70,
            CapeJkg: i < 48 ? 100 : 2500,
            PrecipitationIn: i < 48 ? 0.0 : 0.2)).ToList();

    private static WeatherSnapshot Snapshot(int hours, bool lateSnow = false)
    {
        var series = Series(hours, lateSnow);
        var head = series.Where(h => h.Time < T0.AddHours(WeatherSnapshot.HeadlineHours)).ToList();
        return new WeatherSnapshot(
            WindMph: head.Max(h => h.WindMph),
            TempF: head.Min(h => h.TempF),
            PrecipitationProbabilityPct: head.Max(h => h.PrecipitationProbabilityPct),
            Hourly: series,
            MaxGustMph: head.Max(h => h.GustMph!.Value),
            MaxCapeJkg: head.Max(h => h.CapeJkg!.Value),
            PrecipAmountIn: head.Sum(h => h.PrecipitationIn!.Value));
    }

    // No recent SNOTEL snow (NewSnowLast7DaysIn = 0) is what makes the IsSnowExpected head-gate
    // observable: RecentSnowActive = HasRecentSnow(snowpack) || IsSnowExpected(weather), so with
    // HasRecentSnow false the forecast gate alone drives RecentSnow activity. Snow is on the
    // ground (SnowDepthIn = 30) so the Snowpack factor stays active and present in the list.
    // (A non-zero NewSnowLast7Days would saturate the OR to true and mask the gate entirely.)
    private static SnowpackSnapshot MakeSnowpack() => new(
        SnowWaterEquivalentIn: 10.0,
        SnowDepthIn: 30.0,
        NewSnowLast7DaysIn: 0.0,
        PercentOfNormalSwe: 100.0,
        StationTriplet: "TEST:CO:SNTL",
        DailyDepthIn: Array.Empty<DailyDepthPoint>());

    [Fact]
    public void Headline_grade_is_identical_for_168h_series_and_its_48h_twin()
    {
        var full = GradeCalculator.Compute(Snapshot(168), snowpack: null);
        var twin = GradeCalculator.Compute(Snapshot(48), snowpack: null);

        Assert.Equal(twin.Grade, full.Grade);
        Assert.Equal(twin.OverallScore, full.OverallScore);
        Assert.Equal(twin.Rationale, full.Rationale);
        Assert.Equal(
            twin.Factors.Select(f => (f.Name, f.Score, f.Detail)),
            full.Factors.Select(f => (f.Name, f.Score, f.Detail)));
    }

    [Fact]
    public void Window_grades_are_identical_for_168h_series_and_its_48h_twin()
    {
        var full = WindowGradeCalculator.Compute(Snapshot(168), snowpack: null);
        var twin = WindowGradeCalculator.Compute(Snapshot(48), snowpack: null);

        foreach (var (f, t) in new[] { (full.Next12h, twin.Next12h), (full.Next24h, twin.Next24h), (full.Next48h, twin.Next48h) })
        {
            Assert.Equal(t.Grade, f.Grade);
            Assert.Equal(t.OverallScore, f.OverallScore);
            Assert.Equal(t.HoursCovered, f.HoursCovered);
        }
    }

    [Fact]
    public void Blended_consensus_scalars_are_identical_for_168h_sources_and_their_48h_twins()
    {
        EnsembleResult Run(int hours)
        {
            var inputs = new[]
            {
                new ConsensusInput(new SourceSnapshot("A", Snapshot(hours), T0, ForecastFactors.All), 1.75),
                new ConsensusInput(new SourceSnapshot("B", Snapshot(hours), T0, ForecastFactors.All), 1.0),
            };
            return new ConsensusCalculator().Compute(inputs, 2);
        }

        var full = Run(168).Blended!;
        var twin = Run(48).Blended!;

        Assert.Equal(twin.WindMph, full.WindMph);
        Assert.Equal(twin.TempF, full.TempF);
        Assert.Equal(twin.PrecipitationProbabilityPct, full.PrecipitationProbabilityPct);
        Assert.Equal(twin.MaxGustMph, full.MaxGustMph);
        Assert.Equal(twin.MaxCapeJkg, full.MaxCapeJkg);
        Assert.Equal(twin.PrecipAmountIn, full.PrecipAmountIn);
    }

    [Fact]
    public void Snow_relevance_gating_is_identical_for_168h_series_and_its_48h_twin()
    {
        // Fresh SNOTEL snow + snowy forecast hours ONLY on days 5–7: if IsSnowExpected
        // scanned past the headline window, RecentSnow/Snowpack active-state (and with it
        // the factor list) would flip relative to the twin.
        var snowpack = MakeSnowpack();

        var full = GradeCalculator.Compute(Snapshot(168, lateSnow: true), snowpack);
        var twin = GradeCalculator.Compute(Snapshot(48), snowpack);

        Assert.Equal(
            twin.Factors.Select(f => (f.Name, f.Score, f.IsActive)),
            full.Factors.Select(f => (f.Name, f.Score, f.IsActive)));
        Assert.Equal(twin.Grade, full.Grade);
        Assert.Equal(twin.OverallScore, full.OverallScore);
    }
}
