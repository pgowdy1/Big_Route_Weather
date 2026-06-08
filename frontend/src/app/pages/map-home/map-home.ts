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
  }
}
