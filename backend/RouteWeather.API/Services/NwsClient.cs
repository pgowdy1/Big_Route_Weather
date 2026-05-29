using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using RouteWeather.Core.Models;

namespace RouteWeather.API.Services;

public class NwsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<NwsClient> _logger;

    public NwsClient(HttpClient http, ILogger<NwsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<WeatherSnapshot?> GetForecastAsync(double lat, double lon, CancellationToken ct = default)
    {
        try
        {
            using var pointResp = await _http.GetAsync($"points/{lat:0.0000},{lon:0.0000}", ct);
            pointResp.EnsureSuccessStatusCode();
            var pointJson = await pointResp.Content.ReadFromJsonAsync<NwsPointResponse>(cancellationToken: ct);
            var hourlyUrl = pointJson?.Properties?.ForecastHourly;
            if (string.IsNullOrWhiteSpace(hourlyUrl)) return null;

            using var fcastResp = await _http.GetAsync(hourlyUrl, ct);
            fcastResp.EnsureSuccessStatusCode();
            var fcastJson = await fcastResp.Content.ReadFromJsonAsync<NwsForecastResponse>(cancellationToken: ct);
            var periods = fcastJson?.Properties?.Periods ?? new List<NwsPeriod>();
            var next48 = periods.Take(48)
                .Select(p => new HourlyForecast(
                    p.StartTime,
                    p.Temperature,
                    ParseWindMph(p.WindSpeed),
                    p.ProbabilityOfPrecipitation?.Value ?? 0,
                    p.ShortForecast ?? string.Empty))
                .ToList();

            if (next48.Count == 0) return null;

            return new WeatherSnapshot(
                WindMph: next48.Max(h => h.WindMph),
                TempF: next48.Min(h => h.TempF),
                PrecipitationProbabilityPct: next48.Max(h => h.PrecipitationProbabilityPct),
                Next48Hours: next48);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NWS fetch failed for {Lat},{Lon}", lat, lon);
            return null;
        }
    }

    private static double ParseWindMph(string? windSpeed)
    {
        if (string.IsNullOrWhiteSpace(windSpeed)) return 0;
        var token = windSpeed.Split(' ').FirstOrDefault(s => double.TryParse(s, out _));
        return double.TryParse(token, out var mph) ? mph : 0;
    }

    private sealed class NwsPointResponse
    {
        [JsonPropertyName("properties")] public NwsPointProperties? Properties { get; set; }
    }

    private sealed class NwsPointProperties
    {
        [JsonPropertyName("forecast")] public string? Forecast { get; set; }
        [JsonPropertyName("forecastHourly")] public string? ForecastHourly { get; set; }
    }

    private sealed class NwsForecastResponse
    {
        [JsonPropertyName("properties")] public NwsForecastProperties? Properties { get; set; }
    }

    private sealed class NwsForecastProperties
    {
        [JsonPropertyName("periods")] public List<NwsPeriod>? Periods { get; set; }
    }

    private sealed class NwsPeriod
    {
        [JsonPropertyName("startTime")] public DateTimeOffset StartTime { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("windSpeed")] public string? WindSpeed { get; set; }
        [JsonPropertyName("shortForecast")] public string? ShortForecast { get; set; }
        [JsonPropertyName("probabilityOfPrecipitation")] public NwsValue? ProbabilityOfPrecipitation { get; set; }
    }

    private sealed class NwsValue
    {
        [JsonPropertyName("value")] public int? Value { get; set; }
    }
}
