using RouteWeather.API.Options;
using Xunit;

namespace RouteWeather.API.Tests;

public class ForecastSourcesOptionsTests
{
    [Fact]
    public void PrecipVoteWeightFor_usesConfiguredValue_whenSet()
    {
        var opts = new ForecastSourcesOptions
        {
            Sources = { new SourceOptions { Name = "NWS", Weight = 1.0, PrecipVoteWeight = 1.75 } },
        };
        Assert.Equal(1.75, opts.PrecipVoteWeightFor("NWS"));
    }

    [Fact]
    public void PrecipVoteWeightFor_fallsBackToWeight_whenUnset()
    {
        var opts = new ForecastSourcesOptions
        {
            Sources = { new SourceOptions { Name = "OpenMeteo-HRRR", Weight = 1.2 } },
        };
        Assert.Equal(1.2, opts.PrecipVoteWeightFor("OpenMeteo-HRRR"));
    }

    [Fact]
    public void PrecipVoteWeightFor_defaultsToOne_whenSourceMissing()
    {
        var opts = new ForecastSourcesOptions();
        Assert.Equal(1.0, opts.PrecipVoteWeightFor("Nonexistent"));
    }
}
