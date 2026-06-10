using System.Text.Json;
using System.Xml;
using RouteWeather.Core.Models;

namespace RouteWeather.API.Services;

/// <summary>
/// Parses the raw NWS gridpoints payload (layered sparse intervals with
/// "validTime": "start/ISO8601-duration") into a WeatherSnapshot. Hours without
/// a temperature value are excluded; quantitativePrecipitation interval totals
/// are spread evenly across their hours.
/// </summary>
public static class NwsGridpointParser
{
    /// <param name="nowUtc">Any offset accepted; normalized to UTC internally.</param>
    public static WeatherSnapshot? Parse(JsonElement root, DateTimeOffset nowUtc)
    {
        nowUtc = nowUtc.ToUniversalTime();
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("properties", out var props)) return null;
        if (props.ValueKind != JsonValueKind.Object) return null;

        var temp = Layer(props, "temperature", spread: false);
        if (temp.Count == 0) return null;

        var wind = Layer(props, "windSpeed", spread: false);
        var gust = Layer(props, "windGust", spread: false);
        var pop = Layer(props, "probabilityOfPrecipitation", spread: false);
        var qpf = Layer(props, "quantitativePrecipitation", spread: true);
        var sky = Layer(props, "skyCover", spread: false);
        var vis = Layer(props, "visibility", spread: false);
        var apparent = Layer(props, "apparentTemperature", spread: false);
        var weather = WeatherTextLayer(props);

        var start = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, TimeSpan.Zero);
        var hours = new List<HourlyForecast>(48);
        for (var i = 0; i < 48; i++)
        {
            var t = start.AddHours(i);
            if (!temp.Values.TryGetValue(t, out var tempVal)) continue;

            hours.Add(new HourlyForecast(
                Time: t,
                TempF: ToImperial(tempVal, temp.Uom),
                WindMph: wind.Values.TryGetValue(t, out var w) ? ToImperial(w, wind.Uom) : 0,
                PrecipitationProbabilityPct: pop.Values.TryGetValue(t, out var p) ? (int)Math.Round(p) : 0,
                ShortForecast: weather.TryGetValue(t, out var text) ? text : string.Empty,
                GustMph: gust.Values.TryGetValue(t, out var g) ? ToImperial(g, gust.Uom) : null,
                CapeJkg: null,
                PrecipitationIn: qpf.Values.TryGetValue(t, out var q) ? ToImperial(q, qpf.Uom) : null,
                CloudCoverPct: sky.Values.TryGetValue(t, out var s) ? (int)Math.Round(s) : null,
                VisibilityMiles: vis.Values.TryGetValue(t, out var v) ? Math.Round(ToImperial(v, vis.Uom), 1) : null,
                ApparentTempF: apparent.Values.TryGetValue(t, out var a) ? ToImperial(a, apparent.Uom) : null));
        }

        if (hours.Count == 0) return null;

        var gusts = hours.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var amounts = hours.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: hours.Max(h => h.WindMph),
            TempF: hours.Min(h => h.TempF),
            PrecipitationProbabilityPct: hours.Max(h => h.PrecipitationProbabilityPct),
            Next48Hours: hours,
            MaxGustMph: gusts.Count == 0 ? null : gusts.Max(),
            MaxCapeJkg: null,
            PrecipAmountIn: amounts.Count == 0 ? null : amounts.Sum());
    }

    private sealed record LayerData(string Uom, Dictionary<DateTimeOffset, double> Values)
    {
        public int Count => Values.Count;
    }

    private static LayerData Layer(JsonElement props, string name, bool spread)
    {
        var values = new Dictionary<DateTimeOffset, double>();
        if (!props.TryGetProperty(name, out var layer) || layer.ValueKind != JsonValueKind.Object)
            return new LayerData(string.Empty, values);

        var uom = layer.TryGetProperty("uom", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? string.Empty : string.Empty;
        if (!layer.TryGetProperty("values", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new LayerData(uom, values);

        foreach (var entry in arr.EnumerateArray())
        {
            if (!entry.TryGetProperty("value", out var valEl) || valEl.ValueKind != JsonValueKind.Number) continue;
            var (intervalStart, durationHours) = ParseValidTime(entry);
            if (intervalStart is null || durationHours <= 0) continue;

            var value = valEl.GetDouble();
            var perHour = spread ? value / durationHours : value;
            for (var h = 0; h < durationHours; h++)
                values[intervalStart.Value.AddHours(h)] = perHour; // overlapping intervals: last interval wins
        }
        return new LayerData(uom, values);
    }

    private static Dictionary<DateTimeOffset, string> WeatherTextLayer(JsonElement props)
    {
        var result = new Dictionary<DateTimeOffset, string>();
        if (!props.TryGetProperty("weather", out var layer)
            || layer.ValueKind != JsonValueKind.Object
            || !layer.TryGetProperty("values", out var arr)
            || arr.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var entry in arr.EnumerateArray())
        {
            var (intervalStart, durationHours) = ParseValidTime(entry);
            if (intervalStart is null || durationHours <= 0) continue;
            if (!entry.TryGetProperty("value", out var phenomena) || phenomena.ValueKind != JsonValueKind.Array) continue;

            var text = string.Empty;
            foreach (var ph in phenomena.EnumerateArray())
            {
                if (ph.ValueKind != JsonValueKind.Object) continue;
                if (ph.TryGetProperty("weather", out var w) && w.ValueKind == JsonValueKind.String)
                {
                    var raw = w.GetString();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        var pretty = raw.Replace('_', ' ');
                        text = char.ToUpperInvariant(pretty[0]) + pretty[1..];
                        break;
                    }
                }
            }
            if (text.Length == 0) continue;
            for (var h = 0; h < durationHours; h++)
                result[intervalStart.Value.AddHours(h)] = text;
        }
        return result;
    }

    private static (DateTimeOffset? Start, int Hours) ParseValidTime(JsonElement entry)
    {
        if (!entry.TryGetProperty("validTime", out var vt) || vt.ValueKind != JsonValueKind.String)
            return (null, 0);
        var parts = (vt.GetString() ?? string.Empty).Split('/');
        if (parts.Length != 2) return (null, 0);
        if (!DateTimeOffset.TryParse(parts[0], out var start)) return (null, 0);
        try
        {
            var duration = XmlConvert.ToTimeSpan(parts[1]);
            return (start, (int)Math.Max(1, Math.Round(duration.TotalHours)));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return (null, 0);
        }
    }

    private static double ToImperial(double value, string uom) => uom switch
    {
        "wmoUnit:degC" => value * 9.0 / 5.0 + 32.0,
        "wmoUnit:degF" => value,
        "wmoUnit:km_h-1" => value * 0.621371,
        "wmoUnit:m_s-1" => value * 2.23694,
        "wmoUnit:mm" => value / 25.4,
        "wmoUnit:m" => value / 1609.34,
        _ => value,
    };
}
