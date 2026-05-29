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
        return await db.Routes.AsNoTracking().OrderBy(r => r.Mountain).ToListAsync(ct);
    }

    public async Task<RouteEntity?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug, ct);
    }
}
