using Microsoft.AspNetCore.Mvc;
using RouteWeather.API.Services;
using RouteWeather.Core.Models;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Controllers;

[ApiController]
[Route("api/routes")]
public class RoutesController : ControllerBase
{
    private const int MaxConcurrentFetches = 8;
    private const string CachedPolicy = "public, max-age=900, stale-while-revalidate=3600";
    private const string PositionsCachePolicy = "public, max-age=86400, stale-while-revalidate=604800";

    private readonly RouteRepository _routes;
    private readonly ConditionsAggregator _aggregator;

    public RoutesController(RouteRepository routes, ConditionsAggregator aggregator)
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
        using var gate = new SemaphoreSlim(MaxConcurrentFetches, MaxConcurrentFetches);

        var tasks = routes.Select(async r =>
        {
            await gate.WaitAsync(ct);
            try { return (Route: r, Conditions: await _aggregator.GetConditionsAsync(r, useCache: true, ct)); }
            finally { gate.Release(); }
        });

        var pairs = await Task.WhenAll(tasks);
        var dto = pairs.Select(p => ToSummary(p.Route, p.Conditions)).ToList();
        Response.Headers.CacheControl = CachedPolicy;
        return Ok(dto);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var route = await _routes.GetBySlugAsync(slug, ct);
        if (route is null) return NotFound();
        var conditions = await _aggregator.GetConditionsAsync(route, useCache: true, ct);
        Response.Headers.CacheControl = CachedPolicy;
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
            rangeSlug = route.Range?.Slug ?? string.Empty,
            rangeName = route.Range?.Name ?? string.Empty,
            grade = window?.Grade?.ToString(),
            overallScore = window?.OverallScore,
            drivers = window?.Drivers ?? Array.Empty<Driver>(),
            updatedAt = c.UpdatedAt,
            isStale = c.IsStale,
            consensus = SerializeConsensus(c.Consensus),
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
        rangeSlug = route.Range?.Slug ?? string.Empty,
        rangeName = route.Range?.Name ?? string.Empty,
        grade = c.Grade?.ToString(),
        overallScore = c.OverallScore,
        drivers = c.Drivers,
        factors = c.Factors,
        rationale = c.Rationale,
        updatedAt = c.UpdatedAt,
        isStale = c.IsStale,
        forecastNext48h = c.Weather?.Next48Hours,
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
        perSourceForecast = c.PerSourceForecast?.Select(p => new
        {
            sourceName = p.SourceName,
            windMph = p.WindMph,
            tempF = p.TempF,
            precipitationProbabilityPct = p.PrecipitationProbabilityPct,
            fetchedAt = p.FetchedAt,
        }),
    };

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
