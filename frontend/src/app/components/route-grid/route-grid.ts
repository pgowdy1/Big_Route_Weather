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

  filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.routes();
    return this.routes().filter(r => r.mountain.toLowerCase().includes(q));
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
