# Big Route Weather

A grade card (A–F) for conditions on big mountain routes. MVP covers three popular Colorado 14ers — Longs Peak (Keyhole), Capitol Peak (Knife Edge), Pyramid Peak (NE Ridge) — using NOAA NWS forecasts and USDA NRCS SNOTEL snowpack.

## Prerequisites

- .NET 10 SDK
- Node.js 22+ and npm 11+

## Run

```bash
# Backend (from repo root) — listens on http://localhost:5150
dotnet run --project backend/RouteWeather.API

# Frontend (in another shell, from repo root) — serves http://localhost:4200
cd frontend && npm start
```

The Angular dev server proxies `/api/*` to the backend, so the UI just works once both are running.

## Test

```bash
dotnet test backend/RouteWeather.slnx     # backend (xUnit)
cd frontend && npm test                   # frontend (Vitest)
```

## Notes

- The SQLite database (`routeweather.db`) is created and seeded on first API boot — no manual setup.
- No API keys required; both NWS and SNOTEL are free public endpoints.
- Forecasts and snowpack are cached per route for 1 hour. If an upstream is unreachable, the API serves the last cached value and flags it as `isStale`.
