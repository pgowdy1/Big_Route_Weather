import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { forkJoin } from 'rxjs';
import { Router } from '@angular/router';
import { RoutesService } from '../../services/routes-service';
import { RangesService } from '../../services/ranges-service';
import { RouteSummary } from '../../models/route-conditions';
import { RangeMeta } from '../../models/range';

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

  mapContainer = viewChild<ElementRef<HTMLDivElement>>('mapEl');

  routes = signal<RouteSummary[]>([]);
  ranges = signal<RangeMeta[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  lastFetchedAt = signal<number | null>(null);

  lastUpdatedLabel = computed(() => {
    const t = this.lastFetchedAt();
    if (t === null) return null;
    const diffMin = Math.max(0, Math.round((Date.now() - t) / 60000));
    if (diffMin < 1) return 'just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    return `${Math.round(diffMin / 60)}h ago`;
  });

  private map: any | null = null;
  private layers: any[] = [];

  constructor() {
    forkJoin([this.routesSvc.list(), this.rangesSvc.list()]).subscribe({
      next: ([routes, ranges]) => {
        this.routes.set(routes);
        this.ranges.set(ranges);
        this.lastFetchedAt.set(Date.now());
        this.loading.set(false);
        this.renderLayers();
        this.renderMarkers();
      },
      error: e => {
        this.error.set(e?.message ?? 'Could not load conditions');
        this.loading.set(false);
      },
    });

    afterNextRender(() => this.initMap());
  }

  ngOnDestroy() {
    if (this.map) { this.map.remove(); this.map = null; }
  }

  private async initMap() {
    const el = this.mapContainer()?.nativeElement;
    if (!el) return;

    const L = await import('leaflet');

    this.map = L.map(el, {
      center: [41.5, -113],
      zoom: 5,
      minZoom: 4,
      maxZoom: 12,
      maxBounds: [[28, -130], [52, -100]],
      scrollWheelZoom: true,
    });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 12,
    }).addTo(this.map);

    this.renderLayers();
    this.renderMarkers();
  }

  private async renderLayers() {
    if (!this.map || this.ranges().length === 0) return;
    const L = await import('leaflet');

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

  private async renderMarkers() {
    if (!this.map || this.routes().length === 0) return;
    const L = (await import('leaflet')) as any;
    await import('leaflet.markercluster');

    const cluster = L.markerClusterGroup({
      disableClusteringAtZoom: 8,
      showCoverageOnHover: false,
      spiderfyOnMaxZoom: true,
      maxClusterRadius: 60,
    });
    let usedCluster = false;

    for (const route of this.routes()) {
      if (!route.summitLat || !route.summitLon) continue;

      const grade = (route.grade ?? 'x').toLowerCase();
      const icon = L.divIcon({
        className: 'peak-marker',
        html: `<span class="dot grade-${grade}"></span>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14],
      });

      const marker = L.marker([route.summitLat, route.summitLon], { icon, title: route.mountain });
      marker.bindPopup(popupHtml(route), { className: 'peak-popup' });

      if (route.rangeSlug === 'colorado-14ers') {
        cluster.addLayer(marker);
        usedCluster = true;
      } else {
        marker.addTo(this.map);
        this.layers.push(marker);
      }
    }

    if (usedCluster) {
      cluster.addTo(this.map);
      this.layers.push(cluster);
    }
  }
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
