using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class RecentSnowFactorTests
{
    [Fact]
    public void Score_returns100_atZero() =>
        Assert.Equal(100, RecentSnowFactor.Score(0));

    [Fact]
    public void Score_returns0_atOrAbove4Inches()
    {
        Assert.Equal(0, RecentSnowFactor.Score(4));
        Assert.Equal(0, RecentSnowFactor.Score(20));
    }

    [Fact]
    public void Score_isMonotonicallyDecreasing()
    {
        var prev = 101;
        for (var inches = 0.0; inches <= 7; inches += 0.5)
        {
            var s = RecentSnowFactor.Score(inches);
            Assert.True(s <= prev, $"Score went up at {inches}\": {prev} -> {s}");
            prev = s;
        }
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(2, null)]
    [InlineData(2.1, Grade.B)]
    [InlineData(4, Grade.B)]
    [InlineData(4.1, Grade.C)]
    [InlineData(8, Grade.C)]
    [InlineData(8.1, Grade.D)]
    [InlineData(20, Grade.D)]
    public void Cap_appliesAtCorrectThresholds(double inches, Grade? expected) =>
        Assert.Equal(expected, RecentSnowFactor.Cap(inches).Cap);
}
