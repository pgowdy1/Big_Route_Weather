using Microsoft.AspNetCore.Mvc;
using RouteWeather.API.Services;
using RouteWeather.Core.Models;
using RouteWeather.Core.Services;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Controllers;

[ApiController]
[Route("api/routes")]
public class RoutesController : ControllerBase
{
    private const string CachedPolicy = "public, max-age=900, stale-while-revalidate=3600";
    private const string PositionsCachePolicy = "public, max-age=86400, stale-while-revalidate=604800";
    // Stale payloads must not be browser-cached for 15 minutes, or the
    // frontend's recovery refetch would be served the same stale bytes.
    private const string NoCachePolicy = "no-cache";

    private readonly RouteRepository _routes;
    private readonly IConditionsAggregator _aggregator;

    public RoutesController(RouteRepository routes, IConditionsAggregator aggregator)
    {
        _routes = routes;
        _aggregator = aggregator;
    }

    [HttpGet("positions")]
    public async Task<IActionResult> GetPositions(CancellationToken ct)
    {
        var positions = await _routes.GetPositionsAsync(ct);
        var dto = positions.Select(p => new
        {
            slug = p.Slug,
            mountain = p.Mountain,
            summitLat = p.SummitLat,
            summitLon = p.SummitLon,
            rangeSlug = p.RangeSlug,
        });
        Response.Headers.CacheControl = PositionsCachePolicy;
        return Ok(dto);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var routes = await _routes.GetAllAsync(ct);
        var pairs = await _aggregator.GetManyCacheOnlyAsync(routes, ct);
        var dto = pairs.Select(p => ToSummary(p.Route, p.Conditions)).ToList();
        Response.Headers.CacheControl = pairs.Any(p => p.Conditions.IsStale) ? NoCachePolicy : CachedPolicy;
        return Ok(dto);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var route = await _routes.GetBySlugAsync(slug, ct);
        if (route is null) return NotFound();
        var conditions = await _aggregator.GetConditionsAsync(route, FetchMode.CacheOnly, ct);
        Response.Headers.CacheControl = conditions.IsStale ? NoCachePolicy : CachedPolicy;
        return Ok(ToDetail(route, conditions));
    }

    private static object ToSummary(RouteEntity route, RouteConditions c)
    {
        var window = c.WindowGrades?.Next24h;
        return new
        {
            slug = c.Route.Slug,
            mountain = c.Route.Mountain,
            routeName = c.Route.RouteName,
            summitElevationFt = c.Route.SummitElevationFt,
            summitLat = c.Route.SummitLat,
            summitLon = c.Route.SummitLon,
            classDifficulty = c.Route.ClassDifficulty,
            isGlaciated = route.IsGlaciated,
            rangeSlug = route.Range?.Slug ?? string.Empty,
            rangeName = route.Range?.Name ?? string.Empty,
            grade = window?.Grade?.ToString(),
            overallScore = window?.OverallScore,
            drivers = window?.Drivers ?? Array.Empty<Driver>(),
            updatedAt = c.UpdatedAt,
            isStale = c.IsStale,
            consensus = SerializeConsensus(c.Consensus),
            airQualityUsAqi = c.AirQuality?.UsAqi,
            nextWindow = SerializeNextWindow(c.Windows),
        };
    }

    private static object ToDetail(RouteEntity route, RouteConditions c) => new
    {
        slug = c.Route.Slug,
        mountain = c.Route.Mountain,
        routeName = c.Route.RouteName,
        summitElevationFt = c.Route.SummitElevationFt,
        summitLat = c.Route.SummitLat,
        summitLon = c.Route.SummitLon,
        classDifficulty = c.Route.ClassDifficulty,
        isGlaciated = route.IsGlaciated,
        rangeSlug = route.Range?.Slug ?? string.Empty,
        rangeName = route.Range?.Name ?? string.Empty,
        grade = c.Grade?.ToString(),
        overallScore = c.OverallScore,
        drivers = c.Drivers,
        factors = c.Factors,
        rationale = c.Rationale,
        updatedAt = c.UpdatedAt,
        isStale = c.IsStale,
        forecastNext48h = c.Weather?.Hourly.Take(WeatherSnapshot.HeadlineHours),
        snowpack = c.Snowpack,
        windowGrades = c.WindowGrades is null ? null : new
        {
            next12h = SerializeWindow(c.WindowGrades.Next12h),
            next24h = SerializeWindow(c.WindowGrades.Next24h),
            next48h = SerializeWindow(c.WindowGrades.Next48h),
        },
        sources = new
        {
            nws = new { fetchedAt = c.Sources.NwsFetchedAt },
            snotel = new { fetchedAt = c.Sources.SnotelFetchedAt },
        },
        consensus = SerializeConsensus(c.Consensus),
        airQuality = c.AirQuality is null ? null : new
        {
            usAqi = c.AirQuality.UsAqi,
            pm25 = c.AirQuality.Pm25,
            fetchedAt = c.Sources.AirQualityFetchedAt,
        },
        daylight = ComputeDaylight(c) is { } day ? new
        {
            sunriseUtc = day.SunriseUtc,
            sunsetUtc = day.SunsetUtc,
            daylightHours = day.DaylightHours,
        } : null,
        perSourceForecast = c.PerSourceForecast?.Select(p => new
        {
            sourceName = p.SourceName,
            windMph = p.WindMph,
            tempF = p.TempF,
            precipitationProbabilityPct = p.PrecipitationProbabilityPct,
            fetchedAt = p.FetchedAt,
            maxGustMph = p.MaxGustMph,
            maxCapeJkg = p.MaxCapeJkg,
        }),
        climbWindows = c.Windows?
            .Where(w => w.EndUtc > DateTimeOffset.UtcNow)
            .Select(w => new
            {
                startUtc = w.StartUtc,
                endUtc = w.EndUtc,
                grade = w.Grade.ToString(),
                score = w.Score,
                endReason = w.EndReason,
                lowConfidence = w.LowConfidence,
            }),
        hourlyQuality = c.HourlyScores?.Select(q => new
        {
            timeUtc = q.TimeUtc,
            score = q.Score,
            qualifies = q.Qualifies,
        }),
        dailyDaylight = ComputeDailyDaylight(c),
    };

    // Computed at read time, never cached: a RouteConditions row can be served up to
    // 24h stale, which would freeze sunrise/sunset into the past.
    private static DaylightInfo? ComputeDaylight(RouteConditions c) =>
        SolarCalculator.NextDaylight(c.Route.SummitLat, c.Route.SummitLon, DateTimeOffset.UtcNow);

    // Selected at request time so a memory-cached RouteConditions can't pin
    // "next window" to a stretch that has already ended.
    private static object? SerializeNextWindow(IReadOnlyList<ClimbWindow>? windows)
    {
        var best = windows?
            .Where(w => w.EndUtc > DateTimeOffset.UtcNow)
            .OrderByDescending(w => w.Score)
            .ThenBy(w => w.StartUtc)
            .FirstOrDefault();
        return best is null ? null : new
        {
            startUtc = best.StartUtc,
            endUtc = best.EndUtc,
            grade = best.Grade.ToString(),
            lowConfidence = best.LowConfidence,
        };
    }

    // Read-time like ComputeDaylight: a stale cached row must not freeze sunrise/sunset.
    // Today + 8 days covers the 168h horizon plus UTC-date spill at western longitudes.
    private static IReadOnlyList<object> ComputeDailyDaylight(RouteConditions c)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Enumerable.Range(0, 9)
            .Select(i => SolarCalculator.ComputeUtc(c.Route.SummitLat, c.Route.SummitLon, today.AddDays(i)))
            .Where(d => d is not null)
            .Select(d => (object)new { sunriseUtc = d!.SunriseUtc, sunsetUtc = d.SunsetUtc })
            .ToList();
    }

    private static object? SerializeConsensus(ConsensusReport? r) => r is null ? null : new
    {
        level = r.Level.ToString().ToLowerInvariant(),
        worstFactor = r.WorstFactor,
        coefficientOfVariationByFactor = r.CoefficientOfVariationByFactor,
        sourcesReporting = r.SourcesReporting,
        sourcesAttempted = r.SourcesAttempted,
    };

    private static object SerializeWindow(WindowGrade w) => new
    {
        grade = w.Grade?.ToString(),
        overallScore = w.OverallScore,
        hoursCovered = w.HoursCovered,
        factors = w.Factors,
        drivers = w.Drivers,
        rationale = w.Rationale,
    };
}
