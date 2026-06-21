import { ChangeDetectionStrategy, Component, PLATFORM_ID, computed, inject, OnInit, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { RoutesService } from '../../services/routes-service';
import { RouteSummary } from '../../models/route-conditions';
import { RouteCard } from '../route-card/route-card';
import { SeoService } from '../../seo/seo.service';
import { allPeaksMeta } from '../../seo/route-meta';

interface RangeGroup {
  slug: string;
  name: string;
  routes: RouteSummary[];
}

@Component({
  selector: 'app-route-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouteCard],
  templateUrl: './route-grid.html',
  styleUrl: './route-grid.scss',
})
export class RouteGrid implements OnInit {
  private service = inject(RoutesService);
  private seo = inject(SeoService);
  private platformId = inject(PLATFORM_ID);

  routes = signal<RouteSummary[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);
  query = signal('');
  lastFetchedAt = signal<number | null>(null);

  groups = computed<RangeGroup[]>(() => {
    const q = this.query().trim().toLowerCase();
    const filtered = q
      ? this.routes().filter(r => r.mountain.toLowerCase().includes(q))
      : this.routes();

    const bySlug = new Map<string, RangeGroup>();
    for (const r of filtered) {
      const key = r.rangeSlug || '';
      let g = bySlug.get(key);
      if (!g) {
        g = { slug: key, name: r.rangeName || 'Other', routes: [] };
        bySlug.set(key, g);
      }
      g.routes.push(r);
    }
    return Array.from(bySlug.values());
  });

  lastUpdatedLabel = computed(() => {
    const t = this.lastFetchedAt();
    return t === null ? null : relativeFromNow(t);
  });

  ngOnInit() {
    this.seo.setMeta(allPeaksMeta());
    if (isPlatformBrowser(this.platformId)) {
      this.load();
    }
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.service.list().subscribe({
      next: r => {
        this.routes.set(r);
        this.lastFetchedAt.set(Date.now());
        this.loading.set(false);
      },
      error: e => {
        this.error.set(e?.message ?? 'Could not reach the backend');
        this.loading.set(false);
      },
    });
  }

  onSearch(value: string) {
    this.query.set(value);
  }
}

function relativeFromNow(ts: number): string {
  const diffMin = Math.max(0, Math.round((Date.now() - ts) / 60000));
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const hrs = Math.round(diffMin / 60);
  return `${hrs}h ago`;
}
