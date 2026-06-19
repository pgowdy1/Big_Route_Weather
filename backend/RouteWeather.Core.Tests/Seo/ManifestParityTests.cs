using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using Xunit;

namespace RouteWeather.Core.Tests.Seo;

public class ManifestParityTests
{
    private sealed record PeakSeo(
        string Slug, string Mountain, string RouteName, int SummitElevationFt,
        string ClassDifficulty, string RangeName, string RangeSlug,
        double SummitLat, double SummitLon);

    [Fact]
    public async Task Manifest_matches_the_seeder_catalog()
    {
        var manifest = LoadManifest();

        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);
        var seeded = await db.Routes.Include(r => r.Range).ToListAsync();

        // Same set of slugs.
        var manifestSlugs = manifest.Select(p => p.Slug).OrderBy(s => s).ToArray();
        var seededSlugs = seeded.Select(r => r.Slug).OrderBy(s => s).ToArray();
        Assert.Equal(seededSlugs, manifestSlugs);

        // Same key fields per slug (catches a stale regenerate).
        var bySlug = manifest.ToDictionary(p => p.Slug);
        foreach (var r in seeded)
        {
            var p = bySlug[r.Slug];
            Assert.Equal(r.Mountain, p.Mountain);
            Assert.Equal(r.RouteName, p.RouteName);
            Assert.Equal(r.SummitElevationFt, p.SummitElevationFt);
            Assert.Equal(r.ClassDifficulty, p.ClassDifficulty);
            Assert.Equal(r.Range!.Slug, p.RangeSlug);
            Assert.Equal(r.Range!.Name, p.RangeName);
        }
    }

    private static List<PeakSeo> LoadManifest()
    {
        var path = FindRepoFile("frontend/src/app/seo/peaks.manifest.json");
        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<PeakSeo>>(json, opts)!;
    }

    // Walk up from the test bin dir to the repo root and resolve a known file.
    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {relative} walking up from {AppContext.BaseDirectory}");
    }

    private static RouteWeatherContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RouteWeatherContext(opts);
    }
}
