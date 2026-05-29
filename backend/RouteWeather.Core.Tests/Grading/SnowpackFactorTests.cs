using RouteWeather.Core.Grading;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class SnowpackFactorTests
{
    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(120)]
    public void Score_returns100_inNormalBand(double percent) =>
        Assert.Equal(100, SnowpackFactor.Score(percent));

    [Fact]
    public void Score_drops_whenWellBelowNormal() =>
        Assert.True(SnowpackFactor.Score(30) <= 0 + 1);

    [Fact]
    public void Score_drops_whenWellAboveNormal() =>
        Assert.True(SnowpackFactor.Score(200) <= 0 + 1);
}
