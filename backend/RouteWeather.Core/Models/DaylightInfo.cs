namespace RouteWeather.Core.Models;

/// <summary>
/// The next daylight window as absolute UTC instants. For western longitudes
/// SunsetUtc routinely falls on the UTC day after the local date — consumers
/// must not derive date labels from SunsetUtc.Date.
/// </summary>
public record DaylightInfo(DateTimeOffset SunriseUtc, DateTimeOffset SunsetUtc, double DaylightHours);
