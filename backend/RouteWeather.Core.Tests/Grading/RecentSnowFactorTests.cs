using RouteWeather.Core.Grading;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class RecentSnowFactorTests
{
    [Fact]
    public void Score_returns100_atZero() =>
        Assert.Equal(100, RecentSnowFactor.Score(0));

    [Fact]
    public void Score_returns0_atOrAbove6Inches()
    {
        Assert.Equal(0, RecentSnowFactor.Score(6));
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
}
