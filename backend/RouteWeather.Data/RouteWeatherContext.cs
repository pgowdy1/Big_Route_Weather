using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data;

public class RouteWeatherContext : DbContext
{
    public RouteWeatherContext(DbContextOptions<RouteWeatherContext> options) : base(options) { }

    public DbSet<RouteEntity> Routes => Set<RouteEntity>();
    public DbSet<CachedForecastEntity> CachedForecasts => Set<CachedForecastEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RouteEntity>()
            .HasIndex(r => r.Slug)
            .IsUnique();

        modelBuilder.Entity<CachedForecastEntity>()
            .HasIndex(c => new { c.RouteId, c.Source })
            .IsUnique();
    }
}
