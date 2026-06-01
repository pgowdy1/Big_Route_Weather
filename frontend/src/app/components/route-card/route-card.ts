import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { GradeBadge } from '../grade-badge/grade-badge';
import { RouteSummary } from '../../models/route-conditions';

@Component({
  selector: 'app-route-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, GradeBadge, RouterLink],
  templateUrl: './route-card.html',
  styleUrl: './route-card.scss',
})
export class RouteCard {
  route = input.required<RouteSummary>();

  ageLabel = computed(() => relativeMinutes(this.route().updatedAt));
}

function relativeMinutes(iso: string): string {
  const updated = new Date(iso).getTime();
  const diffMin = Math.max(0, Math.round((Date.now() - updated) / 60000));
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const hrs = Math.round(diffMin / 60);
  return `${hrs}h ago`;
}
