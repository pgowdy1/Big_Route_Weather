using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data;

public static class RouteSeeder
{
    public static async Task SeedAsync(RouteWeatherContext db, CancellationToken ct = default)
    {
        if (await db.Routes.AnyAsync(ct)) return;

        db.Routes.AddRange(
            new RouteEntity
            {
                Slug = "longs-peak-keyhole",
                Mountain = "Longs Peak",
                RouteName = "Keyhole",
                SummitElevationFt = 14259,
                SummitLat = 40.2549,
                SummitLon = -105.6160,
                ClassDifficulty = "3",
                SnotelStationTriplet = "1042:CO:SNTL",
            },
            new RouteEntity
            {
                Slug = "capitol-peak-knife-edge",
                Mountain = "Capitol Peak",
                RouteName = "NE Ridge / Knife Edge",
                SummitElevationFt = 14137,
                SummitLat = 39.1503,
                SummitLon = -107.0830,
                ClassDifficulty = "4",
                SnotelStationTriplet = "737:CO:SNTL",
            },
            new RouteEntity
            {
                Slug = "pyramid-peak-ne-ridge",
                Mountain = "Pyramid Peak",
                RouteName = "NE Ridge",
                SummitElevationFt = 14025,
                SummitLat = 39.0716,
                SummitLon = -106.9501,
                ClassDifficulty = "4",
                SnotelStationTriplet = "737:CO:SNTL",
            }
        );

        await db.SaveChangesAsync(ct);
    }
}
