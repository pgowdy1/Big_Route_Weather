using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data.Repositories;

public class DailyApiCallRepository
{
    private readonly IDbContextFactory<RouteWeatherContext> _dbFactory;

    public DailyApiCallRepository(IDbContextFactory<RouteWeatherContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<List<DailyApiCallEntity>> GetSinceAsync(DateOnly fromDateInclusive, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.DailyApiCalls.AsNoTracking()
            .Where(c => c.DateUtc >= fromDateInclusive)
            .OrderByDescending(c => c.DateUtc)
            .ThenBy(c => c.Source)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(DateOnly dateUtc, string source, int count, DateTime updatedAtUtc, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.DailyApiCalls
            .FirstOrDefaultAsync(c => c.DateUtc == dateUtc && c.Source == source, ct);

        if (existing is null)
        {
            db.DailyApiCalls.Add(new DailyApiCallEntity
            {
                DateUtc = dateUtc,
                Source = source,
                Count = count,
                UpdatedAtUtc = updatedAtUtc,
            });
        }
        else
        {
            existing.Count = count;
            existing.UpdatedAtUtc = updatedAtUtc;
        }
        await db.SaveChangesAsync(ct);
    }
}
