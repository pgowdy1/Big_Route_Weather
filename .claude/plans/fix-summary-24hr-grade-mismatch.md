# Fix: Summary vs Detail 24h Grade Mismatch

**Branch:** `feature/fix-summary-24hr-grade-mismatch`
**Scope:** Backend-only — small.
**Layers:** ASP.NET API.

## Problem

`GET /api/routes` (route card grade) and `GET /api/routes/{slug}` (Next 24h window card grade) both read `c.WindowGrades.Next24h.Grade`. PR #13 aligned the field. But each endpoint recomputes `RouteConditions` from scratch through `ConditionsAggregator.GetConditionsAsync`. Between the two HTTP calls:

- A per-source forecast cache can expire (NWS/OpenMeteo TTL = 60 min) → fresh fetch produces a slightly different blended forecast → different `Next24h` slice → different grade.
- Cloudflare edge cache entries for the two URLs have independent ages.
- The frontend's in-memory `RouteGrid` signal lives until the component is destroyed, so a stale summary signal can be compared against a freshly fetched detail.

## Fix

Cache the computed `RouteConditions` per slug for 5 minutes inside the aggregator using `IMemoryCache`. Both endpoints check the same cache, so a back-to-back summary/detail pair returns the same `Next24h.Grade`.

- `/refresh` endpoints bypass the cache and remove the entry before recomputing.
- Cache only successful computations (i.e., `RouteConditions.Grade is not null`).
- Cache check happens **inside** the existing per-slug `SemaphoreSlim` gate to absorb the thundering herd.

## Affected files

- `backend/RouteWeather.API/Program.cs` — register `IMemoryCache`.
- `backend/RouteWeather.API/Services/ConditionsAggregator.cs` — inject `IMemoryCache`, add `useCache` flag, wrap compute.
- `backend/RouteWeather.API/Controllers/RoutesController.cs` — pass `useCache: false` from the `/refresh` endpoints.

## Test plan

- New unit test: hit `GetConditionsAsync` twice within the TTL → same `RouteConditions` instance returned.
- New unit test: hit `GetConditionsAsync(..., useCache: false)` evicts the cache.
- Existing aggregator/grading tests must keep passing.

## Verification

```bash
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
```
