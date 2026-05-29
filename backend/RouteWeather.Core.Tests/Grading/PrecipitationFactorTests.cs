using RouteWeather.Core.Grading;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class PrecipitationFactorTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(25, 75)]
    [InlineData(100, 0)]
    public void Score_isInverseOfPrecipPct(int precipPct, int expected) =>
        Assert.Equal(expected, PrecipitationFactor.Score(precipPct));
}
