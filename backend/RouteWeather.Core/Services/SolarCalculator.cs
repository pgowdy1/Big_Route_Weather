using RouteWeather.Core.Models;

namespace RouteWeather.Core.Services;

/// <summary>NOAA sunrise/sunset approximation — accurate to a couple of minutes at mid-latitudes.</summary>
public static class SolarCalculator
{
    public static DaylightInfo? NextDaylight(double lat, double lon, DateTimeOffset nowUtc)
    {
        var today = ComputeUtc(lat, lon, DateOnly.FromDateTime(nowUtc.UtcDateTime));
        if (today is not null && today.SunsetUtc > nowUtc) return today;
        return ComputeUtc(lat, lon, DateOnly.FromDateTime(nowUtc.UtcDateTime).AddDays(1));
    }

    public static DaylightInfo? ComputeUtc(double lat, double lon, DateOnly dateUtc)
    {
        var gamma = 2.0 * Math.PI / 365.0 * (dateUtc.DayOfYear - 1);

        var eqTimeMin = 229.18 * (0.000075
            + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));

        var declRad = 0.006918
            - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
            - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        var latRad = lat * Math.PI / 180.0;
        var zenithRad = 90.833 * Math.PI / 180.0; // official sunrise: refraction + solar disc

        var cosHa = Math.Cos(zenithRad) / (Math.Cos(latRad) * Math.Cos(declRad))
                    - Math.Tan(latRad) * Math.Tan(declRad);
        if (cosHa < -1 || cosHa > 1) return null; // polar day or night

        var haDeg = Math.Acos(cosHa) * 180.0 / Math.PI;
        var sunriseMin = 720.0 - 4.0 * (lon + haDeg) - eqTimeMin;
        var sunsetMin = 720.0 - 4.0 * (lon - haDeg) - eqTimeMin;

        var midnightUtc = new DateTimeOffset(dateUtc.Year, dateUtc.Month, dateUtc.Day, 0, 0, 0, TimeSpan.Zero);
        var sunrise = midnightUtc.AddMinutes(sunriseMin);
        var sunset = midnightUtc.AddMinutes(sunsetMin);
        return new DaylightInfo(sunrise, sunset, (sunset - sunrise).TotalMinutes / 60.0);
    }
}
