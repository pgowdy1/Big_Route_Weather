using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RouteWeather.API.Services;
using RouteWeather.Core.Services;
using RouteWeather.Core.Sources;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class NwsClientTests
{
    private const string PointsJson = """
    { "properties": { "gridId": "SEW", "gridX": 124, "gridY": 69 } }
    """;

    // Minimal gridpoint body the parser accepts (one temperature interval starting now).
    private static string GridJson()
    {
        var now = DateTime.UtcNow;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day,
            now.Hour, 0, 0, TimeSpan.Zero).ToString("yyyy-MM-dd'T'HH:mm:sszzz",
            System.Globalization.CultureInfo.InvariantCulture);
        return $$"""
        { "properties": { "temperature": { "uom": "wmoUnit:degC", "values": [
            { "validTime": "{{start}}/PT4H", "value": 10.0 } ] } } }
        """;
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        public Func<string, HttpResponseMessage> Respond { get; set; } = _ => Json("{}");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            return Task.FromResult(Respond(path));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/geo+json") };

    private static readonly ForecastLocation Loc = new(RouteId: 1, Lat: 46.85, Lon: -121.76, SummitElevationFt: 14411);

    private static bool IsPointsPath(string path) => path.StartsWith("/points/", StringComparison.Ordinal);

    private static (NwsClient Client, ForecastCacheRepository Cache, ScriptedHandler Handler) Build(string dbName)
    {
        var factory = new TestDbContextFactory(dbName);
        var cache = new ForecastCacheRepository(factory);
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.weather.gov/") };
        var client = new NwsClient(http, NullLogger<NwsClient>.Instance, new DailyCallCounter(), cache);
        return (client, cache, handler);
    }

    [Fact]
    public async Task FirstFetch_resolvesPoints_thenGridpoints_andCachesMapping()
    {
        var (client, cache, handler) = Build(nameof(FirstFetch_resolvesPoints_thenGridpoints_andCachesMapping));
        handler.Respond = path => IsPointsPath(path) ? Json(PointsJson) : Json(GridJson());

        var snap = await client.FetchAsync(Loc, CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Equal(
            ["/points/46.8500,-121.7600", "/gridpoints/SEW/124,69"],
            handler.Paths);

        var row = await cache.GetAsync(1, "NWS-Grid");
        Assert.NotNull(row);
        var grid = JsonSerializer.Deserialize<NwsClient.GridRef>(row!.PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(grid);
        Assert.Equal("SEW", grid!.GridId);
        Assert.Equal(124, grid.GridX);
        Assert.Equal(69, grid.GridY);
    }

    [Fact]
    public async Task SecondFetch_usesCachedMapping_singleCall()
    {
        var (client, _, handler) = Build(nameof(SecondFetch_usesCachedMapping_singleCall));
        handler.Respond = path => IsPointsPath(path) ? Json(PointsJson) : Json(GridJson());

        // Warm the mapping.
        var first = await client.FetchAsync(Loc, CancellationToken.None);
        Assert.NotNull(first);
        handler.Paths.Clear();

        var snap = await client.FetchAsync(Loc, CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Equal(["/gridpoints/SEW/124,69"], handler.Paths);
    }

    [Fact]
    public async Task Gridpoints404_withCachedMapping_reResolvesOnce()
    {
        var (client, cache, handler) = Build(nameof(Gridpoints404_withCachedMapping_reResolvesOnce));

        // Pre-seed a stale mapping that NWS has since regridded away.
        var stale = JsonSerializer.Serialize(new NwsClient.GridRef("OLD", 1, 1),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        await cache.UpsertAsync(1, "NWS-Grid", stale, DateTime.UtcNow.AddDays(365));

        handler.Respond = path => path switch
        {
            "/gridpoints/OLD/1,1" => Json("{}", HttpStatusCode.NotFound),
            _ when IsPointsPath(path) => Json(PointsJson),
            _ => Json(GridJson()),
        };

        var snap = await client.FetchAsync(Loc, CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Equal(
            ["/gridpoints/OLD/1,1", "/points/46.8500,-121.7600", "/gridpoints/SEW/124,69"],
            handler.Paths);

        var row = await cache.GetAsync(1, "NWS-Grid");
        Assert.NotNull(row);
        Assert.Contains("SEW", row!.PayloadJson);
    }

    [Fact]
    public async Task PointsFailure_returnsNull()
    {
        var (client, _, handler) = Build(nameof(PointsFailure_returnsNull));
        handler.Respond = _ => Json("{}", HttpStatusCode.InternalServerError);

        var snap = await client.FetchAsync(Loc, CancellationToken.None);

        Assert.Null(snap);
    }
}
