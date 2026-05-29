using RouteWeather.Core.Grading;
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
        Assert.Equal(0, WindFactor.Score(50));
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
}
