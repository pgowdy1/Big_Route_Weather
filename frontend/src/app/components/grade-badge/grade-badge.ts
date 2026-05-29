import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Grade } from '../../models/route-conditions';

@Component({
  selector: 'app-grade-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './grade-badge.html',
  styleUrl: './grade-badge.scss',
})
export class GradeBadge {
  grade = input<Grade | null>(null);

  letter = computed(() => this.grade() ?? '—');
  className = computed(() => `badge badge-${(this.grade() ?? 'unknown').toLowerCase()}`);
}
