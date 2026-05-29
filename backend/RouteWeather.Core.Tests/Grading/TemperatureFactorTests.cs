using RouteWeather.Core.Grading;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class TemperatureFactorTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    public void Score_returns100_inComfortableBand(double tempF) =>
        Assert.Equal(100, TemperatureFactor.Score(tempF));

    [Fact]
    public void Score_returns0_atFreezingExtreme() =>
        Assert.Equal(0, TemperatureFactor.Score(-20));

    [Fact]
    public void Score_returns0_atHotExtreme() =>
        Assert.Equal(0, TemperatureFactor.Score(90));

    [Fact]
    public void Score_decreases_belowFloorAndAboveCeiling()
    {
        Assert.True(TemperatureFactor.Score(0) < 100);
        Assert.True(TemperatureFactor.Score(80) < 100);
    }
}
