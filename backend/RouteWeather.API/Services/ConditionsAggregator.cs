using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RouteWeather.API.Options;
using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using RouteWeather.Data.Entities;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

public class ConditionsAggregator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    private readonly IReadOnlyList<IForecastSource> _forecastSources;
    private readonly IReadOnlyList<ISnowpackSource> _snowpackSources;
    private readonly ForecastCacheRepository _cache;
    private readonly ForecastSourcesOptions _options;
    private readonly ConsensusCalculator _consensus;
    private readonly ILogger<ConditionsAggregator> _logger;

    public ConditionsAggregator(
        IEnumerable<IForecastSource> forecastSources,
        IEnumerable<ISnowpackSource> snowpackSources,
        ForecastCacheRepository cache,
        IOptions<ForecastSourcesOptions> options,
        ConsensusCalculator consensus,
        ILogger<ConditionsAggregator> logger)
    {
        _forecastSources = forecastSources.ToArray();
        _snowpackSources = snowpackSources.ToArray();
        _cache = cache;
        _options = options.Value;
        _consensus = consensus;
        _logger = logger;
    }

    public async Task<RouteConditions> GetConditionsAsync(RouteEntity routeEntity, CancellationToken ct = default)
    {
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

            await Task.WhenAll(forecastFetches.Cast<Task>().Concat(snowpackFetches));

            var forecastResults = forecastFetches.Select(t => t.Result).ToList();
            var snowpackResults = snowpackFetches.Select(t => t.Result).ToList();

            var liveForecasts = forecastResults.Where(r => r.Snapshot is not null).ToList();
            var consensusInputs = liveForecasts
                .Select(r => new ConsensusInput(
                    new SourceSnapshot(r.SourceName, r.Snapshot!, r.FetchedAt ?? DateTimeOffset.UtcNow, r.ActiveFactors),
                    _options.WeightFor(r.SourceName)))
                .ToList();

            var ensemble = _consensus.Compute(consensusInputs, _forecastSources.Count);
            var blendedWeather = ensemble.Blended;
            var snowpack = snowpackResults.FirstOrDefault(r => r.Snapshot is not null).Snapshot;

            var result = GradeCalculator.Compute(blendedWeather, snowpack);

            var weatherFetched = liveForecasts.Count == 0 ? null : liveForecasts.Max(r => r.FetchedAt);
            var snowpackFetched = snowpackResults.Where(r => r.Snapshot is not null).Select(r => r.FetchedAt).FirstOrDefault();

            var updatedAt = MaxOf(weatherFetched, snowpackFetched) ?? DateTimeOffset.UtcNow;
            var isStale = (forecastResults.Any(r => r.IsStale) && liveForecasts.Count == 0)
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
                snowpackFetched);

            var perSourceForecast = liveForecasts
                .Select(r => new PerSourceForecast(
                    r.SourceName,
                    r.Snapshot!.WindMph,
                    r.Snapshot.TempF,
                    r.ActiveFactors.Contains(ForecastFactors.Precipitation) ? r.Snapshot.PrecipitationProbabilityPct : (int?)null,
                    r.FetchedAt ?? DateTimeOffset.UtcNow))
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
                perSourceForecast.Count == 0 ? null : perSourceForecast);
        }
        finally
        {
            gate.Release();
        }
    }

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
            fresh = await source.FetchAsync(route.SummitLat, route.SummitLon, ct);
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

    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOpts);

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
}
