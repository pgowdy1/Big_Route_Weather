using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data.Repositories;

public class RangeRepository
{
    private readonly IDbContextFactory<RouteWeatherContext> _dbFactory;

    public RangeRepository(IDbContextFactory<RouteWeatherContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<List<RangeEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Ranges
            .AsNoTracking()
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync(ct);
    }
}
