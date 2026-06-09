using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data.Repositories;

public class RouteRepository
{
    private readonly IDbContextFactory<RouteWeatherContext> _dbFactory;

    public RouteRepository(IDbContextFactory<RouteWeatherContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<List<RouteEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Routes
            .AsNoTracking()
            .Include(r => r.Range)
            .OrderBy(r => r.Mountain)
            .ToListAsync(ct);
    }

    public async Task<RouteEntity?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Routes
            .AsNoTracking()
            .Include(r => r.Range)
            .FirstOrDefaultAsync(r => r.Slug == slug, ct);
    }

    public async Task<List<RoutePosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Routes
            .AsNoTracking()
            .OrderBy(r => r.Mountain)
            .Select(r => new RoutePosition(
                r.Slug,
                r.Mountain,
                r.SummitLat,
                r.SummitLon,
                r.Range != null ? r.Range.Slug : string.Empty))
            .ToListAsync(ct);
    }
}

public record RoutePosition(string Slug, string Mountain, double SummitLat, double SummitLon, string RangeSlug);
