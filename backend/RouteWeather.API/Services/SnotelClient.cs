using System.Text.Json;
using System.Text.Json.Serialization;
using RouteWeather.Core.Models;

namespace RouteWeather.API.Services;

public class SnotelClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SnotelClient> _logger;

    public SnotelClient(HttpClient http, ILogger<SnotelClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<SnowpackSnapshot?> GetSnowpackAsync(string stationTriplet, CancellationToken ct = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var weekAgo = today.AddDays(-7).ToString("yyyy-MM-dd");
            var nowStr = today.ToString("yyyy-MM-dd");

            var url = "data?stationTriplets=" + Uri.EscapeDataString(stationTriplet) +
                      "&elements=WTEQ,SNWD&duration=DAILY" +
                      $"&beginDate={weekAgo}&endDate={nowStr}" +
                      "&periodRef=END&centralTendencyType=NONE&returnFlags=false&returnOriginalValues=false&returnSuspectData=false";

            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var stations = await resp.Content.ReadFromJsonAsync<List<SnotelStationData>>(cancellationToken: ct);
            var station = stations?.FirstOrDefault();
            if (station?.Data is null || station.Data.Count == 0) return null;

            var wteq = station.Data.FirstOrDefault(d => d.StationElement?.ElementCode == "WTEQ");
            var snwd = station.Data.FirstOrDefault(d => d.StationElement?.ElementCode == "SNWD");

            var wteqValues = wteq?.Values ?? new List<SnotelValue>();
            var snwdValues = snwd?.Values ?? new List<SnotelValue>();

            var latestSwe = wteqValues.LastOrDefault(v => v.Value.HasValue)?.Value ?? 0;
            var latestDepth = snwdValues.LastOrDefault(v => v.Value.HasValue)?.Value ?? 0;
            var oldestDepth = snwdValues.FirstOrDefault(v => v.Value.HasValue)?.Value ?? latestDepth;
            var newSnow = Math.Max(0, latestDepth - oldestDepth);

            return new SnowpackSnapshot(
                SnowWaterEquivalentIn: latestSwe,
                SnowDepthIn: latestDepth,
                NewSnowLast7DaysIn: newSnow,
                PercentOfNormalSwe: 100);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SNOTEL fetch failed for {Triplet}", stationTriplet);
            return null;
        }
    }

    private sealed class SnotelStationData
    {
        [JsonPropertyName("data")] public List<SnotelElementData>? Data { get; set; }
    }

    private sealed class SnotelElementData
    {
        [JsonPropertyName("stationElement")] public SnotelStationElement? StationElement { get; set; }
        [JsonPropertyName("values")] public List<SnotelValue>? Values { get; set; }
    }

    private sealed class SnotelStationElement
    {
        [JsonPropertyName("elementCode")] public string? ElementCode { get; set; }
    }

    private sealed class SnotelValue
    {
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("value")] public double? Value { get; set; }
    }
}
