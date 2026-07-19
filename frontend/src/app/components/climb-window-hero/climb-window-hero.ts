import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { GradeBadge } from '../grade-badge/grade-badge';
import { ClimbWindow } from '../../models/route-conditions';
import { SettingsService } from '../../services/settings';

@Component({
  selector: 'app-climb-window-hero',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, GradeBadge],
  templateUrl: './climb-window-hero.html',
  styleUrl: './climb-window-hero.scss',
})
export class ClimbWindowHero {
  readonly u = inject(SettingsService);

  windows = input.required<ClimbWindow[] | null>();

  // Captured once per component instance; the page is short-lived enough that a
  // ticking clock isn't worth the change-detection churn.
  private readonly now = Date.now();

  private upcoming = computed(() =>
    (this.windows() ?? []).filter(w => new Date(w.endUtc).getTime() > this.now));

  best = computed<ClimbWindow | null>(() => {
    const list = this.upcoming();
    if (list.length === 0) return null;
    return [...list].sort((a, b) =>
      b.score - a.score || new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime())[0];
  });

  startsNow(w: ClimbWindow): boolean {
    return new Date(w.startUtc).getTime() <= this.now;
  }

  crossesDay(w: ClimbWindow): boolean {
    return new Date(w.startUtc).toDateString() !== new Date(w.endUtc).toDateString();
  }
}
