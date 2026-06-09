using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RouteWeather.API.Options;
using RouteWeather.API.Services;
using RouteWeather.Data;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Tests;

public class ConditionsWarmerServiceTests
{
    private static (IServiceScopeFactory ScopeFactory, FakeConditionsAggregator Fake) BuildScope(TestDbContextFactory dbFactory)
    {
        var fake = new FakeConditionsAggregator();
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<RouteWeatherContext>>(dbFactory);
        services.AddScoped<RouteRepository>();
        services.AddSingleton<IConditionsAggregator>(fake);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), fake);
    }

    // Fully qualified on purpose: inside namespace RouteWeather.API.Tests, a bare
    // `Options` resolves to the RouteWeather.API.Options NAMESPACE (enclosing-namespace
    // lookup beats using directives) and fails to compile.
    private static ConditionsWarmerService BuildWarmer(IServiceScopeFactory scopeFactory, WarmerOptions? options = null) =>
        new(scopeFactory, Microsoft.Extensions.Options.Options.Create(options ?? new WarmerOptions()),
            NullLogger<ConditionsWarmerService>.Instance);

    [Fact]
    public async Task RunCycle_WarmsEveryRoute_InReadThroughMode()
    {
        var dbFactory = new TestDbContextFactory(nameof(RunCycle_WarmsEveryRoute_InReadThroughMode));
        await TestData.SeedRoutesAsync(dbFactory,
            TestData.Route(id: 1, slug: "mt-a", mountain: "Mt A"),
            TestData.Route(id: 2, slug: "mt-b", mountain: "Mt B"),
            TestData.Route(id: 3, slug: "mt-c", mountain: "Mt C"));
        var (scopeFactory, fake) = BuildScope(dbFactory);
        var warmer = BuildWarmer(scopeFactory);

        await warmer.RunCycleAsync(CancellationToken.None);

        Assert.Equal(3, fake.Calls);
        Assert.All(fake.ModesSeen, m => Assert.Equal(FetchMode.ReadThrough, m));
    }

    [Fact]
    public async Task RunCycle_OneRouteThrows_OthersStillWarm_NoExceptionEscapes()
    {
        var dbFactory = new TestDbContextFactory(nameof(RunCycle_OneRouteThrows_OthersStillWarm_NoExceptionEscapes));
        await TestData.SeedRoutesAsync(dbFactory,
            TestData.Route(id: 1, slug: "mt-good", mountain: "Mt Good"),
            TestData.Route(id: 2, slug: "mt-bad", mountain: "Mt Bad"),
            TestData.Route(id: 3, slug: "mt-fine", mountain: "Mt Fine"));
        var (scopeFactory, fake) = BuildScope(dbFactory);
        fake.OnGet = r => r.Slug == "mt-bad"
            ? throw new InvalidOperationException("boom")
            : TestData.Conditions(r, isStale: false);
        var warmer = BuildWarmer(scopeFactory);

        await warmer.RunCycleAsync(CancellationToken.None); // must not throw

        Assert.Equal(3, fake.Calls); // all three attempted
    }

    [Fact]
    public async Task Disabled_RunsNoCycles()
    {
        var dbFactory = new TestDbContextFactory(nameof(Disabled_RunsNoCycles));
        var (scopeFactory, fake) = BuildScope(dbFactory);
        var warmer = BuildWarmer(scopeFactory, new WarmerOptions { Enabled = false });

        await warmer.StartAsync(CancellationToken.None);
        await (warmer.ExecuteTask ?? Task.CompletedTask);
        await warmer.StopAsync(CancellationToken.None);

        Assert.Equal(0, fake.Calls);
    }
}
