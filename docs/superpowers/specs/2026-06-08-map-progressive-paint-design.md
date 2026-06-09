# Map Progressive Paint — Design

**Date:** 2026-06-08
**Scope:** `frontend/src/app/pages/map-home/`
**Goal:** Eliminate the "blank dark box" gap on first visit by decoupling the map's paint paths so basemap tiles and range polygons render as soon as Leaflet is ready, with grade markers fading in independently when `/api/routes` resolves. Add a high-contrast error overlay so failures are unmissable.

## Problem

On a cold visit to `/`, the user stares at an empty dark `.map-container` div for several seconds. Inside `MapHome`'s constructor, `forkJoin([routes.list(), ranges.list()])` waits on both API calls before anything paints, and the Leaflet basemap tile layer is attached inside `initMap` — which runs `afterNextRender` and dynamically imports Leaflet itself. Even when tiles *do* paint before data on fast networks, there is no progress indication, so an idle map reads as a broken map. Errors today are a single small `<p class="status error">` line below the map — easy to miss entirely.

## Non-goals

- **No client-side caching.** The backend already has edge (15m + 1h SWR) + `IMemoryCache` + per-source SQLite tiers. We trust those.
- **No Leaflet preload / `modulepreload` / static import.** Leaflet stays dynamically imported inside `initMap`. We're not bloating PeakDetail, /all, About, etc.
- **No skeleton/spinner over the basemap container.** Progressive paint replaces the blank gap on its own.
- **No backend changes.** No payload split, no new endpoint, no edge-cache tuning.
- **No PeakDetail changes.** This spec is scoped to `MapHome` only.

## Design

### Paint sequence

Three independent paint events, each gated only on its own prerequisites:

```
T0  page hits, MapHome constructs
     │
     ├── fire /api/ranges    (small, edge-cached, polygons)
     ├── fire /api/routes    (heavy, per-route conditions aggregation)
     └── afterNextRender → initMap()
                            ├── loadLeaflet()
                            ├── L.map(...) + tile layer       ← PAINT 1: basemap
                            └── flush whatever data arrived first
     │
     ├── /api/ranges resolves → renderLayers()                 ← PAINT 2: polygons
     └── /api/routes resolves → renderMarkers() (fade-in)      ← PAINT 3: grade dots
```

Each `render*` method guards on `this.map != null` AND its own data array. They are idempotent — they clear `this.layers` (or the markers) and rebuild. Calling them from both the data-arrival callback and the end of `initMap` is safe: whichever runs first paints, the other reconciles.

### State

Today:
```ts
loading      = signal(true);
error        = signal<string | null>(null);
```

After:
```ts
loading      = signal(true);  // flips false when /api/routes resolves (the main payload)
error        = signal<{ kind: 'routes' | 'leaflet'; message: string } | null>(null);
```

`lastFetchedAt` is still set when `/api/routes` resolves — the "Updated Xm ago" chip reflects grade freshness, not range polygon freshness.

### Code shape (target)

```ts
constructor() {
  this.rangesSvc.list().subscribe({
    next: ranges => {
      this.ranges.set(ranges);
      this.renderLayers();
    },
    error: e => {
      console.warn('ranges load failed', e);  // silent: ranges are decorative
    },
  });

  this.routesSvc.list().subscribe({
    next: routes => {
      this.routes.set(routes);
      this.lastFetchedAt.set(Date.now());
      this.loading.set(false);
      this.renderMarkers();
    },
    error: e => {
      this.loading.set(false);
      this.error.set({ kind: 'routes', message: e?.message ?? 'Could not load conditions' });
    },
  });

  afterNextRender(() => this.initMap());
}

private async initMap() {
  const el = this.mapContainer()?.nativeElement;
  if (!el) return;
  try {
    const L = await loadLeaflet();
    // ... existing map + tile + zoom + click-handler setup ...
    this.renderLayers();   // flush ranges if already arrived
    this.renderMarkers();  // flush routes if already arrived
  } catch (e: any) {
    this.error.set({ kind: 'leaflet', message: 'Map failed to load' });
  }
}

retryRoutes() {
  this.error.set(null);
  this.loading.set(true);
  this.routesSvc.list().subscribe({ /* same handlers */ });
}

reload() {
  window.location.reload();
}
```

### Marker fade-in (SCSS)

In `map-home.scss`:

```scss
.peak-marker {
  animation: peak-fade-in 220ms ease-out;
}
@keyframes peak-fade-in {
  from { opacity: 0; transform: scale(0.6); }
  to   { opacity: 1; transform: scale(1); }
}
```

Note: markercluster recreates `.peak-marker` DOM elements when a cluster expands on zoom-in, so the fade will re-fire on user-initiated cluster expansion. That's acceptable — the animation is brief (220ms), fires only on explicit user action, and matches the "appearing" affordance. Cluster glyphs themselves use `.marker-cluster` and are unaffected.

### Error overlay

Replace the inline `<p class="status error">` with a centered overlay card on top of the map container.

**Template (`map-home.html`):**

```html
@if (error(); as err) {
  <div class="map-error-overlay">
    <div class="map-error-card">
      <h2>{{ err.kind === 'leaflet' ? 'Map failed to load' : "Couldn't load conditions" }}</h2>
      <p>{{ err.message }}</p>
      @if (err.kind === 'routes') {
        <button type="button" (click)="retryRoutes()">Retry</button>
      } @else {
        <button type="button" (click)="reload()">Reload page</button>
      }
    </div>
  </div>
}
```

**SCSS:**

```scss
.map-error-overlay {
  position: absolute;
  inset: 0;
  z-index: 1100;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(10, 21, 37, 0.78);
  border-radius: 8px;
}
.map-error-card {
  background: #1a2942;
  border: 1px solid #c44;
  border-radius: 8px;
  padding: 20px 28px;
  max-width: 360px;
  text-align: center;
  color: #fff;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);

  h2 { font-size: 1.1rem; margin: 0 0 8px; color: #ff8a8a; }
  p  { color: #cfd9e8; margin: 0 0 16px; font-size: 0.9rem; }
  button {
    background: #c44;
    color: #fff;
    border: none;
    padding: 8px 20px;
    border-radius: 4px;
    cursor: pointer;
    font-weight: 600;

    &:hover { background: #d55; }
  }
}
```

For the routes-failure case, the basemap + polygons remain visible *under* the dimmed overlay — the user still sees the geography. For Leaflet failure, there's no map underneath; the overlay covers the dark container.

### Data flow & race orderings

| Order of arrival | What user sees | Why it works |
|---|---|---|
| Leaflet → ranges → routes | basemap → polygons → dots fade in | Each handler paints its slice |
| Leaflet → routes → ranges | basemap → dots fade in → polygons | Same, routes wins the API race |
| ranges → routes → Leaflet (slow JS, fast API) | blank, then everything at once | `initMap`'s tail flushes both `render*` calls |
| ranges → Leaflet → routes | basemap+polygons together → dots fade in | `initMap` flushes ranges; routes paints when it arrives |
| routes → Leaflet → ranges | basemap+dots together → polygons | `initMap` flushes routes; ranges paints when it arrives |

Z-order is unaffected: polygons live in Leaflet's `overlayPane`, markers in `markerPane`, regardless of insertion order.

### Error matrix

| Failure | UI response |
|---|---|
| `/api/ranges` fails | Silent. `console.warn`. Map + markers fully functional. |
| `/api/routes` fails | High-contrast overlay card with **Retry** button. Basemap + polygons visible underneath, dimmed. |
| `loadLeaflet()` throws (network blip on dynamic import) | High-contrast overlay card with **Reload page** button. No map underneath. |
| Both `/api/routes` and `loadLeaflet` fail | Whichever sets `error()` first wins. Leaflet failure takes priority if both lose — surface the more fundamental break. (Acceptable: ordering is undefined but both cases yield an actionable overlay.) |
| Route has `summitLat == null` | Already filtered in `renderMarkers`. No change. |

## Testing

Vitest + jsdom. Update `map-home.spec.ts`:

- **Existing**: combined `forkJoin` flush is replaced. Mock `/api/ranges` and `/api/routes` separately via `HttpTestingController`.
- **Ranges-only success**: `/api/ranges` flushes with data, `/api/routes` stays pending → assert `ranges()` populated, `loading()` still true, no error overlay in DOM.
- **Routes-only success**: `/api/routes` flushes, `/api/ranges` stays pending → assert `routes()` populated, `loading()` false, no error overlay.
- **Ranges fails silently**: `/api/ranges` errors, `/api/routes` succeeds → assert no `.map-error-overlay` in DOM.
- **Routes fails with overlay**: `/api/routes` errors → assert `.map-error-overlay` present, h2 reads `Couldn't load conditions`, Retry button present.
- **Retry button refires `/api/routes`**: click Retry → second `expectOne(/\/api\/routes/)` flush → assert overlay cleared.
- **Leaflet failure overlay variant**: not asserted in unit tests — Leaflet itself does not instantiate cleanly under jsdom. Cover via manual verification on the dev preview.
- `httpMock.verify()` in `afterEach` per existing rules.

No new dependencies, no bundle-size delta beyond a few lines of SCSS.

## Files changed

- `frontend/src/app/pages/map-home/map-home.ts` — split observables (drop `forkJoin`), new error state shape, `retryRoutes()`, `reload()`, try/catch around `loadLeaflet`, drop unused `forkJoin` import.
- `frontend/src/app/pages/map-home/map-home.html` — new error overlay markup, remove old inline `<p class="status error">`.
- `frontend/src/app/pages/map-home/map-home.scss` — `.peak-marker` fade-in, `.map-error-overlay` + `.map-error-card` styles.
- `frontend/src/app/pages/map-home/map-home.spec.ts` — separate request mocks, new failure-mode assertions.

## Out of scope (for follow-ups, not this spec)

- Server-side payload trimming for `/api/routes`.
- A lightweight "positions-only" endpoint to drop unscored markers in even earlier.
- Service worker / offline cache.
- Leaflet bundle reduction.
- Error-overlay treatment on PeakDetail or other pages.
