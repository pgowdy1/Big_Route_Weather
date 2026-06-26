using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RouteWeather.API.Controllers;
using RouteWeather.API.Services;
using RouteWeather.Core.Models;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class RoutesControllerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static RoutesController Build(TestDbContextFactory dbFactory, IConditionsAggregator aggregator) =>
        new(new RouteRepository(dbFactory), aggregator)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    /// Serializes the anonymous DTO the controller returns into the same camelCase
    /// JSON the API emits, so assertions exercise the real wire shape.
    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, JsonOpts);

    [Fact]
    public async Task GetAll_AllFresh_KeepsEdgeCachePolicy()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_AllFresh_KeepsEdgeCachePolicy));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var fake = new FakeConditionsAggregator();
        var controller = Build(dbFactory, fake);

        var result = await controller.GetAll(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("public, max-age=900, stale-while-revalidate=3600",
            controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(1, fake.Calls);      // one bulk call...
        Assert.Empty(fake.ModesSeen);     // ...and zero per-route fan-out
    }

    [Fact]
    public async Task GetAll_AnyStale_SendsNoCache()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_AnyStale_SendsNoCache));
        await TestData.SeedRoutesAsync(dbFactory,
            TestData.Route(id: 1, slug: "mt-fresh", mountain: "Mt Fresh"),
            TestData.Route(id: 2, slug: "mt-stale", mountain: "Mt Stale"));
        var fake = new FakeConditionsAggregator
        {
            OnGet = r => TestData.Conditions(r, isStale: r.Slug == "mt-stale"),
        };
        var controller = Build(dbFactory, fake);

        await controller.GetAll(CancellationToken.None);

        Assert.Equal("no-cache", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetBySlug_Stale_SendsNoCache()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_Stale_SendsNoCache));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var fake = new FakeConditionsAggregator { OnGet = r => TestData.Conditions(r, isStale: true) };
        var controller = Build(dbFactory, fake);

        var result = await controller.GetBySlug("mt-test", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("no-cache", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(FetchMode.CacheOnly, fake.ModesSeen.Single());
    }

    [Fact]
    public async Task GetBySlug_Fresh_KeepsEdgeCachePolicy()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_Fresh_KeepsEdgeCachePolicy));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        await controller.GetBySlug("mt-test", CancellationToken.None);

        Assert.Equal("public, max-age=900, stale-while-revalidate=3600",
            controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetBySlug_UnknownSlug_ReturnsNotFound()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_UnknownSlug_ReturnsNotFound));
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        var result = await controller.GetBySlug("nope", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetAll_summaryIncludesAirQualityUsAqi()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_summaryIncludesAirQualityUsAqi));
        await TestData.SeedRoutesAsync(dbFactory,
            TestData.Route(id: 1, slug: "mt-aqi", mountain: "Mt Aqi"),
            TestData.Route(id: 2, slug: "mt-noaqi", mountain: "Mt NoAqi"));
        var fake = new FakeConditionsAggregator
        {
            OnGet = r => TestData.Conditions(r, isStale: false,
                airQuality: r.Slug == "mt-aqi" ? new AirQualitySnapshot(168, 60.0) : null),
        };
        var controller = Build(dbFactory, fake);

        var summaries = Json(await controller.GetAll(CancellationToken.None));

        var withAqi = summaries.EnumerateArray().Single(s => s.GetProperty("slug").GetString() == "mt-aqi");
        Assert.Equal(168, withAqi.GetProperty("airQualityUsAqi").GetInt32());

        var withoutAqi = summaries.EnumerateArray().Single(s => s.GetProperty("slug").GetString() == "mt-noaqi");
        Assert.Equal(JsonValueKind.Null, withoutAqi.GetProperty("airQualityUsAqi").ValueKind);
    }

    [Fact]
    public async Task GetBySlug_detailIncludesAirQualityDaylightAndPerSourceExtras()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_detailIncludesAirQualityDaylightAndPerSourceExtras));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var fetchedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var perSource = new List<PerSourceForecast>
        {
            new("NWS", WindMph: 12, TempF: 28, PrecipitationProbabilityPct: 20,
                FetchedAt: fetchedAt, MaxGustMph: 42.5, MaxCapeJkg: 850),
            new("OpenMeteo", WindMph: 10, TempF: 27, PrecipitationProbabilityPct: 15,
                FetchedAt: fetchedAt, MaxGustMph: null, MaxCapeJkg: null),
        };
        var fake = new FakeConditionsAggregator
        {
            OnGet = r => TestData.Conditions(r, isStale: false,
                airQuality: new AirQualitySnapshot(155, 42.5),
                airQualityFetchedAt: fetchedAt,
                perSourceForecast: perSource),
        };
        var controller = Build(dbFactory, fake);

        var detail = Json(await controller.GetBySlug("mt-test", CancellationToken.None));

        var aqi = detail.GetProperty("airQuality");
        Assert.Equal(155, aqi.GetProperty("usAqi").GetInt32());
        Assert.Equal(42.5, aqi.GetProperty("pm25").GetDouble());
        Assert.Equal(JsonValueKind.String, aqi.GetProperty("fetchedAt").ValueKind);

        // Mid-latitude fixture coords -> NextDaylight returns non-null; exact times
        // are clock-dependent, so assert structure and a positive day length only.
        var daylight = detail.GetProperty("daylight");
        Assert.Equal(JsonValueKind.String, daylight.GetProperty("sunriseUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, daylight.GetProperty("sunsetUtc").ValueKind);
        Assert.True(daylight.GetProperty("daylightHours").GetDouble() > 0);

        var sources = detail.GetProperty("perSourceForecast");
        Assert.Equal(42.5, sources[0].GetProperty("maxGustMph").GetDouble());
        Assert.Equal(850, sources[0].GetProperty("maxCapeJkg").GetDouble());
        Assert.Equal(JsonValueKind.Null, sources[1].GetProperty("maxGustMph").ValueKind);
        Assert.Equal(JsonValueKind.Null, sources[1].GetProperty("maxCapeJkg").ValueKind);
    }

    [Fact]
    public async Task GetAll_summaryIncludesIsGlaciated()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_summaryIncludesIsGlaciated));
        var ice = TestData.Route(id: 1, slug: "mt-ice", mountain: "Mt Ice");
        ice.IsGlaciated = true;
        var rock = TestData.Route(id: 2, slug: "mt-rock", mountain: "Mt Rock");
        await TestData.SeedRoutesAsync(dbFactory, ice, rock);
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        var summaries = Json(await controller.GetAll(CancellationToken.None));

        var iced = summaries.EnumerateArray().Single(s => s.GetProperty("slug").GetString() == "mt-ice");
        Assert.True(iced.GetProperty("isGlaciated").GetBoolean());
        var rocked = summaries.EnumerateArray().Single(s => s.GetProperty("slug").GetString() == "mt-rock");
        Assert.False(rocked.GetProperty("isGlaciated").GetBoolean());
    }

    [Fact]
    public async Task GetBySlug_detailIncludesIsGlaciated()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetBySlug_detailIncludesIsGlaciated));
        var ice = TestData.Route(id: 1, slug: "mt-test", mountain: "Mt Test");
        ice.IsGlaciated = true;
        var rock = TestData.Route(id: 2, slug: "mt-rock", mountain: "Mt Rock");
        await TestData.SeedRoutesAsync(dbFactory, ice, rock);
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        var detail = Json(await controller.GetBySlug("mt-test", CancellationToken.None));
        Assert.True(detail.GetProperty("isGlaciated").GetBoolean());

        var rockDetail = Json(await controller.GetBySlug("mt-rock", CancellationToken.None));
        Assert.False(rockDetail.GetProperty("isGlaciated").GetBoolean());
    }
}
