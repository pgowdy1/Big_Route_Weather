using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class GustFactorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(24.9, false)]
    [InlineData(25, true)]
    [InlineData(60, true)]
    public void IsActive_gatesAtFloor(double gust, bool expected) =>
        Assert.Equal(expected, GustFactor.IsActive(gust));

    [Fact]
    public void Score_returns100_atOrBelowGoodThreshold() =>
        Assert.Equal(100, GustFactor.Score(25));

    [Fact]
    public void Score_returns0_atOrAboveBadThreshold() =>
        Assert.Equal(0, GustFactor.Score(55));

    [Fact]
    public void Score_isMidwayBetweenThresholds() =>
        Assert.Equal(50, GustFactor.Score(40));

    [Theory]
    [InlineData(40, null)]
    [InlineData(45, null)]
    [InlineData(46, Grade.C)]
    [InlineData(55, Grade.C)]
    [InlineData(56, Grade.D)]
    [InlineData(70, Grade.D)]
    [InlineData(71, Grade.F)]
    public void Cap_appliesAtCorrectThresholds(double gust, Grade? expected) =>
        Assert.Equal(expected, GustFactor.Cap(gust).Cap);

    [Fact]
    public void Detail_mentionsGustValueAndUnit()
    {
        Assert.Contains("42", GustFactor.Detail(42));
        Assert.Contains("mph", GustFactor.Detail(42));
    }

    [Fact]
    public void Cap_reasonMentionsGustValue() =>
        Assert.Contains("60", GustFactor.Cap(60).Reason);
}
