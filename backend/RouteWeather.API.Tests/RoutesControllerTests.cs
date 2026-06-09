using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RouteWeather.API.Controllers;
using RouteWeather.API.Services;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class RoutesControllerTests
{
    private static RoutesController Build(TestDbContextFactory dbFactory, IConditionsAggregator aggregator) =>
        new(new RouteRepository(dbFactory), aggregator)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task GetAll_AllFresh_KeepsEdgeCachePolicy()
    {
        var dbFactory = new TestDbContextFactory(nameof(GetAll_AllFresh_KeepsEdgeCachePolicy));
        await TestData.SeedRoutesAsync(dbFactory, TestData.Route());
        var controller = Build(dbFactory, new FakeConditionsAggregator());

        var result = await controller.GetAll(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("public, max-age=900, stale-while-revalidate=3600",
            controller.Response.Headers.CacheControl.ToString());
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
}
