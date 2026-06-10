using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RouteWeather.Core.Models;
using RouteWeather.Core.Services;

namespace RouteWeather.API.Services;

public class OpenMeteoClient
{
    private const string GfsSourceName = "OpenMeteo-GFS";
    private const string GfsModelKey = "gfs_global";

    public static IReadOnlyList<(string SourceName, string ModelKey)> Models { get; } = new[]
    {
        (GfsSourceName,     GfsModelKey),
        ("OpenMeteo-ECMWF", "ecmwf_ifs025"),
        ("OpenMeteo-ICON",  "icon_global"),
        ("OpenMeteo-HRRR",  "gfs_hrrr"),
    };

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<string, WeatherSnapshot>>>> _inflight = new();

    private readonly HttpClient _http;
    private readonly ILogger<OpenMeteoClient> _logger;
    private readonly DailyCallCounter _calls;

    public OpenMeteoClient(HttpClient http, ILogger<OpenMeteoClient> logger, DailyCallCounter calls)
    {
        _http = http;
        _logger = logger;
        _calls = calls;
    }

    public Task<IReadOnlyDictionary<string, WeatherSnapshot>> FetchAllModelsAsync(double lat, double lon, int summitElevationFt, CancellationToken ct = default)
    {
        var key = $"{lat:F4},{lon:F4}";
        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<IReadOnlyDictionary<string, WeatherSnapshot>>>(() => FetchImpl(lat, lon, summitElevationFt, ct)));
        var task = lazy.Value;
        task.ContinueWith(_ => _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<IReadOnlyDictionary<string, WeatherSnapshot>>>>(key, lazy)), TaskScheduler.Default);
        return task;
    }

    private async Task<IReadOnlyDictionary<string, WeatherSnapshot>> FetchImpl(double lat, double lon, int summitElevationFt, CancellationToken ct)
    {
        var primaryTask = FetchPrimaryAsync(lat, lon, summitElevationFt, ct);
        var gfsProbTask = FetchGfsProbabilityAsync(lat, lon, summitElevationFt, ct);
        await Task.WhenAll(primaryTask, gfsProbTask);

        var primary = primaryTask.Result;
        if (primary is null) return EmptyResult();

        var result = new Dictionary<string, WeatherSnapshot>(Models.Count);
        foreach (var (sourceName, modelKey) in Models)
        {
            var snapshot = BuildSnapshot(primary, modelKey);
            if (snapshot is null) continue;

            if (sourceName == GfsSourceName)
            {
                snapshot = OverlayGfsProbability(snapshot, gfsProbTask.Result);
            }

            result[sourceName] = snapshot;
        }
        return result;
    }

    private async Task<OpenMeteoHourly?> FetchPrimaryAsync(double lat, double lon, int summitElevationFt, CancellationToken ct)
    {
        var modelsParam = string.Join(',', Models.Select(m => m.ModelKey));
        var url = BuildUrl(lat, lon, summitElevationFt,
            "temperature_2m,precipitation,wind_speed_10m,wind_gusts_10m,cape,cloud_cover,visibility,apparent_temperature,weather_code",
            modelsParam);

        try
        {
            _calls.Increment("OpenMeteo");
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("OpenMeteo primary {Status} for {Lat},{Lon}: {Body}",
                    (int)resp.StatusCode, lat, lon, Truncate(body, 400));
                return null;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOpts, ct);
            return payload?.Hourly;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenMeteo primary fetch failed for {Lat},{Lon}", lat, lon);
            return null;
        }
    }

    private async Task<GfsProbabilityResult?> FetchGfsProbabilityAsync(double lat, double lon, int summitElevationFt, CancellationToken ct)
    {
        var url = BuildUrl(lat, lon, summitElevationFt, "precipitation_probability", GfsModelKey);

        try
        {
            _calls.Increment("OpenMeteo-GFSProb");
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("OpenMeteo GFS probability {Status} for {Lat},{Lon}: {Body}",
                    (int)resp.StatusCode, lat, lon, Truncate(body, 400));
                return null;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOpts, ct);
            if (payload?.Hourly?.Time is null) return null;

            // Single-model response uses unsuffixed field names.
            if (!payload.Hourly.Series.TryGetValue("precipitation_probability", out var prob)) return null;
            return new GfsProbabilityResult(payload.Hourly.Time, prob);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenMeteo GFS probability fetch failed for {Lat},{Lon}", lat, lon);
            return null;
        }
    }

    private static string BuildUrl(double lat, double lon, int summitElevationFt, string hourly, string models) =>
        "v1/forecast" +
        $"?latitude={lat.ToString("F4", CultureInfo.InvariantCulture)}" +
        $"&longitude={lon.ToString("F4", CultureInfo.InvariantCulture)}" +
        $"&elevation={(summitElevationFt * 0.3048).ToString("F0", CultureInfo.InvariantCulture)}" +
        $"&hourly={hourly}" +
        $"&models={models}" +
        "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch" +
        "&forecast_days=2&timezone=UTC";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

    private static IReadOnlyDictionary<string, WeatherSnapshot> EmptyResult() =>
        new Dictionary<string, WeatherSnapshot>(0);

    private static WeatherSnapshot? BuildSnapshot(OpenMeteoHourly hourly, string modelKey)
    {
        var times = hourly.Time ?? new List<DateTimeOffset>();
        if (times.Count == 0) return null;

        if (!hourly.Series.TryGetValue($"temperature_2m_{modelKey}", out var tempSeries) || tempSeries is null || tempSeries.Count == 0)
            return null;
        if (!hourly.Series.TryGetValue($"wind_speed_10m_{modelKey}", out var windSeries))
            windSeries = null;

        hourly.Series.TryGetValue($"wind_gusts_10m_{modelKey}", out var gustSeries);
        hourly.Series.TryGetValue($"cape_{modelKey}", out var capeSeries);
        hourly.Series.TryGetValue($"precipitation_{modelKey}", out var precipSeries);
        hourly.Series.TryGetValue($"cloud_cover_{modelKey}", out var cloudSeries);
        hourly.Series.TryGetValue($"visibility_{modelKey}", out var visSeries);
        hourly.Series.TryGetValue($"apparent_temperature_{modelKey}", out var apparentSeries);
        hourly.Series.TryGetValue($"weather_code_{modelKey}", out var codeSeries);

        var count = Math.Min(48, Math.Min(times.Count, tempSeries.Count));
        if (count == 0) return null;

        var hourly48 = new List<HourlyForecast>(count);
        for (var i = 0; i < count; i++)
        {
            var t = tempSeries[i];
            if (!t.HasValue) continue;
            var w = windSeries is not null && i < windSeries.Count ? (windSeries[i] ?? 0.0) : 0.0;
            var code = At(codeSeries, i);
            var visMeters = At(visSeries, i);

            hourly48.Add(new HourlyForecast(
                Time: times[i],
                TempF: t.Value,
                WindMph: w,
                PrecipitationProbabilityPct: 0,
                ShortForecast: code is null ? string.Empty : WmoWeatherText.For((int)code.Value),
                GustMph: At(gustSeries, i),
                CapeJkg: At(capeSeries, i),
                PrecipitationIn: At(precipSeries, i),
                CloudCoverPct: At(cloudSeries, i) is double c ? (int)Math.Round(c) : null,
                VisibilityMiles: visMeters is null ? null : Math.Round(visMeters.Value / 1609.34, 1),
                ApparentTempF: At(apparentSeries, i)));
        }

        if (hourly48.Count == 0) return null;

        var gusts = hourly48.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var capes = hourly48.Where(h => h.CapeJkg.HasValue).Select(h => h.CapeJkg!.Value).ToList();
        var amounts = hourly48.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: hourly48.Max(h => h.WindMph),
            TempF: hourly48.Min(h => h.TempF),
            PrecipitationProbabilityPct: 0,
            Next48Hours: hourly48,
            MaxGustMph: gusts.Count == 0 ? null : gusts.Max(),
            MaxCapeJkg: capes.Count == 0 ? null : capes.Max(),
            PrecipAmountIn: amounts.Count == 0 ? null : amounts.Sum());
    }

    private static double? At(List<double?>? series, int i) =>
        series is not null && i < series.Count ? series[i] : null;

    private static WeatherSnapshot OverlayGfsProbability(WeatherSnapshot snapshot, GfsProbabilityResult? prob)
    {
        if (prob is null) return snapshot;

        var probByTime = new Dictionary<DateTimeOffset, int>(prob.Times.Count);
        for (var i = 0; i < prob.Times.Count && i < prob.Probability.Count; i++)
        {
            var p = prob.Probability[i];
            if (p.HasValue) probByTime[prob.Times[i]] = (int)Math.Round(Math.Clamp(p.Value, 0, 100));
        }
        if (probByTime.Count == 0) return snapshot;

        var hourly = snapshot.Next48Hours.Select(h =>
        {
            return probByTime.TryGetValue(h.Time, out var pct)
                ? h with { PrecipitationProbabilityPct = pct }
                : h;
        }).ToList();

        return snapshot with
        {
            PrecipitationProbabilityPct = hourly.Max(h => h.PrecipitationProbabilityPct),
            Next48Hours = hourly,
        };
    }

    private sealed record GfsProbabilityResult(List<DateTimeOffset> Times, List<double?> Probability);

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("hourly")] public OpenMeteoHourly? Hourly { get; set; }
    }

    [JsonConverter(typeof(OpenMeteoHourlyConverter))]
    public sealed class OpenMeteoHourly
    {
        public List<DateTimeOffset>? Time { get; set; }
        public Dictionary<string, List<double?>> Series { get; set; } = new();
    }

    private sealed class OpenMeteoHourlyConverter : JsonConverter<OpenMeteoHourly>
    {
        public override OpenMeteoHourly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
            var hourly = new OpenMeteoHourly();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return hourly;
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
                var name = reader.GetString();
                reader.Read();

                if (name == "time")
                {
                    hourly.Time = JsonSerializer.Deserialize<List<DateTimeOffset>>(ref reader, options);
                }
                else
                {
                    var arr = JsonSerializer.Deserialize<List<double?>>(ref reader, options) ?? new List<double?>();
                    hourly.Series[name ?? string.Empty] = arr;
                }
            }
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, OpenMeteoHourly value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }
}
