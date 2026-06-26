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
        double SummitLat, double SummitLon, bool IsGlaciated);

    [Fact]
    public async Task Manifest_matches_the_seeder_catalog()
    {
        var manifest = LoadManifest();
        Assert.NotNull(manifest);

        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);
        var seeded = await db.Routes.Include(r => r.Range).ToListAsync();

        // Same set of slugs.
        var manifestSlugs = manifest.Select(p => p.Slug).OrderBy(s => s).ToArray();
        var seededSlugs = seeded.Select(r => r.Slug).OrderBy(s => s).ToArray();
        Assert.Equal(seededSlugs, manifestSlugs);

        // Same key fields per slug (catches a stale regenerate). Collect every
        // mismatch and name the slug + field so a failure pinpoints the culprit
        // among 124 peaks instead of a bare Expected/Actual.
        var bySlug = manifest.ToDictionary(p => p.Slug);
        var mismatches = new List<string>();
        foreach (var r in seeded)
        {
            var p = bySlug[r.Slug];
            void Check(string field, object? seededVal, object? manifestVal)
            {
                if (!Equals(seededVal, manifestVal))
                    mismatches.Add($"{r.Slug}.{field}: seeder='{seededVal}' manifest='{manifestVal}'");
            }
            Check("Mountain", r.Mountain, p.Mountain);
            Check("RouteName", r.RouteName, p.RouteName);
            Check("SummitElevationFt", r.SummitElevationFt, p.SummitElevationFt);
            Check("ClassDifficulty", r.ClassDifficulty, p.ClassDifficulty);
            Check("RangeSlug", r.Range!.Slug, p.RangeSlug);
            Check("RangeName", r.Range!.Name, p.RangeName);
            Check("SummitLat", r.SummitLat, p.SummitLat);
            Check("SummitLon", r.SummitLon, p.SummitLon);
            Check("IsGlaciated", r.IsGlaciated, p.IsGlaciated);
        }
        Assert.True(mismatches.Count == 0, $"Manifest/seeder field mismatches:\n{string.Join("\n", mismatches)}");
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
