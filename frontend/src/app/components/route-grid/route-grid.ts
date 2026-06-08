import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RoutesService } from '../../services/routes-service';
import { RouteSummary } from '../../models/route-conditions';
import { RouteCard } from '../route-card/route-card';

@Component({
  selector: 'app-route-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouteCard],
  templateUrl: './route-grid.html',
  styleUrl: './route-grid.scss',
})
export class RouteGrid implements OnInit {
  private service = inject(RoutesService);

  routes = signal<RouteSummary[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);
  query = signal('');
  lastFetchedAt = signal<number | null>(null);

  filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.routes();
    return this.routes().filter(r => r.mountain.toLowerCase().includes(q));
  });

  lastUpdatedLabel = computed(() => {
    const t = this.lastFetchedAt();
    return t === null ? null : relativeFromNow(t);
  });

  ngOnInit() {
    this.load();
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
