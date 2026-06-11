using Microsoft.Extensions.Options;
using RouteWeather.API.Options;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

/// Owns all upstream weather fetching: re-aggregates every route on startup and
/// on a fixed interval so user requests are pure cache reads. The single-loop
/// do/while makes cycles non-reentrant — a slow cycle delays the next tick.
public class ConditionsWarmerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WarmerOptions _options;
    private readonly ILogger<ConditionsWarmerService> _logger;

    public ConditionsWarmerService(
        IServiceScopeFactory scopeFactory,
        IOptions<WarmerOptions> options,
        ILogger<ConditionsWarmerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Conditions warmer disabled by configuration");
            return;
        }

        // Yield so host startup is never blocked by the first (long) cycle.
        await Task.Yield();

        // A misconfigured interval must not kill the warmer: post-inversion it is
        // the only path to upstream data, so clamp instead of throwing.
        var intervalMinutes = _options.IntervalMinutes;
        if (intervalMinutes < 1)
        {
            _logger.LogWarning("Warmer IntervalMinutes={Configured} is invalid; clamping to 1", intervalMinutes);
            intervalMinutes = 1;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        do
        {
            try
            {
                await RunCycleAsync(ct);
            }
            // Realistic trigger: cancellation during the initial GetAllAsync —
            // per-route OCEs are swallowed inside the cycle, so they never reach here.
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Warm cycle failed; retrying on next tick");
            }
        }
        while (await WaitForNextTickSafeAsync(timer, ct));
    }

    internal async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var routes = await scope.ServiceProvider.GetRequiredService<RouteRepository>().GetAllAsync(ct);
        var aggregator = scope.ServiceProvider.GetRequiredService<IConditionsAggregator>();

        // Low fan-out by design: warm cycles run on a single shared CPU in prod, and
        // a wide post-deploy cold cycle starves the thread pool (outage 2026-06-11).
        var maxConcurrent = Math.Max(1, _options.MaxConcurrentRoutes);
        using var gate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        var tasks = routes.Select(async route =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await aggregator.GetConditionsAsync(route, FetchMode.ReadThrough, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown — let the cycle wind down quietly.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Warm fetch failed for {Slug}", route.Slug);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);

        if (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("Warm cycle completed for {Count} routes", routes.Count);
        }
    }

    private static async Task<bool> WaitForNextTickSafeAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
