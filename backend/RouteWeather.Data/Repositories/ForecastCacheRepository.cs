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
