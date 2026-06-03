# Edge cache + manual refresh

## Branch
`feature/reduce-api-compute-cost`

## Goal
Cut Fly.io compute by letting Cloudflare's edge cache `/api/routes` and `/api/routes/{slug}` for 15 minutes (with up to 1 hour of stale-while-revalidate). Give users a manual "Refresh" button on both pages that bypasses the edge cache (but still uses the backend's 1-hour data cache, so we don't hammer NWS/SNOTEL).

## Scope & layers
- **Full-stack** (backend headers + new endpoints; frontend service + UI).
- **Size:** small/medium — controller tweaks + two new endpoints + two UI buttons.
- **Recommended:** solo build (`/new-feature`).

## Affected files

### Backend (existing — edit)
- `backend/RouteWeather.API/Controllers/RoutesController.cs`
  - Add `Cache-Control: public, max-age=900, stale-while-revalidate=3600` to `GetAll` and `GetBySlug`.
  - Add `GetAllRefresh` (route `refresh`) and `GetBySlugRefresh` (route `{slug}/refresh`) that produce the same payloads but set `Cache-Control: no-store`.

### Frontend (existing — edit)
- `frontend/src/app/services/routes-service.ts`
  - Add `listRefresh()` -> `GET /api/routes/refresh`.
  - Add `detailRefresh(slug)` -> `GET /api/routes/{slug}/refresh`.
- `frontend/src/app/components/route-grid/route-grid.{ts,html,scss}`
  - "Refresh" button in the header row.
  - "Updated <relative time>" label from `max(routes.updatedAt)`.
  - `refresh()` method calls `listRefresh()` and overwrites the signal.
  - Disable button while a refresh is in-flight.
- `frontend/src/app/pages/peak-detail/peak-detail.{ts,html,scss}`
  - "Refresh" button in the header.
  - "Updated <relative time>" label from `detail.updatedAt`.
  - `refresh()` method calls `detailRefresh(slug)` and overwrites the signal.
  - Disable while in-flight.

### Frontend (existing — edit tests)
- `frontend/src/app/components/route-grid/route-grid.spec.ts`
  - New spec: clicking Refresh issues `GET /api/routes/refresh` (not `/api/routes`).
- `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`
  - New spec: clicking Refresh issues `GET /api/routes/{slug}/refresh`.

### Not touched
- `frontend/functions/api/[[path]].ts` — the proxy is already a passthrough; Cloudflare's default cache behavior will respect the backend's Cache-Control header on the response. No edge-side configuration needed for this slice.

## API contract

### `GET /api/routes` and `GET /api/routes/{slug}` (unchanged payloads)
**New response header:**
```
Cache-Control: public, max-age=900, stale-while-revalidate=3600
```

### `GET /api/routes/refresh` and `GET /api/routes/{slug}/refresh` (new)
Identical JSON payloads to the cached endpoints. **Response header:**
```
Cache-Control: no-store
```

Routes are defined as literal segments so they don't collide with `{slug}` matching. ASP.NET routes literal segments before parameter segments, so `refresh` will never be interpreted as a slug.

## Edge cases & error handling
- **In-flight refresh:** button is disabled while the request is pending so a user can't queue 5 refreshes.
- **Error during refresh:** keep the existing cached payload on screen, surface a small error toast/inline message ("Refresh failed — showing cached data").
- **Stale chip:** existing `isStale` flag continues to work and surfaces when backend served from its stale cache.
- **Refresh slug collision:** none — `refresh` is a literal route segment; can't conflict with mountain slugs.
- **CORS:** unchanged; Cache-Control headers don't require any CORS adjustment.

## Test plan

### Frontend (Vitest)
- `route-grid.spec.ts`:
  - Initial load hits `/api/routes`.
  - Clicking Refresh button issues `GET /api/routes/refresh`.
  - Refresh response replaces the routes signal.
- `peak-detail.spec.ts`:
  - Initial load hits `/api/routes/longs-peak`.
  - Clicking Refresh issues `GET /api/routes/longs-peak/refresh`.
  - Refresh response replaces the detail signal.

### Backend
- No new tests. Verification by curl in Phase 6.

## Verification commands

### Backend
```powershell
# Build
dotnet build C:\Users\pgowd\Documents\Big_Route_Weather\backend\RouteWeather.Core\RouteWeather.Core.csproj

# Existing tests still pass
dotnet test C:\Users\pgowd\Documents\Big_Route_Weather\backend\RouteWeather.Core.Tests\RouteWeather.Core.Tests.csproj

# Header verification (run after starting backend manually)
curl.exe -i http://localhost:5150/api/routes      | Select-String -Pattern "Cache-Control"
curl.exe -i http://localhost:5150/api/routes/refresh | Select-String -Pattern "Cache-Control"
# Expected: cached endpoint -> "public, max-age=900, stale-while-revalidate=3600"
#           refresh endpoint -> "no-store"
```

### Frontend
```bash
cd frontend
npm test
npm run build
```

## Implementation order
1. Backend: add Cache-Control to existing endpoints (small change).
2. Backend: add `/refresh` sibling endpoints (delegate to existing handlers).
3. Frontend service: add refresh methods.
4. Frontend route-grid: refresh button + last-updated UI.
5. Frontend peak-detail: refresh button + last-updated UI.
6. Tests + build verification.
7. Manual curl verification of headers.

## Complexity assessment
**Solo build** — under 10 small file changes, no new infrastructure, no DB migration, no new packages.
