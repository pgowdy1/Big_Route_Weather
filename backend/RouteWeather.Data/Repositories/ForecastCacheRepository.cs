using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data.Repositories;

public class ForecastCacheRepository
{
    private readonly RouteWeatherContext _db;

    public ForecastCacheRepository(RouteWeatherContext db) => _db = db;

    public Task<CachedForecastEntity?> GetAsync(int routeId, string source, CancellationToken ct = default) =>
        _db.CachedForecasts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.RouteId == routeId && c.Source == source, ct);

    public async Task UpsertAsync(int routeId, string source, string payloadJson, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        var existing = await _db.CachedForecasts
            .FirstOrDefaultAsync(c => c.RouteId == routeId && c.Source == source, ct);

        var nowUtc = DateTime.UtcNow;
        if (existing is null)
        {
            _db.CachedForecasts.Add(new CachedForecastEntity
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
        await _db.SaveChangesAsync(ct);
    }
}
