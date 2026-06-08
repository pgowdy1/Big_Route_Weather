using Microsoft.AspNetCore.Mvc;
using RouteWeather.Core.Services;
using RouteWeather.Data.Repositories;

namespace RouteWeather.API.Controllers;

[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly DailyCallCounter _counter;
    private readonly DailyApiCallRepository _repo;

    public DiagnosticsController(DailyCallCounter counter, DailyApiCallRepository repo)
    {
        _counter = counter;
        _repo = repo;
    }

    [HttpGet("daily-calls")]
    public async Task<IActionResult> GetDailyCalls([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var clampedDays = Math.Clamp(days, 1, 30);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-clampedDays + 1);

        var persisted = await _repo.GetSinceAsync(since, ct);
        var inMemory = _counter.Snapshot();

        var merged = new Dictionary<(DateOnly DateUtc, string Source), int>();
        foreach (var row in persisted)
        {
            merged[(row.DateUtc, row.Source)] = row.Count;
        }
        foreach (var entry in inMemory)
        {
            if (entry.DateUtc < since) continue;
            merged[(entry.DateUtc, entry.Source)] = entry.Count;
        }

        var entries = merged
            .Select(kv => new DailyCallDto(
                DateUtc: kv.Key.DateUtc.ToString("yyyy-MM-dd"),
                Source: kv.Key.Source,
                Count: kv.Value,
                IsToday: kv.Key.DateUtc == today))
            .OrderByDescending(e => e.DateUtc)
            .ThenBy(e => e.Source)
            .ToList();

        return Ok(new DailyCallsResponse(
            Days: clampedDays,
            AsOfUtc: DateTime.UtcNow,
            Entries: entries));
    }

    public record DailyCallsResponse(int Days, DateTime AsOfUtc, IReadOnlyList<DailyCallDto> Entries);
    public record DailyCallDto(string DateUtc, string Source, int Count, bool IsToday);
}
