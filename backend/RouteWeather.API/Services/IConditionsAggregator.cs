using RouteWeather.Core.Models;
using RouteWeather.Data.Entities;

namespace RouteWeather.API.Services;

public enum FetchMode
{
    /// Serve from memory cache or last-known SQLite rows; never call upstream sources.
    CacheOnly,

    /// Recompute: respect per-source SQLite TTLs, fetch expired sources upstream,
    /// overwrite the memory cache. Used by the warmer only.
    ReadThrough,
}

public sealed record RouteConditionsPair(RouteEntity Route, RouteConditions Conditions);

public interface IConditionsAggregator
{
    Task<RouteConditions> GetConditionsAsync(RouteEntity routeEntity, FetchMode mode, CancellationToken ct = default);

    Task<IReadOnlyList<RouteConditionsPair>> GetManyCacheOnlyAsync(IReadOnlyList<RouteEntity> routes, CancellationToken ct = default);
}
