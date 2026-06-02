using Microsoft.AspNetCore.Mvc;
using RouteWeather.API.Services;
using RouteWeather.Core.Models;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Controllers;

[ApiController]
[Route("api/routes")]
public class RoutesController : ControllerBase
{
    private const int MaxConcurrentFetches = 8;

    private readonly RouteRepository _routes;
    private readonly ConditionsAggregator _aggregator;

    public RoutesController(RouteRepository routes, ConditionsAggregator aggregator)
    {
        _routes = routes;
        _aggregator = aggregator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var routes = await _routes.GetAllAsync(ct);
        using var gate = new SemaphoreSlim(MaxConcurrentFetches, MaxConcurrentFetches);

        var tasks = routes.Select(async r =>
        {
            await gate.WaitAsync(ct);
            try { return await _aggregator.GetConditionsAsync(r, ct); }
            finally { gate.Release(); }
        });

        var conditions = await Task.WhenAll(tasks);
        var dto = conditions.Select(ToSummary).ToList();
        return Ok(dto);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var route = await _routes.GetBySlugAsync(slug, ct);
        if (route is null) return NotFound();
        var conditions = await _aggregator.GetConditionsAsync(route, ct);
        return Ok(ToDetail(conditions));
    }

    private static object ToSummary(RouteConditions c)
    {
        var window = c.WindowGrades?.Next24h;
        return new
        {
            slug = c.Route.Slug,
            mountain = c.Route.Mountain,
            routeName = c.Route.RouteName,
            summitElevationFt = c.Route.SummitElevationFt,
            classDifficulty = c.Route.ClassDifficulty,
            grade = (window?.Grade ?? c.Grade)?.ToString(),
            overallScore = window?.OverallScore ?? c.OverallScore,
            drivers = window?.Drivers ?? c.Drivers,
            updatedAt = c.UpdatedAt,
            isStale = c.IsStale,
        };
    }

    private static object ToDetail(RouteConditions c) => new
    {
        slug = c.Route.Slug,
        mountain = c.Route.Mountain,
        routeName = c.Route.RouteName,
        summitElevationFt = c.Route.SummitElevationFt,
        summitLat = c.Route.SummitLat,
        summitLon = c.Route.SummitLon,
        classDifficulty = c.Route.ClassDifficulty,
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
