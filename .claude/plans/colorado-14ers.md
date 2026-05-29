# Plan: Add all Colorado 14ers (v1.0)

**Branch:** `feature/colorado-14ers`
**Layers:** Backend (seed data + small controller change). No frontend changes, no schema migrations.
**Scope:** Medium — mostly data entry; one small concurrency tweak.

## Requirements

- Seed all 58 peaks from the 14ers.com list (the climber's list, includes unranked sub-peaks).
- One route per peak — the standard / most popular route.
- Hand-picked nearest SNOTEL station triplet per peak (skip with empty string only if no reasonable station exists; aggregator already tolerates SNOTEL failures gracefully).
- Cap parallel fan-out in `RoutesController.GetAll` at 8 in-flight to avoid hammering NWS/SNOTEL APIs.

## Affected files

**Modified:**
- `backend/RouteWeather.Data/RouteSeeder.cs` — replace 3-route seed with 58-route seed
- `backend/RouteWeather.API/Controllers/RoutesController.cs` — bound parallel fan-out at 8

**Not changed:**
- DB schema (`RouteEntity`, `RouteWeatherContext`) — already fits
- Migrations — no schema change
- Frontend — `route-grid` already iterates generically
- Grading logic — unchanged

## The 58-peak list (14ers.com)

In rank order. Each row will become a `RouteEntity` in the seeder.

| # | Mountain | Standard Route | Elev (ft) | Lat | Lon | Class | SNOTEL Triplet |
|---|---|---|---|---|---|---|---|
| 1 | Mount Elbert | East Ridge / NE Ridge | 14438 | 39.1178 | -106.4453 | 1 | 369:CO:SNTL (Independence Pass) |
| 2 | Mount Massive | East Slopes | 14428 | 39.1873 | -106.4756 | 2 | 1101:CO:SNTL (Brumley) |
| 3 | Mount Harvard | South Slopes | 14421 | 38.9244 | -106.3206 | 2 | 1057:CO:SNTL (St. Elmo) |
| 4 | Blanca Peak | NW Ridge | 14345 | 37.5775 | -105.4856 | 2 | 1141:CO:SNTL (Medano Pass) |
| 5 | La Plata Peak | NW Ridge | 14336 | 39.0294 | -106.4729 | 2 | 369:CO:SNTL (Independence Pass) |
| 6 | Uncompahgre Peak | South Ridge | 14309 | 38.0717 | -107.4622 | 2 | 1186:CO:SNTL (Red Mountain Pass) |
| 7 | Crestone Peak | South Face (Red Gully) | 14294 | 37.9669 | -105.5853 | 3 | 1128:CO:SNTL (Medano Pass) |
| 8 | Mount Lincoln | West Ridge (DeCaLiBron) | 14286 | 39.3514 | -106.1117 | 2 | 1120:CO:SNTL (Hoosier Pass) |
| 9 | Grays Peak | North Slopes | 14270 | 39.6339 | -105.8175 | 1 | 1187:CO:SNTL (Loveland Basin) |
| 10 | Mount Antero | West Slopes (Baldwin Gulch) | 14269 | 38.6741 | -106.2461 | 2 | 1057:CO:SNTL (St. Elmo) |
| 11 | Torreys Peak | South Slopes (via Grays) | 14267 | 39.6428 | -105.8211 | 2 | 1187:CO:SNTL (Loveland Basin) |
| 12 | Castle Peak | NE Ridge | 14265 | 39.0094 | -106.8614 | 2 | 542:CO:SNTL (Schofield Pass) |
| 13 | Quandary Peak | East Ridge | 14265 | 39.3973 | -106.1064 | 1 | 1120:CO:SNTL (Hoosier Pass) |
| 14 | Mount Evans | Northeast Face | 14264 | 39.5883 | -105.6438 | 2 | 1186:CO:SNTL (Loveland Basin) |
| 15 | Longs Peak | Keyhole | 14255 | 40.2549 | -105.6160 | 3 | 1042:CO:SNTL (Willow Park) |
| 16 | Mount Wilson | North Slopes | 14246 | 37.8389 | -107.9911 | 4 | 1060:CO:SNTL (Lizard Head Pass) |
| 17 | Mount Cameron | DeCaLiBron via saddle | 14238 | 39.3464 | -106.1186 | 2 | 1120:CO:SNTL (Hoosier Pass) |
| 18 | Mount Shavano | East Slopes (Angel of Shavano) | 14229 | 38.6192 | -106.2253 | 2 | 1057:CO:SNTL (St. Elmo) |
| 19 | Mount Belford | Northwest Ridge | 14197 | 38.9606 | -106.3608 | 2 | 1057:CO:SNTL (St. Elmo) |
| 20 | Crestone Needle | South Face | 14197 | 37.9647 | -105.5764 | 3 | 1128:CO:SNTL (Medano Pass) |
| 21 | Mount Princeton | East Slopes | 14197 | 38.7492 | -106.2425 | 2 | 1057:CO:SNTL (St. Elmo) |
| 22 | Mount Yale | Southwest Slopes | 14196 | 38.8442 | -106.3136 | 2 | 1057:CO:SNTL (St. Elmo) |
| 23 | Mount Bross | West Slopes (DeCaLiBron) | 14172 | 39.3358 | -106.1078 | 2 | 1120:CO:SNTL (Hoosier Pass) |
| 24 | Kit Carson Peak | North Ridge (via Challenger) | 14165 | 37.9794 | -105.6028 | 3 | 1128:CO:SNTL (Medano Pass) |
| 25 | Maroon Peak | South Ridge | 14156 | 39.0708 | -106.9889 | 4 | 542:CO:SNTL (Schofield Pass) |
| 26 | Tabeguache Peak | West Ridge (via Shavano) | 14155 | 38.6258 | -106.2386 | 2 | 1057:CO:SNTL (St. Elmo) |
| 27 | Mount Oxford | West Ridge (via Belford) | 14153 | 38.9647 | -106.3389 | 2 | 1057:CO:SNTL (St. Elmo) |
| 28 | Mount Sneffels | Southwest Ridge (Lavender Col) | 14150 | 38.0036 | -107.7925 | 3 | 1186:CO:SNTL (Red Mountain Pass) |
| 29 | Mount Democrat | East Slopes (DeCaLiBron) | 14148 | 39.3394 | -106.1397 | 2 | 1120:CO:SNTL (Hoosier Pass) |
| 30 | Capitol Peak | Northeast Ridge (Knife Edge) | 14130 | 39.1503 | -107.0830 | 4 | 542:CO:SNTL (Schofield Pass) |
| 31 | Pikes Peak | Barr Trail | 14110 | 38.8409 | -105.0442 | 1 | 1057:CO:SNTL (St. Elmo) |
| 32 | Snowmass Mountain | East Slopes | 14092 | 39.1186 | -107.0664 | 3 | 542:CO:SNTL (Schofield Pass) |
| 33 | Mount Eolus | NE Ridge | 14083 | 37.6219 | -107.6225 | 3 | 1060:CO:SNTL (Lizard Head Pass) |
| 34 | Windom Peak | West Ridge | 14082 | 37.6214 | -107.5917 | 2 | 1060:CO:SNTL (Lizard Head Pass) |
| 35 | Challenger Point | North Slopes | 14081 | 37.9803 | -105.6064 | 2 | 1128:CO:SNTL (Medano Pass) |
| 36 | Mount Columbia | West Slopes | 14077 | 38.9039 | -106.2972 | 2 | 1057:CO:SNTL (St. Elmo) |
| 37 | Missouri Mountain | Northwest Ridge | 14074 | 38.9478 | -106.3789 | 2 | 1057:CO:SNTL (St. Elmo) |
| 38 | Humboldt Peak | West Ridge | 14070 | 37.9764 | -105.5550 | 2 | 1128:CO:SNTL (Medano Pass) |
| 39 | Mount Bierstadt | West Slopes | 14065 | 39.5828 | -105.6685 | 2 | 1187:CO:SNTL (Loveland Basin) |
| 40 | Conundrum Peak | NE Ridge (via Castle) | 14060 | 39.0064 | -106.8675 | 2 | 542:CO:SNTL (Schofield Pass) |
| 41 | Sunlight Peak | South Face | 14059 | 37.6275 | -107.5950 | 4 | 1060:CO:SNTL (Lizard Head Pass) |
| 42 | Handies Peak | American Basin | 14048 | 37.9131 | -107.5042 | 2 | 1186:CO:SNTL (Red Mountain Pass) |
| 43 | Culebra Peak | Northwest Ridge | 14047 | 37.1225 | -105.1856 | 2 | 1141:CO:SNTL (Medano Pass) |
| 44 | Ellingwood Point | South Face (via Blanca) | 14042 | 37.5822 | -105.4925 | 2 | 1141:CO:SNTL (Medano Pass) |
| 45 | Mount Lindsey | Northwest Ridge | 14042 | 37.5836 | -105.4456 | 2 | 1141:CO:SNTL (Medano Pass) |
| 46 | North Eolus | Eolus Ridge | 14039 | 37.6228 | -107.6233 | 3 | 1060:CO:SNTL (Lizard Head Pass) |
| 47 | Little Bear Peak | West Ridge (Hourglass) | 14037 | 37.5667 | -105.4972 | 4 | 1141:CO:SNTL (Medano Pass) |
| 48 | Mount Sherman | Southwest Ridge | 14036 | 39.2253 | -106.1697 | 2 | 1120:CO:SNTL (Hoosier Pass) |
| 49 | Redcloud Peak | NE Ridge | 14034 | 37.9408 | -107.4214 | 2 | 1186:CO:SNTL (Red Mountain Pass) |
| 50 | Pyramid Peak | NE Ridge | 14018 | 39.0716 | -106.9501 | 4 | 542:CO:SNTL (Schofield Pass) |
| 51 | Wilson Peak | West Ridge | 14017 | 37.8597 | -107.9847 | 3 | 1060:CO:SNTL (Lizard Head Pass) |
| 52 | Wetterhorn Peak | Southeast Ridge | 14015 | 38.0606 | -107.5106 | 3 | 1186:CO:SNTL (Red Mountain Pass) |
| 53 | North Maroon Peak | NE Ridge | 14014 | 39.0758 | -106.9883 | 4 | 542:CO:SNTL (Schofield Pass) |
| 54 | San Luis Peak | Northeast Ridge | 14014 | 37.9869 | -106.9311 | 2 | 1186:CO:SNTL (Red Mountain Pass) |
| 55 | Mount of the Holy Cross | North Ridge | 14005 | 39.4669 | -106.4814 | 2 | 1101:CO:SNTL (Brumley) |
| 56 | Huron Peak | Northwest Slopes | 14003 | 38.9453 | -106.4378 | 2 | 1057:CO:SNTL (St. Elmo) |
| 57 | Sunshine Peak | North Slopes (via Redcloud) | 14001 | 37.9258 | -107.4256 | 2 | 1186:CO:SNTL (Red Mountain Pass) |
| 58 | El Diente Peak | North Slopes | 14159 | 37.8394 | -108.0050 | 3 | 1060:CO:SNTL (Lizard Head Pass) |

## Implementation steps

1. **Rewrite `RouteSeeder.cs`** — 58 `new RouteEntity { ... }` initializers in rank order. Idempotent guard (`if (await db.Routes.AnyAsync(ct)) return;`) stays. Slugs are `kebab-case-of-mountain` (e.g. `mount-elbert`, `north-maroon-peak`). The existing `longs-peak-keyhole` slug pattern (`mountain-routekeyword`) becomes just `mountain` since we have one route per peak — adopt the cleaner `kebab(mountain)` convention.
2. **Bounded concurrency in `RoutesController.GetAll`** — replace `routes.Select(...).Task.WhenAll` with a small `SemaphoreSlim(8)`-gated fan-out.
3. **Reseed locally** — delete `app.db` (or wherever the SQLite file lives) so the seeder repopulates on next `dotnet run`.
4. **Build and test** — backend + frontend.

## Edge cases / error handling

- Aggregator already returns `null` snowpack on SNOTEL failure — no extra error paths needed.
- Cache TTL (1h) + bounded concurrency means a cold start fetches ~58 NWS forecasts across ~8 concurrent requests = ~7-8 batched rounds. Acceptable.
- Slugs must be unique (enforced by index). Verify no collision in the 58-row table.

## Test plan

- `dotnet test` — all existing grading tests still pass (no logic change).
- `dotnet build` — backend compiles.
- `npm test` — frontend specs still pass (the contract didn't change).
- Manual: `GET /api/routes` returns 58 items; one slug pulls a detail; UI shows the grid populated.

## Verification commands

```bash
dotnet build
dotnet test --verbosity normal
cd frontend && npm test
# After running the app:
# curl http://localhost:5xxx/api/routes | jq 'length'   -> 58
```

## Complexity assessment

**Solo build (`/new-feature` equivalent).** Two files change. The work is data entry and a 10-line concurrency wrapper. No need to fan out to multiple agents.
