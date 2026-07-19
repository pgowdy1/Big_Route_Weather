using Microsoft.EntityFrameworkCore;
using RouteWeather.Data.Entities;

namespace RouteWeather.Data;

public static class RouteSeeder
{
    // Single source of truth for which peaks carry a glacier that commonly-climbed
    // routes cross. Applied in BuildRoutes and reconciled onto existing rows.
    private static readonly HashSet<string> GlaciatedSlugs = new()
    {
        "mount-rainier", "mount-hood", "mount-adams", "mount-baker", "mount-shasta",
        "glacier-peak", "mount-shuksan", "mount-stuart", "forbidden-peak", "dragontail-peak",
        "eldorado-peak", "sahale-peak", "bonanza-peak", "goode-mountain", "sloan-peak",
        "silver-star-mountain", "north-sister", "mount-jefferson", "north-palisade", "mount-sill",
        "middle-palisade", "mount-lyell", "mount-ritter", "banner-peak", "mount-darwin",
        "mount-conness", "gannett-peak", "mount-helen", "mount-sacagawea",
    };

    // Typical summit-day push (car-to-car or camp-to-camp), hours, slow end of
    // guidebook ranges. Single source of truth; reconciled onto existing rows.
    private static readonly Dictionary<string, double> TypicalClimbHoursBySlug = new()
    {
        // Cascades
        ["mount-rainier"] = 12, ["mount-hood"] = 9, ["mount-adams"] = 11, ["mount-baker"] = 10,
        ["mount-shasta"] = 11, ["glacier-peak"] = 14, ["mount-st-helens"] = 9, ["mount-shuksan"] = 14,
        ["mount-stuart"] = 14, ["forbidden-peak"] = 14, ["dragontail-peak"] = 14, ["eldorado-peak"] = 12,
        ["sahale-peak"] = 10, ["liberty-bell"] = 7, ["bonanza-peak"] = 14, ["goode-mountain"] = 16,
        ["black-peak"] = 10, ["sloan-peak"] = 12, ["silver-star-mountain"] = 10, ["south-sister"] = 9,
        ["north-sister"] = 12, ["mount-jefferson"] = 14, ["mount-thielsen"] = 8, ["mount-mcloughlin"] = 8,
        ["lassen-peak"] = 6,
        // Sierra
        ["mount-whitney"] = 14, ["mount-williamson"] = 16, ["north-palisade"] = 14, ["mount-sill"] = 14,
        ["mount-russell"] = 12, ["mount-langley"] = 11, ["mount-conness"] = 12, ["cathedral-peak"] = 8,
        ["matterhorn-peak"] = 12, ["mount-dana"] = 7, ["mount-lyell"] = 12, ["mount-ritter"] = 12,
        ["banner-peak"] = 12, ["mount-humphreys"] = 12, ["mount-darwin"] = 14, ["temple-crag"] = 14,
        ["bear-creek-spire"] = 12, ["mount-brewer"] = 14, ["middle-palisade"] = 14, ["mount-tyndall"] = 14,
        // Wind River
        ["gannett-peak"] = 12, ["fremont-peak"] = 10, ["mount-helen"] = 12, ["mount-sacagawea"] = 12,
        ["wind-river-peak"] = 14,
        // Sawtooth
        ["thompson-peak"] = 8, ["mount-heyburn"] = 8, ["mount-cramer"] = 10, ["williams-peak"] = 8,
        ["snowyside-peak"] = 9,
        // Wasatch
        ["mount-timpanogos"] = 8, ["mount-nebo"] = 7, ["lone-peak"] = 10, ["pfeifferhorn"] = 8,
        ["mount-olympus"] = 6, ["box-elder-peak"] = 7, ["broads-fork-twin-peaks"] = 9, ["dromedary-peak"] = 8,
        ["sunrise-peak"] = 8, ["mount-superior"] = 7, ["mount-raymond"] = 7,
        // Colorado 14ers
        ["mount-elbert"] = 8, ["mount-massive"] = 9, ["mount-harvard"] = 10, ["blanca-peak"] = 10,
        ["la-plata-peak"] = 8, ["uncompahgre-peak"] = 7, ["crestone-peak"] = 12, ["mount-lincoln"] = 6,
        ["grays-peak"] = 6, ["mount-antero"] = 9, ["torreys-peak"] = 7, ["castle-peak"] = 9,
        ["quandary-peak"] = 6, ["mount-evans"] = 7, ["longs-peak"] = 13, ["mount-wilson"] = 12,
        ["mount-cameron"] = 6, ["mount-shavano"] = 8, ["mount-belford"] = 8, ["crestone-needle"] = 12,
        ["mount-princeton"] = 8, ["mount-yale"] = 8, ["mount-bross"] = 6, ["kit-carson-peak"] = 12,
        ["el-diente-peak"] = 12, ["maroon-peak"] = 12, ["tabeguache-peak"] = 10, ["mount-oxford"] = 9,
        ["mount-sneffels"] = 8, ["mount-democrat"] = 6, ["capitol-peak"] = 14, ["pikes-peak"] = 12,
        ["snowmass-mountain"] = 13, ["mount-eolus"] = 12, ["windom-peak"] = 11, ["challenger-point"] = 11,
        ["mount-columbia"] = 9, ["missouri-mountain"] = 9, ["humboldt-peak"] = 8, ["mount-bierstadt"] = 6,
        ["conundrum-peak"] = 10, ["sunlight-peak"] = 12, ["handies-peak"] = 6, ["culebra-peak"] = 7,
        ["ellingwood-point"] = 10, ["mount-lindsey"] = 9, ["north-eolus"] = 11, ["little-bear-peak"] = 12,
        ["mount-sherman"] = 5, ["redcloud-peak"] = 8, ["pyramid-peak"] = 12, ["wilson-peak"] = 10,
        ["wetterhorn-peak"] = 9, ["north-maroon-peak"] = 12, ["san-luis-peak"] = 8,
        ["mount-of-the-holy-cross"] = 11, ["huron-peak"] = 7, ["sunshine-peak"] = 9,
    };

    public static async Task SeedAsync(RouteWeatherContext db, CancellationToken ct = default)
    {
        var ranges = await EnsureRangesAsync(db, ct);

        if (!await db.Routes.AnyAsync(ct))
        {
            db.Routes.AddRange(BuildRoutes(ranges));
            await db.SaveChangesAsync(ct);
            return;   // BuildRoutes already set IsGlaciated; no reconcile needed here
        }

        // Routes already exist (the migration backfilled them to colorado-14ers). Add any peaks
        // from the catalog that aren't yet in the DB. Existing CO 14ers stay tagged
        // to colorado-14ers by the migration.
        var existing = await db.Routes.Select(r => r.Slug).ToListAsync(ct);
        var existingSet = existing.ToHashSet();
        var toAdd = BuildRoutes(ranges).Where(r => !existingSet.Contains(r.Slug)).ToList();

        if (toAdd.Count > 0)
        {
            db.Routes.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }

        await ReconcileGlaciatedAsync(db, ct);
        await ReconcileTypicalClimbHoursAsync(db, ct);
    }

    // The add-only path above never updates rows that already exist, so an
    // already-populated DB (dev/prod) would keep the migration default (false).
    // Bring every existing row's IsGlaciated in line with GlaciatedSlugs.
    private static async Task ReconcileGlaciatedAsync(RouteWeatherContext db, CancellationToken ct)
    {
        var rows = await db.Routes.ToListAsync(ct);
        var changed = false;
        foreach (var row in rows)
        {
            var shouldBe = GlaciatedSlugs.Contains(row.Slug);
            if (row.IsGlaciated != shouldBe)
            {
                row.IsGlaciated = shouldBe;
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    // Same add-only gap as IsGlaciated: bring every existing row's TypicalClimbHours
    // in line with the catalog. Slugs missing from the catalog are left untouched.
    private static async Task ReconcileTypicalClimbHoursAsync(RouteWeatherContext db, CancellationToken ct)
    {
        var rows = await db.Routes.ToListAsync(ct);
        var changed = false;
        foreach (var row in rows)
        {
            if (!TypicalClimbHoursBySlug.TryGetValue(row.Slug, out var hours)) continue;
            if (Math.Abs(row.TypicalClimbHours - hours) > 0.001)
            {
                row.TypicalClimbHours = hours;
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
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
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-122.7,49.1],[-120.3,49.1],[-120.3,40.2],[-122.7,40.2],[-122.7,49.1]]]}",
        },
        new RangeEntity
        {
            Slug = "sierra-nevada", Name = "Sierra Nevada", Color = "#d8a85f",
            Description = "The high granite range of eastern California.",
            DisplayOrder = 2,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-119.6,38.3],[-117.9,38.3],[-117.9,36.1],[-119.6,36.1],[-119.6,38.3]]]}",
        },
        new RangeEntity
        {
            Slug = "wind-river", Name = "Wind River Range", Color = "#7fc878",
            Description = "Remote granite spires and big glaciers in west-central Wyoming.",
            DisplayOrder = 3,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-110.0,43.5],[-108.8,43.5],[-108.8,42.4],[-110.0,42.4],[-110.0,43.5]]]}",
        },
        new RangeEntity
        {
            Slug = "sawtooth", Name = "Sawtooth Range", Color = "#c898d8",
            Description = "Compact granite range in central Idaho.",
            DisplayOrder = 4,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-115.2,44.3],[-114.7,44.3],[-114.7,43.8],[-115.2,43.8],[-115.2,44.3]]]}",
        },
        new RangeEntity
        {
            Slug = "wasatch", Name = "Wasatch Range", Color = "#f0a878",
            Description = "Northern Utah's front range.",
            DisplayOrder = 5,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-112.0,40.9],[-111.4,40.9],[-111.4,39.6],[-112.0,39.6],[-112.0,40.9]]]}",
        },
        new RangeEntity
        {
            Slug = "colorado-14ers", Name = "Colorado 14ers", Color = "#e8b04f",
            Description = "The 58 peaks above 14,000 ft in Colorado.",
            DisplayOrder = 6,
            PerimeterGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[[[-108.3,40.5],[-104.8,40.5],[-104.8,36.9],[-108.3,36.9],[-108.3,40.5]]]}",
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

        var routes = Cascades(ca)
            .Concat(Sierras(si))
            .Concat(WindRiver(wr))
            .Concat(Sawtooth(sa))
            .Concat(Wasatch(wa))
            .Concat(Colorado14ers(co))
            .ToList();

        foreach (var r in routes)
            r.IsGlaciated = GlaciatedSlugs.Contains(r.Slug);

        foreach (var r in routes)
            r.TypicalClimbHours = TypicalClimbHoursBySlug[r.Slug];

        return routes;
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
        new RouteEntity { RangeId = rangeId, Slug = "mount-shuksan",        Mountain = "Mount Shuksan",        RouteName = "Fisher Chimneys",        SummitElevationFt = 9131,  SummitLat = 48.8315, SummitLon = -121.6032, ClassDifficulty = "4",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-stuart",         Mountain = "Mount Stuart",         RouteName = "West Ridge",             SummitElevationFt = 9415,  SummitLat = 47.4751, SummitLon = -120.9031, ClassDifficulty = "5.6", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "forbidden-peak",       Mountain = "Forbidden Peak",       RouteName = "West Ridge",             SummitElevationFt = 8815,  SummitLat = 48.5115, SummitLon = -121.0579, ClassDifficulty = "5.6", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "dragontail-peak",      Mountain = "Dragontail Peak",      RouteName = "Backbone Ridge",         SummitElevationFt = 8840,  SummitLat = 47.4787, SummitLon = -120.8334, ClassDifficulty = "5.9", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "eldorado-peak",        Mountain = "Eldorado Peak",        RouteName = "East Ridge",             SummitElevationFt = 8873,  SummitLat = 48.5374, SummitLon = -121.1345, ClassDifficulty = "2",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sahale-peak",          Mountain = "Sahale Peak",          RouteName = "Sahale Arm",             SummitElevationFt = 8680,  SummitLat = 48.4912, SummitLon = -121.0390, ClassDifficulty = "3",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "liberty-bell",         Mountain = "Liberty Bell",         RouteName = "Beckey Route",           SummitElevationFt = 7720,  SummitLat = 48.5154, SummitLon = -120.6579, ClassDifficulty = "5.6", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "bonanza-peak",         Mountain = "Bonanza Peak",         RouteName = "Mary Green Glacier",     SummitElevationFt = 9516,  SummitLat = 48.2382, SummitLon = -120.8664, ClassDifficulty = "4",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "goode-mountain",       Mountain = "Goode Mountain",       RouteName = "Northeast Buttress",     SummitElevationFt = 9220,  SummitLat = 48.4829, SummitLon = -120.9109, ClassDifficulty = "5.4", SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "black-peak",           Mountain = "Black Peak",           RouteName = "South Ridge",            SummitElevationFt = 8975,  SummitLat = 48.5236, SummitLon = -120.8161, ClassDifficulty = "4",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sloan-peak",           Mountain = "Sloan Peak",           RouteName = "Corkscrew Route",        SummitElevationFt = 7835,  SummitLat = 48.0414, SummitLon = -121.3403, ClassDifficulty = "3",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "silver-star-mountain", Mountain = "Silver Star Mountain", RouteName = "Silver Star Glacier",    SummitElevationFt = 8876,  SummitLat = 48.5480, SummitLon = -120.5852, ClassDifficulty = "3",   SnotelStationTriplet = "922:WA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "south-sister",         Mountain = "South Sister",         RouteName = "South Ridge",            SummitElevationFt = 10358, SummitLat = 44.1034, SummitLon = -121.7692, ClassDifficulty = "2",   SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-sister",         Mountain = "North Sister",         RouteName = "South Ridge",            SummitElevationFt = 10085, SummitLat = 44.1665, SummitLon = -121.7723, ClassDifficulty = "4",   SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-jefferson",      Mountain = "Mount Jefferson",      RouteName = "Jefferson Park Glacier", SummitElevationFt = 10497, SummitLat = 44.6743, SummitLon = -121.7996, ClassDifficulty = "5.2", SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-thielsen",       Mountain = "Mount Thielsen",       RouteName = "West Ridge",             SummitElevationFt = 9184,  SummitLat = 43.1528, SummitLon = -122.0665, ClassDifficulty = "4",   SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-mcloughlin",     Mountain = "Mount McLoughlin",     RouteName = "East Ridge",             SummitElevationFt = 9495,  SummitLat = 42.4445, SummitLon = -122.3156, ClassDifficulty = "2",   SnotelStationTriplet = "651:OR:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "lassen-peak",          Mountain = "Lassen Peak",          RouteName = "Southeast Slopes",       SummitElevationFt = 10457, SummitLat = 40.4881, SummitLon = -121.5050, ClassDifficulty = "1",   SnotelStationTriplet = "1067:CA:SNTL" },
    };

    private static IEnumerable<RouteEntity> Sierras(int rangeId) => new[]
    {
        new RouteEntity { RangeId = rangeId, Slug = "mount-whitney",        Mountain = "Mount Whitney",     RouteName = "Mountaineer's Route",    SummitElevationFt = 14505, SummitLat = 36.5786, SummitLon = -118.2920, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-williamson",     Mountain = "Mount Williamson",  RouteName = "West Face",              SummitElevationFt = 14379, SummitLat = 36.6555, SummitLon = -118.3110, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "north-palisade",       Mountain = "North Palisade",    RouteName = "LeConte Route",          SummitElevationFt = 14248, SummitLat = 37.0944, SummitLon = -118.5145, ClassDifficulty = "4", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-sill",           Mountain = "Mount Sill",        RouteName = "Swiss Arête",            SummitElevationFt = 14159, SummitLat = 37.1006, SummitLon = -118.5031, ClassDifficulty = "4", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-russell",        Mountain = "Mount Russell",     RouteName = "East Ridge",             SummitElevationFt = 14094, SummitLat = 36.5867, SummitLon = -118.2750, ClassDifficulty = "3", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-langley",        Mountain = "Mount Langley",     RouteName = "Old Army Pass",          SummitElevationFt = 14032, SummitLat = 36.5239, SummitLon = -118.2392, ClassDifficulty = "2", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-conness",        Mountain = "Mount Conness",        RouteName = "West Ridge",             SummitElevationFt = 12590, SummitLat = 37.9670, SummitLon = -119.3213, ClassDifficulty = "5.6", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "cathedral-peak",       Mountain = "Cathedral Peak",       RouteName = "Southeast Buttress",     SummitElevationFt = 10916, SummitLat = 37.8478, SummitLon = -119.4056, ClassDifficulty = "5.6", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "matterhorn-peak",      Mountain = "Matterhorn Peak",      RouteName = "North Arête",            SummitElevationFt = 12285, SummitLat = 38.0931, SummitLon = -119.3817, ClassDifficulty = "5.7", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-dana",           Mountain = "Mount Dana",           RouteName = "Northwest Slopes",       SummitElevationFt = 13061, SummitLat = 37.9000, SummitLon = -119.2211, ClassDifficulty = "2",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-lyell",          Mountain = "Mount Lyell",          RouteName = "Northwest Slopes",       SummitElevationFt = 13120, SummitLat = 37.7394, SummitLon = -119.2717, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-ritter",         Mountain = "Mount Ritter",         RouteName = "Southeast Glacier",      SummitElevationFt = 13149, SummitLat = 37.6894, SummitLon = -119.1992, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "banner-peak",          Mountain = "Banner Peak",          RouteName = "Northeast Ridge",        SummitElevationFt = 12942, SummitLat = 37.6967, SummitLon = -119.1953, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-humphreys",      Mountain = "Mount Humphreys",      RouteName = "East Arête",             SummitElevationFt = 13992, SummitLat = 37.2705, SummitLon = -118.6730, ClassDifficulty = "5.4", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-darwin",         Mountain = "Mount Darwin",         RouteName = "West Ridge",             SummitElevationFt = 13837, SummitLat = 37.1670, SummitLon = -118.6724, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "temple-crag",          Mountain = "Temple Crag",          RouteName = "Venusian Blind Arête",   SummitElevationFt = 12982, SummitLat = 37.1097, SummitLon = -118.4926, ClassDifficulty = "5.7", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "bear-creek-spire",     Mountain = "Bear Creek Spire",     RouteName = "North Arête",            SummitElevationFt = 13726, SummitLat = 37.3680, SummitLon = -118.7677, ClassDifficulty = "5.8", SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-brewer",         Mountain = "Mount Brewer",         RouteName = "Northwest Ridge",        SummitElevationFt = 13576, SummitLat = 36.7085, SummitLon = -118.4854, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "middle-palisade",      Mountain = "Middle Palisade",      RouteName = "Northeast Face",         SummitElevationFt = 14018, SummitLat = 37.0703, SummitLon = -118.4691, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-tyndall",        Mountain = "Mount Tyndall",        RouteName = "Northwest Rib",          SummitElevationFt = 14025, SummitLat = 36.6557, SummitLon = -118.3373, ClassDifficulty = "3",   SnotelStationTriplet = "428:CA:SNTL" },
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
        new RouteEntity { RangeId = rangeId, Slug = "broads-fork-twin-peaks", Mountain = "Broads Fork Twin Peaks", RouteName = "East Ridge",      SummitElevationFt = 11330, SummitLat = 40.5938, SummitLon = -111.7210, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "dromedary-peak",         Mountain = "Dromedary Peak",         RouteName = "West Ridge",      SummitElevationFt = 11107, SummitLat = 40.5930, SummitLon = -111.7060, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "sunrise-peak",           Mountain = "Sunrise Peak",           RouteName = "South Ridge",     SummitElevationFt = 11275, SummitLat = 40.5909, SummitLon = -111.7112, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-superior",         Mountain = "Mount Superior",         RouteName = "Cardiff Ridge",   SummitElevationFt = 11045, SummitLat = 40.5922, SummitLon = -111.6670, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
        new RouteEntity { RangeId = rangeId, Slug = "mount-raymond",          Mountain = "Mount Raymond",          RouteName = "West Ridge",      SummitElevationFt = 10241, SummitLat = 40.6584, SummitLon = -111.7020, ClassDifficulty = "3", SnotelStationTriplet = "766:UT:SNTL" },
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
