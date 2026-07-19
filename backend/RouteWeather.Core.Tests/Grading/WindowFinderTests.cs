using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class WindowFinderTests
{
    // Mt Baker-ish coords; July UTC sunrise ≈ 12:30Z, sunset ≈ 04:10Z (next UTC day),
    // so each climbing frame is roughly [06:30Z, 04:10Z next day].
    private const double Lat = 48.777;
    private const double Lon = -121.813;
    // 18:00Z (11 am PDT) — deliberately mid-frame, so the 168h horizon cuts the final
    // frame short and the "runs to the forecast edge" end-reason is reachable.
    private static readonly DateTimeOffset T0 = new(2026, 7, 20, 18, 0, 0, TimeSpan.Zero);

    private static HourlyForecast Good(DateTimeOffset t) =>
        new(t, TempF: 45, WindMph: 8, PrecipitationProbabilityPct: 5, "Clear",
            GustMph: 12, CapeJkg: 50, PrecipitationIn: 0.0);

    // Precip prob kept moderate (30) so Thunderstorm is unambiguously the worst factor
    // and the end-reason assertion can't flip to the precip phrase.
    private static HourlyForecast Stormy(DateTimeOffset t) =>
        new(t, TempF: 45, WindMph: 10, PrecipitationProbabilityPct: 30, "Thunderstorm",
            GustMph: 20, CapeJkg: 2600, PrecipitationIn: 0.05);

    private static HourlyForecast Windy(DateTimeOffset t) =>
        new(t, TempF: 40, WindMph: 45, PrecipitationProbabilityPct: 5, "Windy",
            GustMph: 65, CapeJkg: 50, PrecipitationIn: 0.0);

    /// Build a 168h snapshot from a per-index chooser.
    private static WeatherSnapshot Snap(Func<int, DateTimeOffset, HourlyForecast> hour)
    {
        var series = Enumerable.Range(0, 168).Select(i => hour(i, T0.AddHours(i))).ToList();
        var head = series.Take(48).ToList();
        return new WeatherSnapshot(
            head.Max(h => h.WindMph), head.Min(h => h.TempF), head.Max(h => h.PrecipitationProbabilityPct),
            series,
            head.Max(h => h.GustMph!.Value), head.Max(h => h.CapeJkg!.Value), head.Sum(h => h.PrecipitationIn!.Value));
    }

    private static IReadOnlyList<ClimbWindow> Find(WeatherSnapshot snap, double typicalHours = 8) =>
        WindowFinder.Find(snap, snowpack: null, airQuality: null, typicalHours, Lat, Lon);

    [Fact]
    public void All_good_week_yields_one_window_per_day_clipped_to_climbing_frames()
    {
        var windows = Find(Snap((_, t) => Good(t)));

        Assert.InRange(windows.Count, 6, 8);          // one per climbing day in the horizon
        Assert.All(windows, w => Assert.True(w.EndUtc - w.StartUtc >= TimeSpan.FromHours(8)));
        Assert.All(windows, w => Assert.Equal(Grade.A, w.Grade));
        // Chronological order.
        Assert.Equal(windows.OrderBy(w => w.StartUtc).Select(w => w.StartUtc), windows.Select(w => w.StartUtc));
        // Every window is clipped to one climbing day.
        Assert.All(windows, w => Assert.True((w.EndUtc - w.StartUtc) <= TimeSpan.FromHours(24)));
    }

    [Fact]
    public void Storm_hours_do_not_qualify_and_split_the_day()
    {
        // Storms from hour 30 onward kill every later day; only the early windows survive.
        var windows = Find(Snap((i, t) => i < 30 ? Good(t) : Stormy(t)));

        Assert.NotEmpty(windows);
        Assert.All(windows, w => Assert.True(w.EndUtc <= T0.AddHours(31)));
    }

    [Fact]
    public void Windows_shorter_than_typical_climb_hours_are_dropped()
    {
        var all = Find(Snap((_, t) => Good(t)), typicalHours: 8);
        var strict = Find(Snap((_, t) => Good(t)), typicalHours: 22);

        Assert.NotEmpty(all);
        Assert.Empty(strict);                          // no frame is 22h long
    }

    [Fact]
    public void Night_only_good_runs_never_become_windows()
    {
        // Good ONLY in the nightly gap between frames (frame = [sunrise-6h ≈ 06:30Z, sunset ≈ 04:10Z
        // next day]; gap ≈ [04:10Z, 06:30Z]). Good 04:30Z–06:30Z daily, Windy otherwise.
        var windows = Find(Snap((_, t) =>
        {
            var frameStart = new DateTimeOffset(t.Year, t.Month, t.Day, 6, 30, 0, TimeSpan.Zero);
            var inSlack = t >= frameStart.AddHours(-2) && t < frameStart;
            return inSlack ? Good(t) : Windy(t);
        }), typicalHours: 2);

        Assert.Empty(windows);
    }

    [Fact]
    public void End_reason_names_the_disqualifying_factor()
    {
        // Good until hour 32, storms after.
        var windows = Find(Snap((i, t) => i < 32 ? Good(t) : Stormy(t)));

        var last = windows.Last();
        Assert.Equal("closes as storm energy builds", last.EndReason);
    }

    [Fact]
    public void End_reason_is_daylight_when_the_run_continues_past_sunset()
    {
        var windows = Find(Snap((_, t) => Good(t)));

        // Interior windows are clipped by their frame while hours stay good.
        Assert.Contains(windows, w => w.EndReason == "ends with daylight");
    }

    [Fact]
    public void End_reason_is_forecast_edge_at_the_horizon()
    {
        var windows = Find(Snap((_, t) => Good(t)));
        Assert.Equal("runs to the forecast edge", windows.Last().EndReason);
    }

    [Fact]
    public void Low_confidence_flags_windows_with_midpoint_past_96h()
    {
        var windows = Find(Snap((_, t) => Good(t)));

        Assert.Contains(windows, w => !w.LowConfidence);
        Assert.Contains(windows, w => w.LowConfidence);
        Assert.All(windows, w =>
        {
            var mid = w.StartUtc + (w.EndUtc - w.StartUtc) / 2;
            Assert.Equal(mid > T0.AddHours(96), w.LowConfidence);
        });
    }

    [Fact]
    public void Score_hours_qualify_only_at_post_cap_grade_B_or_better()
    {
        var snap = Snap((i, t) => i == 10 ? Stormy(t) : Good(t));
        var scored = WindowFinder.ScoreHours(snap, snowpack: null, airQuality: null);

        Assert.Equal(168, scored.Count);
        Assert.False(scored[10].Qualifies);            // capped by thunderstorm
        Assert.True(scored[11].Qualifies);
    }

    [Fact]
    public void Null_weather_returns_empty()
    {
        Assert.Empty(WindowFinder.Find(null, null, null, 8, Lat, Lon));
        Assert.Empty(WindowFinder.ScoreHours(null, null, null));
    }

    [Fact]
    public void Mid_series_gap_splits_a_run()
    {
        // A 3-hour hole in the data mid-frame must break the qualifying run so no window
        // straddles it, with a window landing on each side.
        var gapStart = new DateTimeOffset(2026, 7, 21, 15, 0, 0, TimeSpan.Zero); // T0 + 21h
        var gapEnd = gapStart.AddHours(3);

        var series = Enumerable.Range(0, 168)
            .Select(i => T0.AddHours(i))
            .Where(t => t < gapStart || t >= gapEnd)   // omit the 3 gap hours
            .Select(Good)
            .ToList();
        var head = series.Take(48).ToList();
        var snap = new WeatherSnapshot(
            head.Max(h => h.WindMph), head.Min(h => h.TempF), head.Max(h => h.PrecipitationProbabilityPct),
            series,
            head.Max(h => h.GustMph!.Value), head.Max(h => h.CapeJkg!.Value), head.Sum(h => h.PrecipitationIn!.Value));

        var windows = WindowFinder.Find(snap, snowpack: null, airQuality: null, 2, Lat, Lon);

        Assert.NotEmpty(windows);
        Assert.All(windows, w => Assert.True(w.EndUtc <= gapStart || w.StartUtc >= gapEnd,
            $"window [{w.StartUtc:o},{w.EndUtc:o}] straddles the gap [{gapStart:o},{gapEnd:o}]"));
        Assert.Contains(windows, w => w.EndUtc <= gapStart);
        Assert.Contains(windows, w => w.StartUtc >= gapEnd);
    }

    [Fact]
    public void Zero_typical_climb_hours_returns_empty()
    {
        Assert.Empty(Find(Snap((_, t) => Good(t)), typicalHours: 0));
    }

    [Fact]
    public void Frames_never_overlap_at_high_latitude()
    {
        // Lat 63° in July: the night is shorter than the 6h alpine-start lead, so raw frames
        // would overlap. The clamp must keep the returned windows non-overlapping.
        var windows = WindowFinder.Find(Snap((_, t) => Good(t)), snowpack: null, airQuality: null,
            typicalClimbHours: 8, lat: 63.0, lon: Lon);

        Assert.NotEmpty(windows);
        for (var i = 1; i < windows.Count; i++)
            Assert.True(windows[i].StartUtc >= windows[i - 1].EndUtc,
                $"window {i} starts {windows[i].StartUtc:o} before previous end {windows[i - 1].EndUtc:o}");
    }
}
