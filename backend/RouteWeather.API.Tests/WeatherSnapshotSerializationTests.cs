using System.Text.Json;
using RouteWeather.Core.Models;

namespace RouteWeather.API.Tests;

public class WeatherSnapshotSerializationTests
{
    // Mirrors ConditionsAggregator.JsonOpts — the serializer that reads cached rows.
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void Deserializes_legacy_next48Hours_cache_key_into_Hourly()
    {
        var json = """{"windMph":5,"tempF":30,"precipitationProbabilityPct":10,"next48Hours":[{"time":"2026-07-20T06:00:00+00:00","tempF":30,"windMph":5,"precipitationProbabilityPct":10,"shortForecast":"Clear"}]}""";

        var snap = JsonSerializer.Deserialize<WeatherSnapshot>(json, JsonOpts);

        Assert.NotNull(snap);
        Assert.Single(snap!.Hourly);
    }

    [Fact]
    public void Serializes_Hourly_back_to_the_next48Hours_key()
    {
        var snap = new WeatherSnapshot(5, 30, 10, new[] { new HourlyForecast(DateTimeOffset.UtcNow, 30, 5, 10, "Clear") });

        var json = JsonSerializer.Serialize(snap, JsonOpts);

        Assert.Contains("\"next48Hours\"", json);
    }
}
