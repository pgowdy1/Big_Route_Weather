import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { GradeBadge } from '../grade-badge/grade-badge';
import { RoutesService } from '../../services/routes-service';
import { RouteDetail, RouteSummary } from '../../models/route-conditions';

@Component({
  selector: 'app-route-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, GradeBadge],
  templateUrl: './route-card.html',
  styleUrl: './route-card.scss',
})
export class RouteCard {
  private service = inject(RoutesService);

  route = input.required<RouteSummary>();

  expanded = signal(false);
  detail = signal<RouteDetail | null>(null);
  detailError = signal<string | null>(null);

  ageLabel = computed(() => relativeMinutes(this.route().updatedAt));

  toggle() {
    const next = !this.expanded();
    this.expanded.set(next);
    if (next && !this.detail()) {
      this.detailError.set(null);
      this.service.detail(this.route().slug).subscribe({
        next: d => this.detail.set(d),
        error: e => this.detailError.set(e?.message ?? 'Failed to load detail'),
      });
    }
  }
}

function relativeMinutes(iso: string): string {
  const updated = new Date(iso).getTime();
  const diffMin = Math.max(0, Math.round((Date.now() - updated) / 60000));
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const hrs = Math.round(diffMin / 60);
  return `${hrs}h ago`;
}
