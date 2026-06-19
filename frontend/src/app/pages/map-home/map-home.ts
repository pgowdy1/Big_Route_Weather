import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnDestroy, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { RoutesService } from '../../services/routes-service';
import { RangesService } from '../../services/ranges-service';
import { RoutePosition, RouteSummary } from '../../models/route-conditions';
import { RangeMeta } from '../../models/range';
import { SeoService } from '../../seo/seo.service';
import { homeMeta } from '../../seo/route-meta';

type MapError = { kind: 'routes' | 'leaflet'; message: string };

@Component({
  selector: 'app-map-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map-home.html',
  styleUrl: './map-home.scss',
})
export class MapHome implements OnDestroy {
  private routesSvc = inject(RoutesService);
  private rangesSvc = inject(RangesService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  private seo = inject(SeoService);

  mapContainer = viewChild<ElementRef<HTMLDivElement>>('mapEl');

  routes = signal<RouteSummary[]>([]);
  positions = signal<RoutePosition[]>([]);
  ranges = signal<RangeMeta[]>([]);
  loading = signal(true);
  error = signal<MapError | null>(null);
  lastFetchedAt = signal<number | null>(null);

  lastUpdatedLabel = computed(() => {
    const t = this.lastFetchedAt();
    if (t === null) return null;
    const diffMin = Math.max(0, Math.round((Date.now() - t) / 60000));
    if (diffMin < 1) return 'just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    return `${Math.round(diffMin / 60)}h ago`;
  });

  private static readonly STALE_REFETCH_DELAY_MS = 60_000;
  private static readonly STALE_REFETCH_MAX_ATTEMPTS = 5;
  private staleRefetchTimer: ReturnType<typeof setTimeout> | null = null;
  private staleRefetchAttempts = 0;

  private map: any | null = null;
  private layers: any[] = [];       // range polygons + labels — owned by renderLayers
  private ghostLayers: any[] = [];  // pre-data position ghosts — owned by renderGhostMarkers
  private markerLayers: any[] = []; // graded markers AND null-grade ghosts — owned by renderMarkers

  searchQuery = signal('');

  searchResults = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (q.length < 2) return [];
    return this.routes()
      .filter(r => r.mountain.toLowerCase().includes(q))
      .slice(0, 8);
  });

  onSearch(value: string) { this.searchQuery.set(value); }

  private markerBySlug = new Map<string, any>();

  focusPeak(slug: string) {
    const m = this.markerBySlug.get(slug);
    if (!m || !this.map) return;
    this.map.setView(m.getLatLng(), Math.max(this.map.getZoom(), 8), { animate: true });
    m.openPopup();
    this.searchQuery.set('');
  }

  constructor() {
    this.seo.setMeta(homeMeta());
    this.fetchRanges();
    this.fetchPositions();
    this.fetchRoutes();
    afterNextRender(() => this.initMap());
  }

  private fetchRanges() {
    this.rangesSvc.list().subscribe({
      next: ranges => {
        this.ranges.set(ranges);
        this.renderLayers();
      },
      error: e => {
        console.warn('ranges load failed', e);
      },
    });
  }

  private fetchPositions() {
    this.routesSvc.positions().subscribe({
      next: positions => {
        this.positions.set(positions);
        this.renderGhostMarkers();
      },
      error: e => {
        console.warn('positions load failed', e);
      },
    });
  }

  private fetchRoutes() {
    this.routesSvc.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: routes => {
        this.routes.set(routes);
        this.lastFetchedAt.set(Date.now());
        this.loading.set(false);
        this.renderMarkers();
        this.scheduleStaleRefetch();
      },
      error: e => {
        this.loading.set(false);
        this.error.set({ kind: 'routes', message: e?.message ?? 'Could not load conditions' });
      },
    });
  }

  retryRoutes() {
    this.staleRefetchAttempts = 0;
    this.error.set(null);
    this.loading.set(true);
    this.fetchRoutes();
  }

  reload() {
    window.location.reload();
  }

  // Backend serves last-known data marked isStale while its warmer catches up
  // (typically <= one 10-min cycle). Quietly poll until fresh — no spinner,
  // no chip; grades are already on screen.
  private scheduleStaleRefetch() {
    if (this.staleRefetchTimer !== null) {
      clearTimeout(this.staleRefetchTimer);
      this.staleRefetchTimer = null;
    }
    if (!this.routes().some(r => r.isStale)) {
      this.staleRefetchAttempts = 0;
      return;
    }
    if (this.staleRefetchAttempts >= MapHome.STALE_REFETCH_MAX_ATTEMPTS) return;
    this.staleRefetchAttempts++;
    this.staleRefetchTimer = setTimeout(() => {
      this.staleRefetchTimer = null;
      this.refetchStaleRoutes();
    }, MapHome.STALE_REFETCH_DELAY_MS);
  }

  private refetchStaleRoutes() {
    this.routesSvc.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: routes => {
        this.routes.set(routes);
        this.lastFetchedAt.set(Date.now());
        this.renderMarkers();
        this.scheduleStaleRefetch();
      },
      error: e => {
        console.warn('stale refetch failed', e);
        this.scheduleStaleRefetch();
      },
    });
  }

  ngOnDestroy() {
    if (this.staleRefetchTimer !== null) { clearTimeout(this.staleRefetchTimer); this.staleRefetchTimer = null; }
    if (this.map) { this.map.remove(); this.map = null; }
  }

  private async initMap() {
    const el = this.mapContainer()?.nativeElement;
    if (!el) return;

    let L: typeof import('leaflet');
    try {
      L = await loadLeaflet();
    } catch (e) {
      console.error('Leaflet failed to load', e);
      this.error.set({ kind: 'leaflet', message: 'Map failed to load. Reload the page to try again.' });
      return;
    }

    const isMobile = window.innerWidth <= 480;

    this.map = L.map(el, {
      center: [43.0, -113.6],
      zoom: isMobile ? 4 : 6,
      minZoom: 4,
      maxZoom: 12,
      maxBounds: [[28, -130], [52, -100]],
      scrollWheelZoom: true,
      zoomControl: false,
    });

    L.control.zoom({ position: 'topright' }).addTo(this.map);

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 12,
    }).addTo(this.map);

    this.map.getContainer().addEventListener('click', (e: MouseEvent) => {
      const target = e.target as HTMLElement;
      const cta = target.closest('a.popup-cta[data-peak]') as HTMLAnchorElement | null;
      if (!cta) return;
      // Let middle-click and modified clicks behave as native anchors.
      if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      e.preventDefault();
      const slug = cta.getAttribute('data-peak')!;
      this.map.closePopup();
      this.router.navigate(['/peak', slug]);
    });

    this.renderLayers();
    this.renderGhostMarkers();
    this.renderMarkers();
  }

  private async renderLayers() {
    if (!this.map || this.ranges().length === 0) return;
    const L = await loadLeaflet();

    for (const layer of this.layers) this.map.removeLayer(layer);
    this.layers = [];

    for (const range of this.ranges()) {
      const poly = L.geoJSON(range.perimeterGeoJson as any, {
        style: {
          color: range.color,
          weight: 1.5,
          dashArray: '4,3',
          fillColor: range.color,
          fillOpacity: 0.22,
          interactive: false,
        } as any,
      });
      poly.addTo(this.map);
      this.layers.push(poly);

      const centroid = polygonCentroid(range.perimeterGeoJson.coordinates[0] as number[][]);
      const label = L.marker([centroid[1], centroid[0]], {
        icon: L.divIcon({
          className: 'range-label',
          html: `<span>${range.name.toUpperCase()}</span>`,
        }),
        interactive: false,
      }).addTo(this.map);
      this.layers.push(label);
    }
  }

  private async renderGhostMarkers() {
    if (!this.map || this.positions().length === 0) return;
    // Real data already arrived — skip; renderMarkers will populate.
    if (this.routes().length > 0) return;
    const L = await loadLeaflet();

    for (const layer of this.ghostLayers) this.map.removeLayer(layer);
    this.ghostLayers = [];

    for (const pos of this.positions()) {
      if (pos.summitLat == null || pos.summitLon == null) continue;
      const icon = L.divIcon({
        className: 'peak-marker peak-marker-ghost',
        html: '<span class="dot grade-ghost"></span>',
        iconSize: [28, 28],
        iconAnchor: [14, 14],
      });
      const marker = L.marker([pos.summitLat, pos.summitLon], { icon, interactive: false });
      marker.addTo(this.map);
      this.ghostLayers.push(marker);
    }
  }

  private async renderMarkers() {
    if (!this.map || this.routes().length === 0) return;
    const L = await loadLeaflet();
    await import('leaflet.markercluster');
    // markercluster augments window.L (which loadLeaflet returns), so L.markerClusterGroup exists here.
    const Lcluster = L as any;

    for (const layer of this.ghostLayers) this.map.removeLayer(layer);
    this.ghostLayers = [];

    // Re-renders (stale-recovery refetch) must replace markers, not stack them.
    for (const layer of this.markerLayers) this.map.removeLayer(layer);
    this.markerLayers = [];

    this.markerBySlug.clear();

    const cluster = Lcluster.markerClusterGroup({
      disableClusteringAtZoom: 8,
      showCoverageOnHover: false,
      spiderfyOnMaxZoom: true,
      maxClusterRadius: 60,
    });
    let usedCluster = false;

    for (const route of this.routes()) {
      if (route.summitLat == null || route.summitLon == null) continue;

      const spec = markerIconSpec(route);
      const icon = L.divIcon({
        className: spec.className,
        html: `<span class="dot ${spec.dotClass}"></span>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14],
      });

      // Ghost routes are intentionally absent from markerBySlug: there is no
      // popup to open, so search-focus skips them until a grade arrives.
      if (!spec.interactive) {
        const ghost = L.marker([route.summitLat, route.summitLon], { icon, interactive: false });
        ghost.addTo(this.map);
        this.markerLayers.push(ghost);
        continue;
      }

      const marker = L.marker([route.summitLat, route.summitLon], { icon, title: route.mountain });
      marker.bindPopup(popupHtml(route), { className: 'peak-popup' });
      this.markerBySlug.set(route.slug, marker);

      // Every range clusters at low zoom and separates at zoom >= 8
      // (disableClusteringAtZoom), so dense ranges (Sierra, Cascades, Colorado)
      // stay readable without per-range special-casing.
      cluster.addLayer(marker);
      usedCluster = true;
    }

    if (usedCluster) {
      cluster.addTo(this.map);
      this.markerLayers.push(cluster);
    }
  }
}

// Leaflet ships UMD-only (no "module"/"exports" in its package.json), so Angular's
// prod esbuild bundle wraps it as { default: L }. The .d.ts declares named exports
// that don't exist at runtime — `L.map` would be undefined. The UMD body also
// assigns window.L, which is the most reliable handle to the real API.
async function loadLeaflet(): Promise<typeof import('leaflet')> {
  const mod = await import('leaflet');
  return (window as any).L ?? (mod as any).default ?? mod;
}

function polygonCentroid(ring: number[][]): [number, number] {
  let twiceArea = 0, cx = 0, cy = 0;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const [x0, y0] = ring[j];
    const [x1, y1] = ring[i];
    const f = x0 * y1 - x1 * y0;
    twiceArea += f;
    cx += (x0 + x1) * f;
    cy += (y0 + y1) * f;
  }
  const area = twiceArea / 2;
  return area === 0 ? ring[0] as [number, number] : [cx / (6 * area), cy / (6 * area)];
}

export interface MarkerIconSpec {
  className: string;
  dotClass: string;
  interactive: boolean;
}

// Null grade = no usable data (<=24h) on the backend; show the same ghost
// treatment as the pre-data positions markers instead of a broken grade dot.
export function markerIconSpec(route: Pick<RouteSummary, 'grade'>): MarkerIconSpec {
  if (route.grade == null) {
    return { className: 'peak-marker peak-marker-ghost', dotClass: 'grade-ghost', interactive: false };
  }
  return { className: 'peak-marker', dotClass: `grade-${route.grade.toLowerCase()}`, interactive: true };
}

function popupHtml(route: RouteSummary): string {
  const grade = route.grade ?? '?';
  const drivers = (route.drivers ?? []).slice(0, 2)
    .map(d => `<div class="popup-driver popup-driver-${d.severity}">${escapeHtml(d.label)}</div>`)
    .join('');
  return `
    <div class="popup-name">${escapeHtml(route.mountain)}</div>
    <div class="popup-sub">${route.summitElevationFt.toLocaleString()} ft &middot; Class ${escapeHtml(route.classDifficulty)}</div>
    <div class="popup-grade grade-${grade.toLowerCase()}">${grade}</div>
    ${drivers}
    <a class="popup-cta" data-peak="${escapeHtml(route.slug)}" href="/peak/${escapeHtml(route.slug)}">View full forecast &rarr;</a>
  `;
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]!));
}
