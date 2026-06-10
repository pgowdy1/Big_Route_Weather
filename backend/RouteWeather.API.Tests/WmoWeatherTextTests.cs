using RouteWeather.API.Services;
using Xunit;

namespace RouteWeather.API.Tests;

public class WmoWeatherTextTests
{
    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(3, "Overcast")]
    [InlineData(61, "Rain")]
    [InlineData(75, "Snow")]
    [InlineData(85, "Snow showers")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(-1, "")]
    [InlineData(42, "")]
    public void For_mapsKnownCodes(int code, string expected) =>
        Assert.Equal(expected, WmoWeatherText.For(code));

    [Fact]
    public void SnowCodes_containSnow_forSnowRelevanceMatching()
    {
        foreach (var code in new[] { 71, 73, 75, 77, 85, 86 })
            Assert.Contains("snow", WmoWeatherText.For(code), StringComparison.OrdinalIgnoreCase);
    }
}
