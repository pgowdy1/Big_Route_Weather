using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Controllers;

[ApiController]
[Route("api/ranges")]
public class RangesController : ControllerBase
{
    private const string CachePolicy = "public, max-age=3600, stale-while-revalidate=86400";

    private readonly RangeRepository _ranges;

    public RangesController(RangeRepository ranges) => _ranges = ranges;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var ranges = await _ranges.GetAllAsync(ct);
        var dto = ranges.Select(r => new
        {
            slug = r.Slug,
            name = r.Name,
            color = r.Color,
            description = r.Description,
            displayOrder = r.DisplayOrder,
            perimeterGeoJson = ParseGeoJson(r.PerimeterGeoJson),
        }).ToList();

        Response.Headers.CacheControl = CachePolicy;
        return Ok(dto);
    }

    private static object? ParseGeoJson(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<JsonElement>(raw);
}
