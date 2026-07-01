using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class AirQualityFactorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(101, true)]
    [InlineData(250, true)]
    public void IsActive_gatesAt101(int aqi, bool expected) =>
        Assert.Equal(expected, AirQualityFactor.IsActive(aqi));

    [Fact]
    public void Score_dragsBelow85_theInstantItActivates() =>
        Assert.Equal(80, AirQualityFactor.Score(101));

    [Fact]
    public void Score_isMidwayByAqi175() =>
        Assert.Equal(50, AirQualityFactor.Score(175));

    [Fact]
    public void Score_hitsZero_atHazardous() =>
        Assert.Equal(0, AirQualityFactor.Score(300));

    [Theory]
    [InlineData(101, null)]
    [InlineData(150, null)]
    [InlineData(151, Grade.C)]
    [InlineData(200, Grade.C)]
    [InlineData(201, Grade.F)]
    [InlineData(400, Grade.F)]
    public void Cap_noCapUntil151_thenCThenF(int aqi, Grade? expected) =>
        Assert.Equal(expected, AirQualityFactor.Cap(aqi).Cap);

    [Fact]
    public void Detail_mentionsAqiValue() =>
        Assert.Contains("101", AirQualityFactor.Detail(101));

    [Fact]
    public void Cap_reasonMentionsAqiValue() =>
        Assert.Contains("250", AirQualityFactor.Cap(250).Reason);
}
