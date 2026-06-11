namespace RouteWeather.API.Options;

public class WarmerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
    public int ServeStaleMaxHours { get; set; } = 24;
    public int MaxConcurrentRoutes { get; set; } = 3;
}
