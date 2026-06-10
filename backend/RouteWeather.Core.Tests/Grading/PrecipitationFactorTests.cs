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

    [Fact]
    public void Score_withNullAmount_equalsProbabilityScore() =>
        Assert.Equal(PrecipitationFactor.Score(40), PrecipitationFactor.Score(40, null, 24));

    [Fact]
    public void Score_withTraceAmount_belowEngageFloor_equalsProbabilityScore() =>
        Assert.Equal(PrecipitationFactor.Score(40), PrecipitationFactor.Score(40, 0.04, 24));

    [Fact]
    public void Score_takesWorseOfProbabilityAndAmount()
    {
        // 20% prob -> probScore 75. 0.5" in 24h (bad=1.0") -> amountScore 50. min() = 50.
        Assert.Equal(50, PrecipitationFactor.Score(20, 0.5, 24));
    }

    [Fact]
    public void Score_amountThresholdScalesByWindowHours()
    {
        // 0.5" over 12h (bad=0.5") -> amountScore 0; same 0.5" over 48h (bad=2.0") -> 75.
        Assert.Equal(0, PrecipitationFactor.Score(0, 0.5, 12));
        Assert.Equal(75, PrecipitationFactor.Score(0, 0.5, 48));
    }

    [Fact]
    public void Detail_mentionsAmount_onlyWhenEngaged()
    {
        Assert.Contains("0.5", PrecipitationFactor.Detail(40, 0.5));
        Assert.DoesNotContain("expected", PrecipitationFactor.Detail(40, null));
        Assert.Equal(PrecipitationFactor.Detail(40, null), PrecipitationFactor.Detail(40, 0.01));
    }
}
