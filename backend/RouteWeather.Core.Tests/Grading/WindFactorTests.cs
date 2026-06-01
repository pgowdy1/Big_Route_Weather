using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class WindFactorTests
{
    [Fact]
    public void Score_returns100_atOrBelowGoodThreshold()
    {
        Assert.Equal(100, WindFactor.Score(0));
        Assert.Equal(100, WindFactor.Score(10));
    }

    [Fact]
    public void Score_returns0_atOrAboveBadThreshold()
    {
        Assert.Equal(0, WindFactor.Score(40));
        Assert.Equal(0, WindFactor.Score(120));
    }

    [Fact]
    public void Score_isMonotonicallyDecreasing()
    {
        var prev = 101;
        for (var mph = 0; mph <= 60; mph += 5)
        {
            var s = WindFactor.Score(mph);
            Assert.True(s <= prev, $"Score went up at {mph} mph: {prev} -> {s}");
            prev = s;
        }
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(20, null)]
    [InlineData(21, Grade.B)]
    [InlineData(30, Grade.B)]
    [InlineData(31, Grade.C)]
    [InlineData(40, Grade.C)]
    [InlineData(41, Grade.D)]
    [InlineData(50, Grade.D)]
    [InlineData(51, Grade.F)]
    [InlineData(100, Grade.F)]
    public void Cap_appliesAtCorrectThresholds(double mph, Grade? expected) =>
        Assert.Equal(expected, WindFactor.Cap(mph).Cap);

    [Fact]
    public void Cap_reasonMentionsWindMph() =>
        Assert.Contains("25", WindFactor.Cap(25).Reason);
}
