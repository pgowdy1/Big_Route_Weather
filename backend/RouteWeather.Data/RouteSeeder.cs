using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data;

public static class RouteSeeder
{
    public static async Task SeedAsync(RouteWeatherContext db, CancellationToken ct = default)
    {
        var ranges = await EnsureRangesAsync(db, ct);

        if (!await db.Routes.AnyAsync(ct))
        {
            db.Routes.AddRange(BuildRoutes(ranges));
            await db.SaveChangesAsync(ct);
            return;
        }

        // Routes already exist (the migration backfilled them to colorado-14ers). Add any peaks
        // from the catalog that aren't yet in the DB (the new 29). Existing CO 14ers stay tagged
        // to colorado-14ers by the migration.
        var existing = await db.Routes.Select(r => r.Slug).ToListAsync(ct);
        var existingSet = existing.ToHashSet();
        var toAdd = BuildRoutes(ranges).Where(r => !existingSet.Contains(r.Slug)).ToList();
        if (toAdd.Count == 0) return;

        db.Routes.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Dictionary<string, int>> EnsureRangesAsync(RouteWeatherContext db, CancellationToken ct)
    {
        var existing = await db.Ranges.ToListAsync(ct);
        var bySlug = existing.ToDictionary(r => r.Slug, r => r.Id);

        foreach (var range in RangeCatalog())
        {
            if (bySlug.ContainsKey(range.Slug)) continue;
            db.Ranges.Add(range);
            await db.SaveChangesAsync(ct);
            bySlug[range.Slug] = range.Id;
        }

        return bySlug;
    }

    private static IEnumerable<RangeEntity> RangeCatalog() => new[]
    {
        new RangeEntity
        {
            Slug = "cascades", Name = "Cascade Range", Color = "#5fa8d8",
            Description = "Volcanic peaks of the Pacific Northwest.",
            DisplayOrder = 1,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-121.5,48.9],[-121.0,48.5],[-120.9,47.3],[-121.5,46.4],[-121.8,45.4],[-122.0,44.8],[-122.2,43.5],[-121.9,42.5],[-122.3,41.3],[-122.6,40.5],[-123.0,41.0],[-122.5,42.5],[-122.4,43.7],[-122.3,45.0],[-122.0,46.2],[-121.9,47.5],[-121.7,48.8],[-121.5,48.9]]]}",
        },
        new RangeEntity
        {
            Slug = "sierra-nevada", Name = "Sierra Nevada", Color = "#d8a85f",
            Description = "The high granite range of eastern California.",
            DisplayOrder = 2,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-120.5,39.5],[-120.0,38.8],[-119.2,38.4],[-118.7,37.4],[-118.5,36.7],[-118.2,36.4],[-117.9,35.9],[-118.5,35.7],[-119.0,36.5],[-119.4,37.0],[-119.8,37.5],[-120.2,38.2],[-120.7,38.8],[-120.9,39.4],[-120.5,39.5]]]}",
        },
        new RangeEntity
        {
            Slug = "wind-river", Name = "Wind River Range", Color = "#7fc878",
            Description = "Remote granite spires and big glaciers in west-central Wyoming.",
            DisplayOrder = 3,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-110.0,43.5],[-109.5,43.2],[-109.0,42.8],[-108.7,42.5],[-108.9,42.3],[-109.4,42.7],[-109.9,43.1],[-110.2,43.4],[-110.0,43.5]]]}",
        },
        new RangeEntity
        {
            Slug = "sawtooth", Name = "Sawtooth Range", Color = "#c898d8",
            Description = "Compact granite range in central Idaho.",
            DisplayOrder = 4,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-115.2,44.4],[-114.7,44.4],[-114.6,43.9],[-114.9,43.7],[-115.2,43.9],[-115.2,44.4]]]}",
        },
        new RangeEntity
        {
            Slug = "wasatch", Name = "Wasatch Range", Color = "#f0a878",
            Description = "Northern Utah's front range.",
            DisplayOrder = 5,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-111.9,41.5],[-111.4,41.5],[-111.4,40.0],[-111.6,39.4],[-111.9,39.4],[-112.0,40.0],[-111.9,41.5]]]}",
        },
        new RangeEntity
        {
            Slug = "colorado-14ers", Name = "Colorado 14ers", Color = "#e8b04f",
            Description = "The 58 peaks above 14,000 ft in Colorado.",
            DisplayOrder = 6,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-108.0,40.5],[-105.4,40.5],[-105.0,39.8],[-105.0,38.4],[-105.5,37.0],[-107.0,37.0],[-108.2,37.5],[-108.5,38.5],[-108.3,39.5],[-108.0,40.5]]]}",
        },
    };

    private static IEnumerable<RouteEntity> BuildRoutes(IReadOnlyDictionary<string, int> rangeIds)
    {
        int co = rangeIds["colorado-14ers"];
        int ca = rangeIds["cascades"];
        int si = rangeIds["sierra-nevada"];
        int wr = rangeIds["wind-river"];
        int sa = rangeIds["sawtooth"];
        int wa = rangeIds["wasatch"];

        return Cascades(ca)
            .Concat(Sierras(si))
            .Concat(WindRiver(wr))
            .Concat(Sawtooth(sa))
            .Concat(Wasatch(wa))
            .Concat(Colorado14ers(co));
    }

    private static IEnumerable<RouteEntity> Cascades(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-rainier",        Mountain = "Mount Rainier",     RouteName = "Disappointment Cleaver", SummitElevationFt = 14411, SummitLat = 46.8523, SummitLon = -121.7603, ClassDifficulty = "4", SnotelStationTriplet = "679:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-hood",           Mountain = "Mount Hood",        RouteName = "South Side (Hogsback)",  SummitElevationFt = 11239, SummitLat = 45.3736, SummitLon = -121.6960, ClassDifficulty = "3", SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-adams",          Mountain = "Mount Adams",       RouteName = "South Spur",             SummitElevationFt = 12281, SummitLat = 46.2024, SummitLon = -121.4909, ClassDifficulty = "2", SnotelStationTriplet = "657:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-baker",          Mountain = "Mount Baker",       RouteName = "Coleman-Deming",         SummitElevationFt = 10781, SummitLat = 48.7768, SummitLon = -121.8145, ClassDifficulty = "3", SnotelStationTriplet = "999:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-shasta",         Mountain = "Mount Shasta",      RouteName = "Avalanche Gulch",        SummitElevationFt = 14179, SummitLat = 41.4099, SummitLon = -122.1949, ClassDifficulty = "3", SnotelStationTriplet = "1067:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "glacier-peak",         Mountain = "Glacier Peak",      RouteName = "Sitkum Glacier",         SummitElevationFt = 10541, SummitLat = 48.1112, SummitLon = -121.1130, ClassDifficulty = "3", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-st-helens",      Mountain = "Mount St. Helens",  RouteName = "Monitor Ridge",          SummitElevationFt = 8366,  SummitLat = 46.1912, SummitLon = -122.1944, ClassDifficulty = "2", SnotelStationTriplet = "999:WA:SNTL" },
    };

    private static IEnumerable<RouteEntity> Sierras(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-whitney",        Mountain = "Mount Whitney",     RouteName = "Mountaineer's Route",    SummitElevationFt = 14505, SummitLat = 36.5786, SummitLon = -118.2920, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-williamson",     Mountain = "Mount Williamson",  RouteName = "West Face",              SummitElevationFt = 14379, SummitLat = 36.6555, SummitLon = -118.3110, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-palisade",       Mountain = "North Palisade",    RouteName = "LeConte Route",          SummitElevationFt = 14248, SummitLat = 37.0944, SummitLon = -118.5145, ClassDifficulty = "4", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sill",           Mountain = "Mount Sill",        RouteName = "Swiss Arête",            SummitElevationFt = 14159, SummitLat = 37.1006, SummitLon = -118.5031, ClassDifficulty = "4", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-russell",        Mountain = "Mount Russell",     RouteName = "East Ridge",             SummitElevationFt = 14094, SummitLat = 36.5867, SummitLon = -118.2750, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-langley",        Mountain = "Mount Langley",     RouteName = "Old Army Pass",          SummitElevationFt = 14032, SummitLat = 36.5239, SummitLon = -118.2392, ClassDifficulty = "2", SnotelStationTriplet = "428:CA:SNTL" },
    };

    private static IEnumerable<RouteEntity> WindRiver(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "gannett-peak",         Mountain = "Gannett Peak",      RouteName = "Gooseneck Glacier",      SummitElevationFt = 13809, SummitLat = 43.1842, SummitLon = -109.6543, ClassDifficulty = "3", SnotelStationTriplet = "1010:WY:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "fremont-peak",         Mountain = "Fremont Peak",      RouteName = "Southwest Slopes",       SummitElevationFt = 13745, SummitLat = 43.1239, SummitLon = -109.6181, ClassDifficulty = "2", SnotelStationTriplet = "367:WY:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-helen",          Mountain = "Mount Helen",       RouteName = "East Ridge",             SummitElevationFt = 13620, SummitLat = 43.1572, SummitLon = -109.6431, ClassDifficulty = "3", SnotelStationTriplet = "1010:WY:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sacagawea",      Mountain = "Mount Sacagawea",   RouteName = "Northeast Ridge",        SummitElevationFt = 13569, SummitLat = 43.1497, SummitLon = -109.6203, ClassDifficulty = "2", SnotelStationTriplet = "1010:WY:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "wind-river-peak",      Mountain = "Wind River Peak",   RouteName = "Northwest Ridge",        SummitElevationFt = 13192, SummitLat = 42.7104, SummitLon = -109.1262, ClassDifficulty = "2", SnotelStationTriplet = "367:WY:SNTL" },
    };

    private static IEnumerable<RouteEntity> Sawtooth(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "thompson-peak",        Mountain = "Thompson Peak",     RouteName = "Southwest Slopes",       SummitElevationFt = 10751, SummitLat = 44.0925, SummitLon = -115.0050, ClassDifficulty = "3", SnotelStationTriplet = "837:ID:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-heyburn",        Mountain = "Mount Heyburn",     RouteName = "East Face Standard",     SummitElevationFt = 10229, SummitLat = 44.0697, SummitLon = -114.9483, ClassDifficulty = "4", SnotelStationTriplet = "837:ID:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-cramer",         Mountain = "Mount Cramer",      RouteName = "Southeast Ridge",        SummitElevationFt = 10716, SummitLat = 44.0411, SummitLon = -114.9347, ClassDifficulty = "3", SnotelStationTriplet = "837:ID:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "williams-peak",        Mountain = "Williams Peak",     RouteName = "South Slopes",           SummitElevationFt = 10635, SummitLat = 44.1283, SummitLon = -114.9961, ClassDifficulty = "3", SnotelStationTriplet = "837:ID:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "snowyside-peak",       Mountain = "Snowyside Peak",    RouteName = "Northeast Ridge",        SummitElevationFt = 10651, SummitLat = 43.9911, SummitLon = -114.8917, ClassDifficulty = "2", SnotelStationTriplet = "837:ID:SNTL" },
    };

    private static IEnumerable<RouteEntity> Wasatch(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-timpanogos",     Mountain = "Mount Timpanogos",  RouteName = "Aspen Grove",            SummitElevationFt = 11752, SummitLat = 40.3908, SummitLon = -111.6453, ClassDifficulty = "2", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-nebo",           Mountain = "Mount Nebo",        RouteName = "North Peak Standard",    SummitElevationFt = 11933, SummitLat = 39.8222, SummitLon = -111.7611, ClassDifficulty = "2", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "lone-peak",            Mountain = "Lone Peak",         RouteName = "NW Couloir",             SummitElevationFt = 11253, SummitLat = 40.5306, SummitLon = -111.7569, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "pfeifferhorn",         Mountain = "Pfeifferhorn",      RouteName = "Northeast Ridge",        SummitElevationFt = 11326, SummitLat = 40.5544, SummitLon = -111.7333, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-olympus",        Mountain = "Mount Olympus",     RouteName = "Standard",               SummitElevationFt = 9026,  SummitLat = 40.6364, SummitLon = -111.7325, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "box-elder-peak",       Mountain = "Box Elder Peak",    RouteName = "South Ridge",            SummitElevationFt = 11101, SummitLat = 40.4878, SummitLon = -111.7239, ClassDifficulty = "2", SnotelStationTriplet = "766:UT:SNTL" },
    };

    // The 58 Colorado 14ers — preserved exactly from the prior seeder, plus RangeId.
    private static IEnumerable<RouteEntity> Colorado14ers(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-elbert",           Mountain = "Mount Elbert",           RouteName = "Northeast Ridge",                SummitElevationFt = 14438, SummitLat = 39.1178, SummitLon = -106.4453, ClassDifficulty = "1", SnotelStationTriplet = "369:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-massive",          Mountain = "Mount Massive",          RouteName = "East Slopes",                    SummitElevationFt = 14428, SummitLat = 39.1873, SummitLon = -106.4756, ClassDifficulty = "2", SnotelStationTriplet = "1101:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-harvard",          Mountain = "Mount Harvard",          RouteName = "South Slopes",                   SummitElevationFt = 14421, SummitLat = 38.9244, SummitLon = -106.3206, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "blanca-peak",            Mountain = "Blanca Peak",            RouteName = "Northwest Ridge",                SummitElevationFt = 14345, SummitLat = 37.5775, SummitLon = -105.4856, ClassDifficulty = "2", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "la-plata-peak",          Mountain = "La Plata Peak",          RouteName = "Northwest Ridge",                SummitElevationFt = 14336, SummitLat = 39.0294, SummitLon = -106.4729, ClassDifficulty = "2", SnotelStationTriplet = "369:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "uncompahgre-peak",       Mountain = "Uncompahgre Peak",       RouteName = "South Ridge",                    SummitElevationFt = 14309, SummitLat = 38.0717, SummitLon = -107.4622, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "crestone-peak",          Mountain = "Crestone Peak",          RouteName = "South Face (Red Gully)",         SummitElevationFt = 14294, SummitLat = 37.9669, SummitLon = -105.5853, ClassDifficulty = "3", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-lincoln",          Mountain = "Mount Lincoln",          RouteName = "West Ridge (DeCaLiBron)",        SummitElevationFt = 14286, SummitLat = 39.3514, SummitLon = -106.1117, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "grays-peak",             Mountain = "Grays Peak",             RouteName = "North Slopes",                   SummitElevationFt = 14270, SummitLat = 39.6339, SummitLon = -105.8175, ClassDifficulty = "1", SnotelStationTriplet = "1187:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-antero",           Mountain = "Mount Antero",           RouteName = "West Slopes (Baldwin Gulch)",    SummitElevationFt = 14269, SummitLat = 38.6741, SummitLon = -106.2461, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "torreys-peak",           Mountain = "Torreys Peak",           RouteName = "South Slopes (via Grays)",       SummitElevationFt = 14267, SummitLat = 39.6428, SummitLon = -105.8211, ClassDifficulty = "2", SnotelStationTriplet = "1187:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "castle-peak",            Mountain = "Castle Peak",            RouteName = "Northeast Ridge",                SummitElevationFt = 14265, SummitLat = 39.0094, SummitLon = -106.8614, ClassDifficulty = "2", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "quandary-peak",          Mountain = "Quandary Peak",          RouteName = "East Ridge",                     SummitElevationFt = 14265, SummitLat = 39.3973, SummitLon = -106.1064, ClassDifficulty = "1", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-evans",            Mountain = "Mount Evans",            RouteName = "Northeast Face",                 SummitElevationFt = 14264, SummitLat = 39.5883, SummitLon = -105.6438, ClassDifficulty = "2", SnotelStationTriplet = "1187:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "longs-peak",             Mountain = "Longs Peak",             RouteName = "Keyhole",                        SummitElevationFt = 14255, SummitLat = 40.2549, SummitLon = -105.6160, ClassDifficulty = "3", SnotelStationTriplet = "1042:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-wilson",           Mountain = "Mount Wilson",           RouteName = "North Slopes",                   SummitElevationFt = 14246, SummitLat = 37.8389, SummitLon = -107.9911, ClassDifficulty = "4", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-cameron",          Mountain = "Mount Cameron",          RouteName = "DeCaLiBron via saddle",          SummitElevationFt = 14238, SummitLat = 39.3464, SummitLon = -106.1186, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-shavano",          Mountain = "Mount Shavano",          RouteName = "East Slopes (Angel of Shavano)", SummitElevationFt = 14229, SummitLat = 38.6192, SummitLon = -106.2253, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-belford",          Mountain = "Mount Belford",          RouteName = "Northwest Ridge",                SummitElevationFt = 14197, SummitLat = 38.9606, SummitLon = -106.3608, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "crestone-needle",        Mountain = "Crestone Needle",        RouteName = "South Face",                     SummitElevationFt = 14197, SummitLat = 37.9647, SummitLon = -105.5764, ClassDifficulty = "3", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-princeton",        Mountain = "Mount Princeton",        RouteName = "East Slopes",                    SummitElevationFt = 14197, SummitLat = 38.7492, SummitLon = -106.2425, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-yale",             Mountain = "Mount Yale",             RouteName = "Southwest Slopes",               SummitElevationFt = 14196, SummitLat = 38.8442, SummitLon = -106.3136, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-bross",            Mountain = "Mount Bross",            RouteName = "West Slopes (DeCaLiBron)",       SummitElevationFt = 14172, SummitLat = 39.3358, SummitLon = -106.1078, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "kit-carson-peak",        Mountain = "Kit Carson Peak",        RouteName = "North Ridge (via Challenger)",   SummitElevationFt = 14165, SummitLat = 37.9794, SummitLon = -105.6028, ClassDifficulty = "3", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "el-diente-peak",         Mountain = "El Diente Peak",         RouteName = "North Slopes",                   SummitElevationFt = 14159, SummitLat = 37.8394, SummitLon = -108.0050, ClassDifficulty = "3", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "maroon-peak",            Mountain = "Maroon Peak",            RouteName = "South Ridge",                    SummitElevationFt = 14156, SummitLat = 39.0708, SummitLon = -106.9889, ClassDifficulty = "4", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "tabeguache-peak",        Mountain = "Tabeguache Peak",        RouteName = "West Ridge (via Shavano)",       SummitElevationFt = 14155, SummitLat = 38.6258, SummitLon = -106.2386, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-oxford",           Mountain = "Mount Oxford",           RouteName = "West Ridge (via Belford)",       SummitElevationFt = 14153, SummitLat = 38.9647, SummitLon = -106.3389, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sneffels",         Mountain = "Mount Sneffels",         RouteName = "Southwest Ridge (Lavender Col)", SummitElevationFt = 14150, SummitLat = 38.0036, SummitLon = -107.7925, ClassDifficulty = "3", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-democrat",         Mountain = "Mount Democrat",         RouteName = "East Slopes (DeCaLiBron)",       SummitElevationFt = 14148, SummitLat = 39.3394, SummitLon = -106.1397, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "capitol-peak",           Mountain = "Capitol Peak",           RouteName = "Northeast Ridge (Knife Edge)",   SummitElevationFt = 14130, SummitLat = 39.1503, SummitLon = -107.0830, ClassDifficulty = "4", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "pikes-peak",             Mountain = "Pikes Peak",             RouteName = "Barr Trail",                     SummitElevationFt = 14110, SummitLat = 38.8409, SummitLon = -105.0442, ClassDifficulty = "1", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "snowmass-mountain",      Mountain = "Snowmass Mountain",      RouteName = "East Slopes",                    SummitElevationFt = 14092, SummitLat = 39.1186, SummitLon = -107.0664, ClassDifficulty = "3", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-eolus",            Mountain = "Mount Eolus",            RouteName = "Northeast Ridge",                SummitElevationFt = 14083, SummitLat = 37.6219, SummitLon = -107.6225, ClassDifficulty = "3", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "windom-peak",            Mountain = "Windom Peak",            RouteName = "West Ridge",                     SummitElevationFt = 14082, SummitLat = 37.6214, SummitLon = -107.5917, ClassDifficulty = "2", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "challenger-point",       Mountain = "Challenger Point",       RouteName = "North Slopes",                   SummitElevationFt = 14081, SummitLat = 37.9803, SummitLon = -105.6064, ClassDifficulty = "2", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-columbia",         Mountain = "Mount Columbia",         RouteName = "West Slopes",                    SummitElevationFt = 14077, SummitLat = 38.9039, SummitLon = -106.2972, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "missouri-mountain",      Mountain = "Missouri Mountain",      RouteName = "Northwest Ridge",                SummitElevationFt = 14074, SummitLat = 38.9478, SummitLon = -106.3789, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "humboldt-peak",          Mountain = "Humboldt Peak",          RouteName = "West Ridge",                     SummitElevationFt = 14070, SummitLat = 37.9764, SummitLon = -105.5550, ClassDifficulty = "2", SnotelStationTriplet = "1128:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-bierstadt",        Mountain = "Mount Bierstadt",        RouteName = "West Slopes",                    SummitElevationFt = 14065, SummitLat = 39.5828, SummitLon = -105.6685, ClassDifficulty = "2", SnotelStationTriplet = "1187:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "conundrum-peak",         Mountain = "Conundrum Peak",         RouteName = "Northeast Ridge (via Castle)",   SummitElevationFt = 14060, SummitLat = 39.0064, SummitLon = -106.8675, ClassDifficulty = "2", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sunlight-peak",          Mountain = "Sunlight Peak",          RouteName = "South Face",                     SummitElevationFt = 14059, SummitLat = 37.6275, SummitLon = -107.5950, ClassDifficulty = "4", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "handies-peak",           Mountain = "Handies Peak",           RouteName = "American Basin",                 SummitElevationFt = 14048, SummitLat = 37.9131, SummitLon = -107.5042, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "culebra-peak",           Mountain = "Culebra Peak",           RouteName = "Northwest Ridge",                SummitElevationFt = 14047, SummitLat = 37.1225, SummitLon = -105.1856, ClassDifficulty = "2", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "ellingwood-point",       Mountain = "Ellingwood Point",       RouteName = "South Face (via Blanca)",        SummitElevationFt = 14042, SummitLat = 37.5822, SummitLon = -105.4925, ClassDifficulty = "2", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-lindsey",          Mountain = "Mount Lindsey",          RouteName = "Northwest Ridge",                SummitElevationFt = 14042, SummitLat = 37.5836, SummitLon = -105.4456, ClassDifficulty = "2", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-eolus",            Mountain = "North Eolus",            RouteName = "Eolus Ridge",                    SummitElevationFt = 14039, SummitLat = 37.6228, SummitLon = -107.6233, ClassDifficulty = "3", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "little-bear-peak",       Mountain = "Little Bear Peak",       RouteName = "West Ridge (Hourglass)",         SummitElevationFt = 14037, SummitLat = 37.5667, SummitLon = -105.4972, ClassDifficulty = "4", SnotelStationTriplet = "1141:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sherman",          Mountain = "Mount Sherman",          RouteName = "Southwest Ridge",                SummitElevationFt = 14036, SummitLat = 39.2253, SummitLon = -106.1697, ClassDifficulty = "2", SnotelStationTriplet = "1120:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "redcloud-peak",          Mountain = "Redcloud Peak",          RouteName = "Northeast Ridge",                SummitElevationFt = 14034, SummitLat = 37.9408, SummitLon = -107.4214, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "pyramid-peak",           Mountain = "Pyramid Peak",           RouteName = "Northeast Ridge",                SummitElevationFt = 14018, SummitLat = 39.0716, SummitLon = -106.9501, ClassDifficulty = "4", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "wilson-peak",            Mountain = "Wilson Peak",            RouteName = "West Ridge",                     SummitElevationFt = 14017, SummitLat = 37.8597, SummitLon = -107.9847, ClassDifficulty = "3", SnotelStationTriplet = "1060:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "wetterhorn-peak",        Mountain = "Wetterhorn Peak",        RouteName = "Southeast Ridge",                SummitElevationFt = 14015, SummitLat = 38.0606, SummitLon = -107.5106, ClassDifficulty = "3", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-maroon-peak",      Mountain = "North Maroon Peak",      RouteName = "Northeast Ridge",                SummitElevationFt = 14014, SummitLat = 39.0758, SummitLon = -106.9883, ClassDifficulty = "4", SnotelStationTriplet = "542:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "san-luis-peak",          Mountain = "San Luis Peak",          RouteName = "Northeast Ridge",                SummitElevationFt = 14014, SummitLat = 37.9869, SummitLon = -106.9311, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-of-the-holy-cross", Mountain = "Mount of the Holy Cross", RouteName = "North Ridge",                  SummitElevationFt = 14005, SummitLat = 39.4669, SummitLon = -106.4814, ClassDifficulty = "2", SnotelStationTriplet = "1101:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "huron-peak",             Mountain = "Huron Peak",             RouteName = "Northwest Slopes",               SummitElevationFt = 14003, SummitLat = 38.9453, SummitLon = -106.4378, ClassDifficulty = "2", SnotelStationTriplet = "1057:CO:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sunshine-peak",          Mountain = "Sunshine Peak",          RouteName = "North Slopes (via Redcloud)",    SummitElevationFt = 14001, SummitLat = 37.9258, SummitLon = -107.4256, ClassDifficulty = "2", SnotelStationTriplet = "1186:CO:SNTL" },
    };
}
