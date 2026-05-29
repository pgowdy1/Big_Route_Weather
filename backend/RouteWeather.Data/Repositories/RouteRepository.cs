using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data.Repositories;

public class RouteRepository
{
    private readonly RouteWeatherContext _db;

    public RouteRepository(RouteWeatherContext db) => _db = db;

    public Task<List<RouteEntity>> GetAllAsync(CancellationToken ct = default) =>
        _db.Routes.AsNoTracking().OrderBy(r => r.Mountain).ToListAsync(ct);

    public Task<RouteEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        _db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug, ct);
}
