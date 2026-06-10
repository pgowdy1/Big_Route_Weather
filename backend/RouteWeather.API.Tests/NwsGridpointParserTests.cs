using System.Text.Json;
using RouteWeather.API.Services;
using Xunit;

namespace RouteWeather.API.Tests;

public class NwsGridpointParserTests
{
    private const string SampleJson = """
    {
      "properties": {
        "temperature": { "uom": "wmoUnit:degC", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT2H", "value": 10.0 },
          { "validTime": "2026-06-10T14:00:00+00:00/PT1H", "value": 12.0 } ] },
        "windSpeed": { "uom": "wmoUnit:km_h-1", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 16.0 } ] },
        "windGust": { "uom": "wmoUnit:km_h-1", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 48.0 } ] },
        "probabilityOfPrecipitation": { "uom": "wmoUnit:percent", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 40 } ] },
        "quantitativePrecipitation": { "uom": "wmoUnit:mm", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT2H", "value": 5.08 } ] },
        "skyCover": { "uom": "wmoUnit:percent", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 75 } ] },
        "visibility": { "uom": "wmoUnit:m", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 16093.44 } ] },
        "apparentTemperature": { "uom": "wmoUnit:degC", "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H", "value": 7.0 } ] },
        "weather": { "values": [
          { "validTime": "2026-06-10T12:00:00+00:00/PT3H",
            "value": [ { "coverage": "likely", "weather": "snow_showers", "intensity": "light" } ] } ] }
      }
    }
    """;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-10T12:00:00+00:00");

    [Fact]
    public void Parse_includesOnlyHoursWithTemperature()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        var snap = NwsGridpointParser.Parse(doc.RootElement, Now);

        Assert.NotNull(snap);
        Assert.Equal(3, snap!.Next48Hours.Count); // 12:00, 13:00 (PT2H), 14:00 (PT1H)
    }

    [Fact]
    public void Parse_convertsUnits()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        var h0 = NwsGridpointParser.Parse(doc.RootElement, Now)!.Next48Hours[0];

        Assert.Equal(50.0, h0.TempF, 1);                 // 10 C
        Assert.Equal(9.9, h0.WindMph, 1);                // 16 km/h
        Assert.Equal(29.8, h0.GustMph!.Value, 1);        // 48 km/h
        Assert.Equal(40, h0.PrecipitationProbabilityPct);
        Assert.Equal(0.1, h0.PrecipitationIn!.Value, 2); // 5.08 mm over PT2H = 0.2" / 2h
        Assert.Equal(75, h0.CloudCoverPct);
        Assert.Equal(10.0, h0.VisibilityMiles!.Value, 1);
        Assert.Equal(44.6, h0.ApparentTempF!.Value, 1);  // 7 C
    }

    [Fact]
    public void Parse_buildsConditionsTextFromWeatherLayer()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        var h0 = NwsGridpointParser.Parse(doc.RootElement, Now)!.Next48Hours[0];
        Assert.Equal("Snow showers", h0.ShortForecast);
    }

    [Fact]
    public void Parse_computesHeadlines()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        var snap = NwsGridpointParser.Parse(doc.RootElement, Now)!;

        Assert.Equal(50.0, snap.TempF, 1);               // min temp
        Assert.Equal(29.8, snap.MaxGustMph!.Value, 1);
        Assert.Equal(0.2, snap.PrecipAmountIn!.Value, 2); // both QPF hours included
        Assert.Null(snap.MaxCapeJkg);                     // NWS has no CAPE
    }

    [Fact]
    public void Parse_missingTemperatureLayer_returnsNull()
    {
        using var doc = JsonDocument.Parse("""{ "properties": { } }""");
        Assert.Null(NwsGridpointParser.Parse(doc.RootElement, Now));
    }

    [Fact]
    public void Parse_toleratesMalformedLayers()
    {
        const string hostile = """
        {
          "properties": {
            "temperature": { "uom": "wmoUnit:degC", "values": [
              { "validTime": "2026-06-10T12:00:00+00:00/PT1H", "value": 10.0 },
              { "validTime": "not-a-date/PT1H", "value": 99.0 },
              { "validTime": "2026-06-10T13:00:00+00:00/P10675200D", "value": 99.0 } ] },
            "windSpeed": { "uom": 5, "values": [
              { "validTime": "2026-06-10T12:00:00+00:00/PT1H", "value": 16.0 } ] },
            "skyCover": { "uom": "wmoUnit:percent", "values": {} },
            "visibility": 5,
            "weather": { "values": [
              { "validTime": "2026-06-10T12:00:00+00:00/PT1H", "value": [ null, 5 ] } ] }
          }
        }
        """;
        using var doc = JsonDocument.Parse(hostile);
        var snap = NwsGridpointParser.Parse(doc.RootElement, DateTimeOffset.Parse("2026-06-10T12:00:00+00:00"));
        Assert.NotNull(snap);
        Assert.Single(snap!.Next48Hours); // only the one well-formed temperature hour survives
    }

    [Theory]
    [InlineData("""{ "properties": 5 }""")]
    [InlineData("""{ "properties": [] }""")]
    [InlineData("5")]
    [InlineData("[]")]
    public void Parse_nonObjectRootOrProperties_returnsNullWithoutThrowing(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Null(NwsGridpointParser.Parse(doc.RootElement, Now));
    }

    [Fact]
    public void Parse_normalizesNonUtcNow()
    {
        using var doc = JsonDocument.Parse(SampleJson);
        // Same instant as Now but expressed at -04:00; must yield the identical window.
        var offsetNow = DateTimeOffset.Parse("2026-06-10T08:00:00-04:00");
        var snap = NwsGridpointParser.Parse(doc.RootElement, offsetNow);
        Assert.NotNull(snap);
        Assert.Equal(3, snap!.Next48Hours.Count);
    }
}
