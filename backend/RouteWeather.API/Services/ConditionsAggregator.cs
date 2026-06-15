using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RouteWeather.API.Options;
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

public class ConditionsAggregator : IConditionsAggregator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    // The warmer overwrites entries every cycle (default 10m). If it stalls, entries
    // expire at 30m and reads degrade to SQLite last-known instead of frozen data.
    private static readonly TimeSpan ReadThroughCacheTtl = TimeSpan.FromMinutes(30);
    // Cache-only results may be stale; keep them only briefly so the warmer's
    // fresh write supersedes them quickly.
    private static readonly TimeSpan CacheOnlyCacheTtl = TimeSpan.FromMinutes(2);

    private readonly IReadOnlyList<IForecastSource> _forecastSources;
    private readonly IReadOnlyList<ISnowpackSource> _snowpackSources;
    private readonly IReadOnlyList<IAirQualitySource> _airQualitySources;
    private readonly ForecastCacheRepository _cache;
    private readonly ForecastSourcesOptions _options;
    private readonly TimeSpan _serveStaleMax;
    private readonly ConsensusCalculator _consensus;
    private readonly IMemoryCache _conditionsCache;
    private readonly ILogger<ConditionsAggregator> _logger;

    public ConditionsAggregator(
        IEnumerable<IForecastSource> forecastSources,
        IEnumerable<ISnowpackSource> snowpackSources,
        IEnumerable<IAirQualitySource> airQualitySources,
        ForecastCacheRepository cache,
        IOptions<ForecastSourcesOptions> options,
        IOptions<WarmerOptions> warmerOptions,
        ConsensusCalculator consensus,
        IMemoryCache conditionsCache,
        ILogger<ConditionsAggregator> logger)
    {
        _forecastSources = forecastSources.ToArray();
        _snowpackSources = snowpackSources.ToArray();
        _airQualitySources = airQualitySources.ToArray();
        _cache = cache;
        _options = options.Value;
        _serveStaleMax = TimeSpan.FromHours(warmerOptions.Value.ServeStaleMaxHours);
        _consensus = consensus;
        _conditionsCache = conditionsCache;
        _logger = logger;
    }

    public async Task<RouteConditions> GetConditionsAsync(
        RouteEntity routeEntity,
        FetchMode mode,
        CancellationToken ct = default)
    {
        var cacheKey = ConditionsCacheKey(routeEntity.Slug);

        if (mode == FetchMode.CacheOnly)
        {
            // No per-slug gate here: reads must never queue behind a warmer
            // aggregation that is mid-flight on upstream fetches.
            if (_conditionsCache.TryGetValue(cacheKey, out RouteConditions? cached) && cached is not null)
            {
                return cached;
            }
            var rows = await _cache.GetForRouteAsync(routeEntity.Id, ct);
            return BuildFromCachedRows(routeEntity, rows);
        }

        var gate = Gates.GetOrAdd(routeEntity.Slug, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var forecastFetches = _forecastSources
                .Select(s => FetchForecastAsync(routeEntity, s, ct))
                .ToArray();
            var snowpackFetches = _snowpackSources
                .Select(s => FetchSnowpackAsync(routeEntity, s, ct))
                .ToArray();
            var airQualityFetches = _airQualitySources
                .Select(s => FetchAirQualityAsync(routeEntity, s, ct))
                .ToArray();

            await Task.WhenAll(forecastFetches.Cast<Task>()
                .Concat(snowpackFetches)
                .Concat(airQualityFetches));

            var conditions = BuildConditions(
                routeEntity,
                forecastFetches.Select(t => t.Result).ToList(),
                snowpackFetches.Select(t => t.Result).ToList(),
                airQualityFetches.Select(t => t.Result).ToList(),
                forceStale: false);

            if (conditions.Grade is not null)
            {
                _conditionsCache.Set(cacheKey, conditions, ReadThroughCacheTtl);
            }

            return conditions;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<RouteConditionsPair>> GetManyCacheOnlyAsync(
        IReadOnlyList<RouteEntity> routes,
        CancellationToken ct = default)
    {
        var anyMiss = routes.Any(r =>
            !_conditionsCache.TryGetValue(ConditionsCacheKey(r.Slug), out RouteConditions? c) || c is null);
        var rowsByRoute = anyMiss
            ? (await _cache.GetAllLatestAsync(ct)).ToLookup(r => r.RouteId)
            : null;

        var results = new List<RouteConditionsPair>(routes.Count);
        foreach (var route in routes)
        {
            if (_conditionsCache.TryGetValue(ConditionsCacheKey(route.Slug), out RouteConditions? cached) && cached is not null)
            {
                results.Add(new RouteConditionsPair(route, cached));
            }
            else
            {
                var rows = rowsByRoute?[route.Id].ToList()
                    ?? await _cache.GetForRouteAsync(route.Id, ct);
                results.Add(new RouteConditionsPair(route, BuildFromCachedRows(route, rows)));
            }
        }
        return results;
    }

    private RouteConditions BuildFromCachedRows(RouteEntity routeEntity, IReadOnlyList<CachedForecastEntity> rows)
    {
        var nowUtc = DateTime.UtcNow;
        var cutoffUtc = nowUtc - _serveStaleMax;

        var forecastResults = _forecastSources.Select(s =>
        {
            var row = rows.FirstOrDefault(r => string.Equals(r.Source, s.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null || row.FetchedAtUtc < cutoffUtc)
            {
                return new SourceFetchResult(s.Name, null, null, true, s.ActiveFactors);
            }
            return new SourceFetchResult(s.Name, TryDeserialize<WeatherSnapshot>(row.PayloadJson, s.Name, routeEntity.Id),
                new DateTimeOffset(row.FetchedAtUtc, TimeSpan.Zero), row.ExpiresAtUtc <= nowUtc, s.ActiveFactors);
        }).ToList();

        var snowpackResults = _snowpackSources.Select(s =>
        {
            var row = rows.FirstOrDefault(r => string.Equals(r.Source, s.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null || row.FetchedAtUtc < cutoffUtc)
            {
                return new SnowpackFetchResult(s.Name, null, null, true);
            }
            return new SnowpackFetchResult(s.Name, TryDeserialize<SnowpackSnapshot>(row.PayloadJson, s.Name, routeEntity.Id),
                new DateTimeOffset(row.FetchedAtUtc, TimeSpan.Zero), row.ExpiresAtUtc <= nowUtc);
        }).ToList();

        // Air quality is display context, not a grading input, so it is never
        // folded into forceStale: a stale/missing AQI row must not mark the route stale.
        var airQualityResults = _airQualitySources.Select(s =>
        {
            var row = rows.FirstOrDefault(r => string.Equals(r.Source, s.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null || row.FetchedAtUtc < cutoffUtc)
            {
                return new AirQualityFetchResult(null, null);
            }
            return new AirQualityFetchResult(TryDeserialize<AirQualitySnapshot>(row.PayloadJson, s.Name, routeEntity.Id),
                new DateTimeOffset(row.FetchedAtUtc, TimeSpan.Zero));
        }).ToList();

        // Stale = any *served* row past its per-source TTL. Rows missing entirely
        // (never fetched, or beyond the 24h cap) flow through the standard
        // "source absent" semantics instead, so a chronically failing source
        // cannot mark every response stale forever. AirQuality is intentionally absent here.
        var forceStale = forecastResults.Any(r => r.Snapshot is not null && r.IsStale)
                         || snowpackResults.Any(r => r.Snapshot is not null && r.IsStale);

        var conditions = BuildConditions(routeEntity, forecastResults, snowpackResults, airQualityResults, forceStale);

        var cacheKey = ConditionsCacheKey(routeEntity.Slug);
        // Best-effort guard, not atomic: a racing warmer Set can still be
        // overwritten; any such stale entry is bounded by the 2-minute TTL.
        // Stale results are cached deliberately — refetches keep seeing isStale
        // (and no-cache headers) until the warmer's ReadThrough write lands.
        if (conditions.Grade is not null && !_conditionsCache.TryGetValue(cacheKey, out _))
        {
            _conditionsCache.Set(cacheKey, conditions, CacheOnlyCacheTtl);
        }
        return conditions;
    }

    private RouteConditions BuildConditions(
        RouteEntity routeEntity,
        List<SourceFetchResult> forecastResults,
        List<SnowpackFetchResult> snowpackResults,
        List<AirQualityFetchResult> airQualityResults,
        bool forceStale)
    {
        var airQuality = airQualityResults.FirstOrDefault(r => r.Snapshot is not null);
        var liveForecasts = forecastResults.Where(r => r.Snapshot is not null).ToList();
        var consensusInputs = liveForecasts
            .Select(r => new ConsensusInput(
                new SourceSnapshot(r.SourceName, r.Snapshot!, r.FetchedAt ?? DateTimeOffset.UtcNow, r.ActiveFactors),
                _options.WeightFor(r.SourceName),
                _options.PrecipVoteWeightFor(r.SourceName)))
            .ToList();

        var ensemble = _consensus.Compute(consensusInputs, _forecastSources.Count);
        var blendedWeather = ensemble.Blended;
        var snowpack = snowpackResults.FirstOrDefault(r => r.Snapshot is not null).Snapshot;

        var result = GradeCalculator.Compute(blendedWeather, snowpack);

        var weatherFetched = liveForecasts.Count == 0 ? null : liveForecasts.Max(r => r.FetchedAt);
        var snowpackFetched = snowpackResults.Where(r => r.Snapshot is not null).Select(r => r.FetchedAt).FirstOrDefault();

        var updatedAt = MaxOf(weatherFetched, snowpackFetched) ?? DateTimeOffset.UtcNow;
        var isStale = forceStale
                      || (forecastResults.Any(r => r.IsStale) && liveForecasts.Count == 0)
                      || snowpackResults.Any(r => r.IsStale && snowpack is not null);

        var windowGrades = blendedWeather is null && snowpack is null
            ? null
            : WindowGradeCalculator.Compute(blendedWeather, snowpack);

        var route = new Core.Models.Route(
            routeEntity.Slug,
            routeEntity.Mountain,
            routeEntity.RouteName,
            routeEntity.SummitElevationFt,
            routeEntity.SummitLat,
            routeEntity.SummitLon,
            routeEntity.ClassDifficulty,
            routeEntity.SnotelStationTriplet);

        var nwsResult = forecastResults.FirstOrDefault(r => r.SourceName == "NWS");
        var sourceFreshness = new SourceFreshness(
            nwsResult.FetchedAt ?? weatherFetched,
            snowpackFetched,
            airQuality.FetchedAt);

        var perSourceForecast = liveForecasts
            .Select(r => new PerSourceForecast(
                r.SourceName,
                r.Snapshot!.WindMph,
                r.Snapshot.TempF,
                r.ActiveFactors.Contains(ForecastFactors.Precipitation) ? r.Snapshot.PrecipitationProbabilityPct : (int?)null,
                r.FetchedAt ?? DateTimeOffset.UtcNow,
                r.Snapshot.MaxGustMph,
                r.Snapshot.MaxCapeJkg))
            .ToList();

        return new RouteConditions(
            route,
            blendedWeather is null && snowpack is null ? null : result.Grade,
            blendedWeather is null && snowpack is null ? null : result.OverallScore,
            result.Drivers,
            result.Factors,
            result.Rationale,
            updatedAt,
            isStale,
            blendedWeather,
            snowpack,
            windowGrades,
            sourceFreshness,
            ensemble.Consensus,
            perSourceForecast.Count == 0 ? null : perSourceForecast,
            airQuality.Snapshot);
    }

    private static string ConditionsCacheKey(string slug) => $"conditions:{slug}";

    private async Task<SourceFetchResult> FetchForecastAsync(RouteEntity route, IForecastSource source, CancellationToken ct)
    {
        var ttl = _options.TtlFor(source.Name);
        var nowUtc = DateTime.UtcNow;
        var cached = await _cache.GetAsync(route.Id, source.Name, ct);

        if (cached is not null && cached.ExpiresAtUtc > nowUtc)
        {
            return new SourceFetchResult(source.Name, Deserialize<WeatherSnapshot>(cached.PayloadJson),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), false, source.ActiveFactors);
        }

        WeatherSnapshot? fresh = null;
        try
        {
            fresh = await source.FetchAsync(
                new ForecastLocation(route.Id, route.SummitLat, route.SummitLon, route.SummitElevationFt), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Forecast source {Source} threw for {Slug}", source.Name, route.Slug);
        }

        if (fresh is not null)
        {
            await _cache.UpsertAsync(route.Id, source.Name, JsonSerializer.Serialize(fresh, JsonOpts), nowUtc.Add(ttl), ct);
            return new SourceFetchResult(source.Name, fresh, DateTimeOffset.UtcNow, false, source.ActiveFactors);
        }

        if (cached is not null)
        {
            _logger.LogInformation("Serving stale {Source} data for {Slug}", source.Name, route.Slug);
            return new SourceFetchResult(source.Name, Deserialize<WeatherSnapshot>(cached.PayloadJson),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), true, source.ActiveFactors);
        }

        return new SourceFetchResult(source.Name, null, null, true, source.ActiveFactors);
    }

    private async Task<SnowpackFetchResult> FetchSnowpackAsync(RouteEntity route, ISnowpackSource source, CancellationToken ct)
    {
        var ttl = _options.TtlFor(source.Name);
        var nowUtc = DateTime.UtcNow;
        var cached = await _cache.GetAsync(route.Id, source.Name, ct);

        if (cached is not null && cached.ExpiresAtUtc > nowUtc)
        {
            return new SnowpackFetchResult(source.Name, Deserialize<SnowpackSnapshot>(cached.PayloadJson),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), false);
        }

        SnowpackSnapshot? fresh = null;
        try
        {
            fresh = await source.FetchAsync(route.SnotelStationTriplet, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snowpack source {Source} threw for {Slug}", source.Name, route.Slug);
        }

        if (fresh is not null)
        {
            await _cache.UpsertAsync(route.Id, source.Name, JsonSerializer.Serialize(fresh, JsonOpts), nowUtc.Add(ttl), ct);
            return new SnowpackFetchResult(source.Name, fresh, DateTimeOffset.UtcNow, false);
        }

        if (cached is not null)
        {
            _logger.LogInformation("Serving stale {Source} data for {Slug}", source.Name, route.Slug);
            return new SnowpackFetchResult(source.Name, Deserialize<SnowpackSnapshot>(cached.PayloadJson),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero), true);
        }

        return new SnowpackFetchResult(source.Name, null, null, true);
    }

    // Mirrors FetchSnowpackAsync but carries NO stale flag: AQI is display context,
    // so a stale or failed AQI fetch must never influence the route's isStale.
    private async Task<AirQualityFetchResult> FetchAirQualityAsync(RouteEntity route, IAirQualitySource source, CancellationToken ct)
    {
        var ttl = _options.TtlFor(source.Name);
        var nowUtc = DateTime.UtcNow;
        var cached = await _cache.GetAsync(route.Id, source.Name, ct);

        if (cached is not null && cached.ExpiresAtUtc > nowUtc)
        {
            return new AirQualityFetchResult(TryDeserialize<AirQualitySnapshot>(cached.PayloadJson, source.Name, route.Id),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero));
        }

        AirQualitySnapshot? fresh = null;
        try
        {
            fresh = await source.FetchAsync(route.SummitLat, route.SummitLon, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AirQuality source {Source} threw for {Slug}", source.Name, route.Slug);
        }

        if (fresh is not null)
        {
            await _cache.UpsertAsync(route.Id, source.Name, JsonSerializer.Serialize(fresh, JsonOpts), nowUtc.Add(ttl), ct);
            return new AirQualityFetchResult(fresh, DateTimeOffset.UtcNow);
        }

        if (cached is not null)
        {
            _logger.LogInformation("Serving stale {Source} data for {Slug}", source.Name, route.Slug);
            return new AirQualityFetchResult(TryDeserialize<AirQualitySnapshot>(cached.PayloadJson, source.Name, route.Id),
                new DateTimeOffset(cached.FetchedAtUtc, TimeSpan.Zero));
        }

        return new AirQualityFetchResult(null, null);
    }

    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOpts);

    private T? TryDeserialize<T>(string json, string source, int routeId)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Discarding corrupt cached payload for route {RouteId} source {Source}", routeId, source);
            return default;
        }
    }

    private static DateTimeOffset? MaxOf(DateTimeOffset? a, DateTimeOffset? b) =>
        (a, b) switch
        {
            (null, null) => null,
            (null, _) => b,
            (_, null) => a,
            _ => a > b ? a : b,
        };

    private record struct SourceFetchResult(string SourceName, WeatherSnapshot? Snapshot, DateTimeOffset? FetchedAt, bool IsStale, IReadOnlySet<string> ActiveFactors);
    private record struct SnowpackFetchResult(string SourceName, SnowpackSnapshot? Snapshot, DateTimeOffset? FetchedAt, bool IsStale);
    private record struct AirQualityFetchResult(AirQualitySnapshot? Snapshot, DateTimeOffset? FetchedAt);
}
