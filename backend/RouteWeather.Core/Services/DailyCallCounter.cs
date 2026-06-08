using System.Collections.Concurrent;

namespace RouteWeather.Core.Services;

public record DailyCallEntry(DateOnly DateUtc, string Source, int Count);

public class DailyCallCounter
{
    private readonly ConcurrentDictionary<(DateOnly Date, string Source), int> _counts = new();

    public void Increment(string sourceName)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _counts.AddOrUpdate((today, sourceName), 1, (_, v) => v + 1);
    }

    public IReadOnlyList<DailyCallEntry> Snapshot() =>
        _counts.Select(kv => new DailyCallEntry(kv.Key.Date, kv.Key.Source, kv.Value)).ToList();

    public void Hydrate(IEnumerable<DailyCallEntry> persisted)
    {
        foreach (var entry in persisted)
        {
            _counts[(entry.DateUtc, entry.Source)] = entry.Count;
        }
    }

    public void DropFlushedPastDays(DateOnly today, IEnumerable<(DateOnly Date, string Source)> flushedKeys)
    {
        foreach (var key in flushedKeys)
        {
            if (key.Date < today)
            {
                _counts.TryRemove(key, out _);
            }
        }
    }
}
