using RouteWeather.Core.Services;

namespace RouteWeather.Core.Tests.Services;

public class DailyCallCounterTests
{
    [Fact]
    public void Increment_AddsToTodaysBucket()
    {
        var counter = new DailyCallCounter();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        counter.Increment("NWS");
        counter.Increment("NWS");
        counter.Increment("OpenMeteo");

        var snapshot = counter.Snapshot();
        Assert.Equal(2, snapshot.Single(e => e.Source == "NWS" && e.DateUtc == today).Count);
        Assert.Equal(1, snapshot.Single(e => e.Source == "OpenMeteo" && e.DateUtc == today).Count);
    }

    [Fact]
    public void Hydrate_PopulatesEntriesFromPersistedState()
    {
        var counter = new DailyCallCounter();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        counter.Hydrate(new[]
        {
            new DailyCallEntry(yesterday, "NWS", 42),
            new DailyCallEntry(today, "OpenMeteo", 17),
        });

        var snapshot = counter.Snapshot();
        Assert.Equal(42, snapshot.Single(e => e.DateUtc == yesterday && e.Source == "NWS").Count);
        Assert.Equal(17, snapshot.Single(e => e.DateUtc == today && e.Source == "OpenMeteo").Count);
    }

    [Fact]
    public void Increment_AfterHydrate_BuildsOnPersistedCount()
    {
        var counter = new DailyCallCounter();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        counter.Hydrate(new[] { new DailyCallEntry(today, "NWS", 100) });

        counter.Increment("NWS");

        Assert.Equal(101, counter.Snapshot().Single(e => e.Source == "NWS" && e.DateUtc == today).Count);
    }

    [Fact]
    public void DropFlushedPastDays_RemovesOnlyPastDateEntries()
    {
        var counter = new DailyCallCounter();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        var twoDaysAgo = today.AddDays(-2);

        counter.Hydrate(new[]
        {
            new DailyCallEntry(today, "NWS", 5),
            new DailyCallEntry(yesterday, "NWS", 3),
            new DailyCallEntry(twoDaysAgo, "NWS", 1),
        });

        counter.DropFlushedPastDays(today, new[]
        {
            (yesterday, "NWS"),
            (twoDaysAgo, "NWS"),
            (today, "NWS"),
        });

        var snapshot = counter.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal(today, snapshot[0].DateUtc);
        Assert.Equal(5, snapshot[0].Count);
    }

    [Fact]
    public void DropFlushedPastDays_DoesNotRemoveTodayEntriesEvenIfPassedInList()
    {
        var counter = new DailyCallCounter();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        counter.Hydrate(new[] { new DailyCallEntry(today, "NWS", 5) });

        counter.DropFlushedPastDays(today, new[] { (today, "NWS") });

        Assert.Equal(5, counter.Snapshot().Single().Count);
    }

    [Fact]
    public void Snapshot_ReturnsEntriesAcrossAllStoredDates()
    {
        var counter = new DailyCallCounter();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        counter.Hydrate(new[]
        {
            new DailyCallEntry(today, "NWS", 1),
            new DailyCallEntry(yesterday, "NWS", 2),
            new DailyCallEntry(today, "OpenMeteo", 3),
        });

        var snapshot = counter.Snapshot();

        Assert.Equal(3, snapshot.Count);
        Assert.Contains(snapshot, e => e.DateUtc == today && e.Source == "NWS" && e.Count == 1);
        Assert.Contains(snapshot, e => e.DateUtc == yesterday && e.Source == "NWS" && e.Count == 2);
        Assert.Contains(snapshot, e => e.DateUtc == today && e.Source == "OpenMeteo" && e.Count == 3);
    }
}
