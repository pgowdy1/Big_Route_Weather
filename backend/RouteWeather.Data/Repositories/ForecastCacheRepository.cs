using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data.Repositories;

public class ForecastCacheRepository
{
    private readonly IDbContextFactory<RouteWeatherContext> _dbFactory;

    public ForecastCacheRepository(IDbContextFactory<RouteWeatherContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<CachedForecastEntity?> GetAsync(int routeId, string source, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CachedForecasts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.RouteId == routeId && c.Source == source, ct);
    }

    /// Last-known rows for one route, ignoring TTL — the cache-only read path
    /// decides staleness itself.
    public async Task<List<CachedForecastEntity>> GetForRouteAsync(int routeId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CachedForecasts.AsNoTracking()
            .Where(c => c.RouteId == routeId)
            .ToListAsync(ct);
    }

    /// All last-known rows in one query (87 routes × ≈ 7 rows per route incl. the
    /// NWS-Grid mapping row ≈ 609 rows), so a cold GET /api/routes costs 1 query
    /// instead of one per route.
    public async Task<List<CachedForecastEntity>> GetAllLatestAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CachedForecasts.AsNoTracking().ToListAsync(ct);
    }

    public async Task UpsertAsync(int routeId, string source, string payloadJson, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.CachedForecasts
            .FirstOrDefaultAsync(c => c.RouteId == routeId && c.Source == source, ct);

        var nowUtc = DateTime.UtcNow;
        if (existing is null)
        {
            db.CachedForecasts.Add(new CachedForecastEntity
            {
                RouteId = routeId,
                Source = source,
                PayloadJson = payloadJson,
                FetchedAtUtc = nowUtc,
                ExpiresAtUtc = expiresAtUtc,
            });
        }
        else
        {
            existing.PayloadJson = payloadJson;
            existing.FetchedAtUtc = nowUtc;
            existing.ExpiresAtUtc = expiresAtUtc;
        }
        await db.SaveChangesAsync(ct);
    }
}
