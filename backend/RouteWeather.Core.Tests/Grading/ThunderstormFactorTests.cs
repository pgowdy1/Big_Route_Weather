using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class ThunderstormFactorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(199, false)]
    [InlineData(200, true)]
    [InlineData(1500, true)]
    public void IsActive_gatesAtFloor(double cape, bool expected) =>
        Assert.Equal(expected, ThunderstormFactor.IsActive(cape));

    [Fact]
    public void Score_returns100_atOrBelowGoodThreshold()
    {
        Assert.Equal(100, ThunderstormFactor.Score(0));
        Assert.Equal(100, ThunderstormFactor.Score(200));
    }

    [Fact]
    public void Score_returns0_atOrAboveBadThreshold()
    {
        Assert.Equal(0, ThunderstormFactor.Score(2000));
        Assert.Equal(0, ThunderstormFactor.Score(4000));
    }

    [Fact]
    public void Score_isMonotonicallyDecreasing()
    {
        var prev = 101;
        for (var cape = 0; cape <= 3000; cape += 100)
        {
            var s = ThunderstormFactor.Score(cape);
            Assert.True(s <= prev, $"Score went up at {cape} J/kg: {prev} -> {s}");
            prev = s;
        }
    }

    [Theory]
    [InlineData(500, null)]
    [InlineData(999, null)]
    [InlineData(1000, Grade.C)]
    [InlineData(1999, Grade.C)]
    [InlineData(2000, Grade.D)]
    [InlineData(3500, Grade.D)]
    public void Cap_appliesAtCorrectThresholds(double cape, Grade? expected) =>
        Assert.Equal(expected, ThunderstormFactor.Cap(cape).Cap);

    [Fact]
    public void Cap_reasonMentionsCape() =>
        Assert.Contains("1200", ThunderstormFactor.Cap(1200).Reason);

    [Fact]
    public void Detail_mentionsCapeValueAndUnit()
    {
        Assert.Contains("1200", ThunderstormFactor.Detail(1200));
        Assert.Contains("CAPE", ThunderstormFactor.Detail(1200));
    }
}
