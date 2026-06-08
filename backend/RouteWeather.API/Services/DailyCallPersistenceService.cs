using RouteWeather.Core.Services;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Services;

public class DailyCallPersistenceService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private static readonly int HydrateLookbackDays = 7;

    private readonly DailyCallCounter _counter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyCallPersistenceService> _logger;

    public DailyCallPersistenceService(
        DailyCallCounter counter,
        IServiceScopeFactory scopeFactory,
        ILogger<DailyCallPersistenceService> logger)
    {
        _counter = counter;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken ct)
    {
        await HydrateAsync(ct);
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            await FlushAsync(ct);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await FlushAsync(ct);
        await base.StopAsync(ct);
    }

    private async Task HydrateAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<DailyApiCallRepository>();
            var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-HydrateLookbackDays + 1);
            var rows = await repo.GetSinceAsync(since, ct);
            _counter.Hydrate(rows.Select(r => new DailyCallEntry(r.DateUtc, r.Source, r.Count)));
            if (rows.Count > 0)
            {
                _logger.LogInformation("Hydrated {Count} DailyCallCounter rows from persistence", rows.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hydrate DailyCallCounter from persistence; continuing with empty state");
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var snapshot = _counter.Snapshot();
        if (snapshot.Count == 0) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var nowUtc = DateTime.UtcNow;
        var flushedPastKeys = new List<(DateOnly, string)>();

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<DailyApiCallRepository>();
            foreach (var entry in snapshot)
            {
                await repo.UpsertAsync(entry.DateUtc, entry.Source, entry.Count, nowUtc, ct);
                if (entry.DateUtc < today)
                {
                    flushedPastKeys.Add((entry.DateUtc, entry.Source));
                }
            }
            if (flushedPastKeys.Count > 0)
            {
                _counter.DropFlushedPastDays(today, flushedPastKeys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush DailyCallCounter to persistence; will retry on next interval");
        }
    }
}
