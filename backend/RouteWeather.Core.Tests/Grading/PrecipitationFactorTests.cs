using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class PrecipitationFactorTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(40, 50)]
    [InlineData(80, 0)]
    [InlineData(100, 0)]
    public void Score_rampsToZeroAt80Percent(int precipPct, int expected) =>
        Assert.Equal(expected, PrecipitationFactor.Score(precipPct));

    [Theory]
    [InlineData(0, null)]
    [InlineData(30, null)]
    [InlineData(31, Grade.B)]
    [InlineData(50, Grade.B)]
    [InlineData(51, Grade.C)]
    [InlineData(70, Grade.C)]
    [InlineData(71, Grade.D)]
    [InlineData(90, Grade.D)]
    [InlineData(91, Grade.F)]
    [InlineData(100, Grade.F)]
    public void Cap_appliesAtCorrectThresholds(int pct, Grade? expected) =>
        Assert.Equal(expected, PrecipitationFactor.Cap(pct).Cap);
}
