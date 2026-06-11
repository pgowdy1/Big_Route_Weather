using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RouteWeather.Core.Models;
using RouteWeather.Core.Services;
using RouteWeather.Core.Sources;

namespace RouteWeather.API.Services;

public class AirQualityClient : IAirQualitySource
{
    public string Name => "AirQuality";

    private readonly HttpClient _http;
    private readonly ILogger<AirQualityClient> _logger;
    private readonly DailyCallCounter _calls;

    public AirQualityClient(HttpClient http, ILogger<AirQualityClient> logger, DailyCallCounter calls)
    {
        _http = http;
        _logger = logger;
        _calls = calls;
    }

    public async Task<AirQualitySnapshot?> FetchAsync(double lat, double lon, CancellationToken ct)
    {
        var url = "v1/air-quality" +
                  $"?latitude={lat.ToString("F4", CultureInfo.InvariantCulture)}" +
                  $"&longitude={lon.ToString("F4", CultureInfo.InvariantCulture)}" +
                  "&current=us_aqi,pm2_5";
        try
        {
            _calls.Increment("AirQuality");
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("AirQuality {Status} for {Lat},{Lon}", (int)resp.StatusCode, lat, lon);
                return null;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<AirQualityResponse>(stream, cancellationToken: ct);
            var current = payload?.Current;
            if (current?.UsAqi is null) return null;
            return new AirQualitySnapshot((int)Math.Round(current.UsAqi.Value), current.Pm25 ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AirQuality fetch failed for {Lat},{Lon}", lat, lon);
            return null;
        }
    }

    private sealed class AirQualityResponse
    {
        [JsonPropertyName("current")] public CurrentBlock? Current { get; set; }
    }

    private sealed class CurrentBlock
    {
        [JsonPropertyName("us_aqi")] public double? UsAqi { get; set; }
        [JsonPropertyName("pm2_5")] public double? Pm25 { get; set; }
    }
}
