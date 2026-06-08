using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data;

public class RouteWeatherContext : DbContext
{
    public RouteWeatherContext(DbContextOptions<RouteWeatherContext> options) : base(options) { }

    public DbSet<RouteEntity> Routes => Set<RouteEntity>();
    public DbSet<CachedForecastEntity> CachedForecasts => Set<CachedForecastEntity>();
    public DbSet<DailyApiCallEntity> DailyApiCalls => Set<DailyApiCallEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RouteEntity>()
            .HasIndex(r => r.Slug)
            .IsUnique();

        modelBuilder.Entity<CachedForecastEntity>()
            .HasIndex(c => new { c.RouteId, c.Source })
            .IsUnique();

        modelBuilder.Entity<DailyApiCallEntity>()
            .HasIndex(c => new { c.DateUtc, c.Source })
            .IsUnique();

        modelBuilder.Entity<DailyApiCallEntity>()
            .Property(c => c.Source)
            .HasMaxLength(60);
    }
}
