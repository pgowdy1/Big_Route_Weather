using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;

namespace RouteWeather.API.Services;

public abstract class OpenMeteoModelSource : IForecastSource
{
    private readonly OpenMeteoClient _client;

    protected OpenMeteoModelSource(OpenMeteoClient client) => _client = client;

    public abstract string Name { get; }
    public abstract IReadOnlySet<string> ActiveFactors { get; }

    public async Task<WeatherSnapshot?> FetchAsync(double lat, double lon, CancellationToken ct)
    {
        var all = await _client.FetchAllModelsAsync(lat, lon, ct);
        return all.TryGetValue(Name, out var snap) ? snap : null;
    }
}

public sealed class OpenMeteoGfsSource(OpenMeteoClient c) : OpenMeteoModelSource(c)
{
    public override string Name => "OpenMeteo-GFS";
    public override IReadOnlySet<string> ActiveFactors => ForecastFactors.All;
}

public sealed class OpenMeteoEcmwfSource(OpenMeteoClient c) : OpenMeteoModelSource(c)
{
    public override string Name => "OpenMeteo-ECMWF";
    public override IReadOnlySet<string> ActiveFactors => ForecastFactors.WindAndTemperatureOnly;
}

public sealed class OpenMeteoIconSource(OpenMeteoClient c) : OpenMeteoModelSource(c)
{
    public override string Name => "OpenMeteo-ICON";
    public override IReadOnlySet<string> ActiveFactors => ForecastFactors.WindAndTemperatureOnly;
}

public sealed class OpenMeteoHrrrSource(OpenMeteoClient c) : OpenMeteoModelSource(c)
{
    public override string Name => "OpenMeteo-HRRR";
    public override IReadOnlySet<string> ActiveFactors => ForecastFactors.WindAndTemperatureOnly;
}
