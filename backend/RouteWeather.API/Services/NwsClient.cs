using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RouteWeather.Core.Models;
using RouteWeather.Core.Services;
using RouteWeather.Core.Sources;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

public class NwsClient : IForecastSource
{
    public string Name => "NWS";

    public IReadOnlySet<string> ActiveFactors => ForecastFactors.All;

    // The lat/lon -> grid mapping is static per route; persist it in the forecast
    // cache table under this pseudo-source so refreshes cost one NWS call, not two.
    private const string GridCacheSource = "NWS-Grid";
    private static readonly TimeSpan GridCacheTtl = TimeSpan.FromDays(365);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ILogger<NwsClient> _logger;
    private readonly DailyCallCounter _calls;
    private readonly ForecastCacheRepository _cache;

    public NwsClient(HttpClient http, ILogger<NwsClient> logger, DailyCallCounter calls, ForecastCacheRepository cache)
    {
        _http = http;
        _logger = logger;
        _calls = calls;
        _cache = cache;
    }

    public async Task<WeatherSnapshot?> FetchAsync(ForecastLocation location, CancellationToken ct = default)
    {
        try
        {
            var (grid, fromPoints) = await ResolveGridAsync(location, forceRefresh: false, ct);
            if (grid is null) return null;

            var snap = await FetchGridpointAsync(grid, ct);
            if (snap is null && !fromPoints)
            {
                // Cached mapping may be stale after an NWS regrid — re-resolve once.
                (grid, _) = await ResolveGridAsync(location, forceRefresh: true, ct);
                if (grid is null) return null;
                snap = await FetchGridpointAsync(grid, ct);
            }
            return snap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NWS fetch failed for {Lat},{Lon}", location.Lat, location.Lon);
            return null;
        }
    }

    private async Task<(GridRef? Grid, bool FromPointsCall)> ResolveGridAsync(
        ForecastLocation location, bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh)
        {
            var row = await _cache.GetAsync(location.RouteId, GridCacheSource, ct);
            if (row is not null && row.ExpiresAtUtc > DateTime.UtcNow)
            {
                var cached = JsonSerializer.Deserialize<GridRef>(row.PayloadJson, JsonOpts);
                if (cached is not null) return (cached, false);
            }
        }

        _calls.Increment("NWS");
        using var resp = await _http.GetAsync($"points/{location.Lat:0.0000},{location.Lon:0.0000}", ct);
        resp.EnsureSuccessStatusCode();
        var point = await resp.Content.ReadFromJsonAsync<NwsPointResponse>(cancellationToken: ct);
        var props = point?.Properties;
        if (props?.GridId is null) return (null, true);

        var grid = new GridRef(props.GridId, props.GridX, props.GridY);
        await _cache.UpsertAsync(location.RouteId, GridCacheSource,
            JsonSerializer.Serialize(grid, JsonOpts), DateTime.UtcNow.Add(GridCacheTtl), ct);
        return (grid, true);
    }

    private async Task<WeatherSnapshot?> FetchGridpointAsync(GridRef grid, CancellationToken ct)
    {
        _calls.Increment("NWS");
        using var resp = await _http.GetAsync($"gridpoints/{grid.GridId}/{grid.GridX},{grid.GridY}", ct);
        // Non-success returns null so a cached-mapping 404 can re-resolve; thrown errors are transient and handled by the caller's catch-all.
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("NWS gridpoints {Status} for {Grid}", (int)resp.StatusCode, grid);
            return null;
        }
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return NwsGridpointParser.Parse(doc.RootElement, DateTimeOffset.UtcNow);
    }

    public sealed record GridRef(string GridId, int GridX, int GridY);

    private sealed class NwsPointResponse
    {
        [JsonPropertyName("properties")] public NwsPointProperties? Properties { get; set; }
    }

    private sealed class NwsPointProperties
    {
        [JsonPropertyName("gridId")] public string? GridId { get; set; }
        [JsonPropertyName("gridX")] public int GridX { get; set; }
        [JsonPropertyName("gridY")] public int GridY { get; set; }
    }
}
