using System.Collections.Concurrent;
using System.Text.Json;
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

public class ConditionsAggregator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    private readonly NwsClient _nws;
    private readonly SnotelClient _snotel;
    private readonly ForecastCacheRepository _cache;
    private readonly ILogger<ConditionsAggregator> _logger;

    public ConditionsAggregator(
        NwsClient nws,
        SnotelClient snotel,
        ForecastCacheRepository cache,
        ILogger<ConditionsAggregator> logger)
    {
        _nws = nws;
        _snotel = snotel;
        _cache = cache;
        _logger = logger;
    }

    public async Task<RouteConditions> GetConditionsAsync(RouteEntity routeEntity, CancellationToken ct = default)
    {
        var gate = Gates.GetOrAdd(routeEntity.Slug, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var (weather, weatherFetched, weatherStale) = await GetOrFetchWeatherAsync(routeEntity, ct);
            var (snowpack, snowpackFetched, snowpackStale) = await GetOrFetchSnowpackAsync(routeEntity, ct);

            var result = GradeCalculator.Compute(weather, snowpack);
            var updatedAt = MaxOf(weatherFetched, snowpackFetched) ?? DateTimeOffset.UtcNow;
            var isStale = weatherStale || snowpackStale;

            var route = new Core.Models.Route(
                routeEntity.Slug,
                routeEntity.Mountain,
                routeEntity.RouteName,
                routeEntity.SummitElevationFt,
                routeEntity.SummitLat,
                routeEntity.SummitLon,
                routeEntity.ClassDifficulty,
                routeEntity.SnotelStationTriplet);

            return new RouteConditions(
                route,
                weather is null && snowpack is null ? null : result.Grade,
                weather is null && snowpack is null ? null : result.OverallScore,
                result.Drivers,
                result.Factors,
                result.Rationale,
                updatedAt,
                isStale,
                weather,
                snowpack);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(WeatherSnapshot?, DateTimeOffset?, bool)> GetOrFetchWeatherAsync(RouteEntity route, CancellationToken ct)
    {
        var cached = await _cache.GetAsync(route.Id, "NWS", ct);
        var nowUtc = DateTime.UtcNow;
        if (cached is not null && cached.ExpiresAtUtc > nowUtc)
        {
            return (Deserialize<WeatherSnapshot>(cached.PayloadJson), new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), false);
        }

        var fresh = await _nws.GetForecastAsync(route.SummitLat, route.SummitLon, ct);
        if (fresh is not null)
        {
            await _cache.UpsertAsync(route.Id, "NWS", JsonSerializer.Serialize(fresh, JsonOpts), nowUtc.Add(CacheTtl), ct);
            return (fresh, DateTimeOffset.UtcNow, false);
        }

        if (cached is not null)
        {
            _logger.LogInformation("Serving stale NWS data for {Slug}", route.Slug);
            return (Deserialize<WeatherSnapshot>(cached.PayloadJson), new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), true);
        }

        return (null, null, true);
    }

    private async Task<(SnowpackSnapshot?, DateTimeOffset?, bool)> GetOrFetchSnowpackAsync(RouteEntity route, CancellationToken ct)
    {
        var cached = await _cache.GetAsync(route.Id, "SNOTEL", ct);
        var nowUtc = DateTime.UtcNow;
        if (cached is not null && cached.ExpiresAtUtc > nowUtc)
        {
            return (Deserialize<SnowpackSnapshot>(cached.PayloadJson), new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), false);
        }

        var fresh = await _snotel.GetSnowpackAsync(route.SnotelStationTriplet, ct);
        if (fresh is not null)
        {
            await _cache.UpsertAsync(route.Id, "SNOTEL", JsonSerializer.Serialize(fresh, JsonOpts), nowUtc.Add(CacheTtl), ct);
            return (fresh, DateTimeOffset.UtcNow, false);
        }

        if (cached is not null)
        {
            _logger.LogInformation("Serving stale SNOTEL data for {Slug}", route.Slug);
            return (Deserialize<SnowpackSnapshot>(cached.PayloadJson), new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), true);
        }

        return (null, null, true);
    }

    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOpts);

    private static DateTimeOffset? MaxOf(DateTimeOffset? a, DateTimeOffset? b) =>
        (a, b) switch
        {
            (null, null) => null,
            (null, _) => b,
            (_, null) => a,
            _ => a > b ? a : b,
        };
}
