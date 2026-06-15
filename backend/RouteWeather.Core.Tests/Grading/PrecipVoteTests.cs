using RouteWeather.Core.Grading;
using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;
using Xunit;

namespace RouteWeather.Core.Tests.Grading;

public class PrecipVoteTests
{
    private static SourceSnapshot Src(bool reportsPop) =>
        new("S",
            new WeatherSnapshot(0, 0, 0, Array.Empty<HourlyForecast>()),
            DateTimeOffset.UtcNow,
            reportsPop ? ForecastFactors.All : ForecastFactors.WindAndTemperatureOnly);

    private static HourlyForecast Hr(int pop, double? amount) =>
        new(DateTimeOffset.UtcNow, TempF: 0, WindMph: 0, PrecipitationProbabilityPct: pop,
            ShortForecast: "", PrecipitationIn: amount);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.005, 0.0)]   // at the dry floor
    [InlineData(0.0275, 0.5)]  // midpoint of 0.005..0.05
    [InlineData(0.05, 1.0)]    // at the certain ceiling
    [InlineData(0.1, 1.0)]     // above the ceiling
    public void Ramp_maps_qpf_to_vote(double qpf, double expected)
    {
        Assert.Equal(expected, PrecipVote.Ramp(qpf), 3);
    }

    [Fact]
    public void For_probability_source_votes_with_pop()
    {
        Assert.Equal(0.6, PrecipVote.For(Src(reportsPop: true), Hr(pop: 60, amount: null))!.Value, 3);
    }

    [Fact]
    public void For_amount_source_votes_via_ramp()
    {
        Assert.Equal(1.0, PrecipVote.For(Src(reportsPop: false), Hr(pop: 0, amount: 0.1))!.Value, 3);
    }

    [Fact]
    public void For_amount_source_with_no_amount_abstains()
    {
        Assert.Null(PrecipVote.For(Src(reportsPop: false), Hr(pop: 0, amount: null)));
    }
}
