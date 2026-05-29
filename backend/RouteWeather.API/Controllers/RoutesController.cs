using Microsoft.AspNetCore.Mvc;
using RouteWeather.API.Services;
using RouteWeather.Core.Models;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Controllers;

[ApiController]
[Route("api/routes")]
public class RoutesController : ControllerBase
{
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
        var tasks = routes.Select(r => _aggregator.GetConditionsAsync(r, ct));
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

    private static object ToSummary(RouteConditions c) => new
    {
        slug = c.Route.Slug,
        mountain = c.Route.Mountain,
        routeName = c.Route.RouteName,
        summitElevationFt = c.Route.SummitElevationFt,
        classDifficulty = c.Route.ClassDifficulty,
        grade = c.Grade?.ToString(),
        overallScore = c.OverallScore,
        drivers = c.Drivers,
        updatedAt = c.UpdatedAt,
        isStale = c.IsStale,
    };

    private static object ToDetail(RouteConditions c) => new
    {
        slug = c.Route.Slug,
        mountain = c.Route.Mountain,
        routeName = c.Route.RouteName,
        summitElevationFt = c.Route.SummitElevationFt,
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
    };
}
