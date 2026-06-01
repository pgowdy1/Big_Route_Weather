using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
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

    [Theory]
    [InlineData(20, null)]
    [InlineData(60, null)]
    [InlineData(10, null)]
    [InlineData(75, null)]
    [InlineData(9, Grade.B)]
    [InlineData(76, Grade.B)]
    [InlineData(0, Grade.B)]
    [InlineData(-1, Grade.C)]
    [InlineData(86, Grade.C)]
    [InlineData(-15, Grade.C)]
    [InlineData(-16, Grade.D)]
    [InlineData(96, Grade.D)]
    public void Cap_appliesSymmetricThresholds(double tempF, Grade? expected) =>
        Assert.Equal(expected, TemperatureFactor.Cap(tempF).Cap);
}
