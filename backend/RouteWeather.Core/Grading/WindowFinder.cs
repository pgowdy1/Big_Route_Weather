using RouteWeather.Core.Models;
using RouteWeather.Core.Services;

namespace RouteWeather.Core.Grading;

/// <summary>
/// Finds climbable windows in a (up to 7-day) hourly series. Pure and clock-free:
/// "low confidence" is anchored to the series start, and callers filter
/// already-past windows at read time.
/// </summary>
public static class WindowFinder
{
    /// <summary>Hours before sunrise a climbing day starts (alpine-start slack).</summary>
    private const double FrameLeadHours = 6;
    /// <summary>Window midpoints beyond this many hours from series start are low-confidence.</summary>
    private const double ConfidenceHorizonHours = 96;

    public static IReadOnlyList<HourlyQuality> ScoreHours(
        WeatherSnapshot? weather, SnowpackSnapshot? snowpack, AirQualitySnapshot? airQuality)
    {
        if (weather is null || weather.Hourly.Count == 0) return Array.Empty<HourlyQuality>();

        var result = new List<HourlyQuality>(weather.Hourly.Count);
        foreach (var hour in weather.Hourly)
        {
            var single = WindowGradeCalculator.Aggregate(new[] { hour });
            var graded = GradeCalculator.Compute(single, snowpack, airQuality);
            // Post-cap grade folds both score and caps: B-or-better means "the site says go".
            var qualifies = graded.Grade is Grade.A or Grade.B;
            result.Add(new HourlyQuality(hour.Time, graded.OverallScore, qualifies));
        }
        return result;
    }

    public static IReadOnlyList<ClimbWindow> Find(
        WeatherSnapshot? weather,
        SnowpackSnapshot? snowpack,
        AirQualitySnapshot? airQuality,
        double typicalClimbHours,
        double lat,
        double lon)
        => Find(weather, snowpack, airQuality, typicalClimbHours, lat, lon,
            ScoreHours(weather, snowpack, airQuality));

    /// <summary>
    /// Overload for callers that already scored the series (e.g. the aggregator, which
    /// also surfaces the per-hour scores). Avoids a second full scoring pass.
    /// </summary>
    public static IReadOnlyList<ClimbWindow> Find(
        WeatherSnapshot? weather,
        SnowpackSnapshot? snowpack,
        AirQualitySnapshot? airQuality,
        double typicalClimbHours,
        double lat,
        double lon,
        IReadOnlyList<HourlyQuality> scored)
    {
        if (weather is null || weather.Hourly.Count == 0 || typicalClimbHours <= 0)
            return Array.Empty<ClimbWindow>();

        var hours = weather.Hourly;
        var seriesStart = hours[0].Time;
        var seriesEnd = hours[^1].Time.AddHours(1);

        var runs = QualifyingRuns(hours, scored);
        var frames = ClimbingFrames(lat, lon, seriesStart, seriesEnd);

        var windows = new List<ClimbWindow>();
        foreach (var frame in frames)
        {
            foreach (var run in runs)
            {
                var start = run.Start > frame.Start ? run.Start : frame.Start;
                var end = run.End < frame.End ? run.End : frame.End;
                if ((end - start).TotalHours < typicalClimbHours) continue;

                var window = BuildWindow(hours, snowpack, airQuality,
                    start, end, run, frame, seriesStart, seriesEnd);
                if (window is not null) windows.Add(window);
            }
        }
        return windows.OrderBy(w => w.StartUtc).ToList();
    }

    private sealed record Run(DateTimeOffset Start, DateTimeOffset End, int FirstIndex, int LastIndex);
    private sealed record Frame(DateTimeOffset Start, DateTimeOffset End);

    /// Contiguous stretches of qualifying hours. An hour h covers [h, h+1); a gap in
    /// the series (missing hour) breaks the run.
    private static List<Run> QualifyingRuns(IReadOnlyList<HourlyForecast> hours, IReadOnlyList<HourlyQuality> scored)
    {
        var runs = new List<Run>();
        int? runStart = null;
        for (var i = 0; i < hours.Count; i++)
        {
            // ±1-minute tolerance: sources timestamp hours with sub-second clock jitter,
            // so an exact 60-minute equality would read every pair as a gap.
            var brokeContinuity = i > 0 && Math.Abs((hours[i].Time - hours[i - 1].Time).TotalMinutes - 60) > 1;
            if (runStart is not null && (brokeContinuity || !scored[i].Qualifies))
            {
                runs.Add(MakeRun(hours, runStart.Value, i - 1));
                runStart = null;
            }
            if (scored[i].Qualifies && runStart is null) runStart = i;
        }
        if (runStart is not null) runs.Add(MakeRun(hours, runStart.Value, hours.Count - 1));
        return runs;
    }

    private static Run MakeRun(IReadOnlyList<HourlyForecast> hours, int first, int last) =>
        new(hours[first].Time, hours[last].Time.AddHours(1), first, last);

    /// One frame per UTC date the horizon touches: [sunrise − FrameLeadHours, sunset].
    private static List<Frame> ClimbingFrames(double lat, double lon, DateTimeOffset start, DateTimeOffset end)
    {
        var frames = new List<Frame>();
        for (var date = DateOnly.FromDateTime(start.UtcDateTime).AddDays(-1);
             date <= DateOnly.FromDateTime(end.UtcDateTime);
             date = date.AddDays(1))
        {
            var daylight = SolarCalculator.ComputeUtc(lat, lon, date);
            if (daylight is null) continue; // polar day/night
            var frame = new Frame(daylight.SunriseUtc.AddHours(-FrameLeadHours), daylight.SunsetUtc);
            // High-latitude clamp: when the night is shorter than the alpine-start lead,
            // this frame's start can precede the previous sunset. Clamp so frames never overlap.
            if (frames.Count > 0 && frame.Start < frames[^1].End)
                frame = frame with { Start = frames[^1].End };
            if (frame.End <= start || frame.Start >= end) continue;
            frames.Add(frame);
        }
        return frames;
    }

    private static ClimbWindow? BuildWindow(
        IReadOnlyList<HourlyForecast> hours,
        SnowpackSnapshot? snowpack,
        AirQualitySnapshot? airQuality,
        DateTimeOffset start,
        DateTimeOffset end,
        Run run,
        Frame frame,
        DateTimeOffset seriesStart,
        DateTimeOffset seriesEnd)
    {
        // Hours are inclusive-start, exclusive-end ([h, h+1) coverage): an hour stamped
        // exactly at `end` belongs to the next window, not this one.
        var slice = hours.Where(h => h.Time >= start && h.Time < end).ToList();
        if (slice.Count == 0) return null; // a clamped frame can leave no hours to grade
        var graded = GradeCalculator.Compute(WindowGradeCalculator.Aggregate(slice), snowpack, airQuality);

        var midpoint = start + (end - start) / 2;
        var lowConfidence = (midpoint - seriesStart).TotalHours > ConfidenceHorizonHours;

        return new ClimbWindow(start, end, graded.Grade, graded.OverallScore,
            EndReason(hours, snowpack, airQuality, end, run, frame, seriesEnd), lowConfidence);
    }

    private static string EndReason(
        IReadOnlyList<HourlyForecast> hours,
        SnowpackSnapshot? snowpack,
        AirQualitySnapshot? airQuality,
        DateTimeOffset end,
        Run run,
        Frame frame,
        DateTimeOffset seriesEnd)
    {
        // Clipped by the data horizon while still good. end = min(run.End, frame.End),
        // so end >= seriesEnd already implies the run itself reached the horizon.
        if (end >= seriesEnd) return "runs to the forecast edge";
        // Clipped by sunset while the run keeps going.
        if (end >= frame.End && run.End > frame.End) return "ends with daylight";

        // Otherwise the run itself ended: name the factor that broke it.
        var nextIndex = run.LastIndex + 1;
        if (nextIndex >= hours.Count) return "runs to the forecast edge";

        var single = WindowGradeCalculator.Aggregate(new[] { hours[nextIndex] });
        var graded = GradeCalculator.Compute(single, snowpack, airQuality);
        // Worst-scoring active factor. (A cap in a factor's neutral band can technically
        // out-vote it, but naming the worst factor is the right v1 message either way.)
        // Ties break toward the heavier factor so an hour that maxes out both storm energy
        // and precip names the storm — the headline hazard — over coincident precip.
        var culprit = graded.Factors.Where(f => f.IsActive)
            .OrderBy(f => f.Score).ThenByDescending(f => f.Weight).FirstOrDefault();
        return Phrase(culprit?.Name);
    }

    private static string Phrase(string? factorName) => factorName switch
    {
        "Thunderstorm" => "closes as storm energy builds",
        "Wind" or "Gusts" => "closes as wind picks up",
        "Precipitation" => "closes as precip moves in",
        "Temperature" => "closes as temps turn harsh",
        "Air quality" => "closes as smoke thickens",
        "Recent snow" => "closes on fresh snow",
        "Snowpack" => "closes on snowpack conditions",
        _ => "closes as conditions deteriorate",
    };
}
