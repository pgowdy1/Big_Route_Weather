using RouteWeather.API.Options;

namespace RouteWeather.API.Tests;

public class WarmerOptionsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var opts = new WarmerOptions();
        Assert.True(opts.Enabled);
        Assert.Equal(10, opts.IntervalMinutes);
        Assert.Equal(24, opts.ServeStaleMaxHours);
    }
}
