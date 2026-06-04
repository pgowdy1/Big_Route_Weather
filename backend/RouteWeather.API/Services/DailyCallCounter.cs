using System.Collections.Concurrent;

namespace RouteWeather.API.Services;

public class DailyCallCounter
{
    private readonly ConcurrentDictionary<string, int> _counts = new();
    private readonly ILogger<DailyCallCounter> _logger;
    private DateOnly _bucketDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly object _rollLock = new();

    public DailyCallCounter(ILogger<DailyCallCounter> logger) => _logger = logger;

    public void Increment(string sourceName)
    {
        RollIfNeeded();
        var newValue = _counts.AddOrUpdate(sourceName, 1, (_, v) => v + 1);
        if (newValue % 100 == 0)
        {
            _logger.LogInformation("Source {Source} daily call count: {Count} (bucket {Bucket})", sourceName, newValue, _bucketDate);
        }
    }

    public IReadOnlyDictionary<string, int> Snapshot()
    {
        RollIfNeeded();
        return new Dictionary<string, int>(_counts);
    }

    private void RollIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today == _bucketDate) return;
        lock (_rollLock)
        {
            if (today == _bucketDate) return;
            foreach (var (k, v) in _counts)
            {
                _logger.LogInformation("Daily roll for {Source}: {Count} on {Date}", k, v, _bucketDate);
            }
            _counts.Clear();
            _bucketDate = today;
        }
    }
}
