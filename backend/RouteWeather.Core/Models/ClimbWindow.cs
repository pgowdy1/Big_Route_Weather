namespace RouteWeather.Core.Models;

/// <summary>
/// A contiguous stretch of climbable hours, clipped to one climbing day
/// (sunrise − 6h → sunset) and long enough for the route's typical summit-day push.
/// EndReason is a display-ready clause: "closes as storm energy builds",
/// "ends with daylight", "runs to the forecast edge".
/// </summary>
public record ClimbWindow(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    Grade Grade,
    int Score,
    string EndReason,
    bool LowConfidence);

/// <summary>Per-hour quality for the week strip. Score/grade come from the same factor machinery as the headline.</summary>
public record HourlyQuality(
    DateTimeOffset TimeUtc,
    int Score,
    bool Qualifies);
