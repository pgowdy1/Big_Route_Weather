import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Consensus } from '../../models/route-conditions';

@Component({
  selector: 'app-consensus-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './consensus-badge.html',
  styleUrl: './consensus-badge.scss',
})
export class ConsensusBadge {
  consensus = input<Consensus | null>(null);

  level = computed(() => this.consensus()?.level ?? null);
  sourcesReporting = computed(() => this.consensus()?.sourcesReporting ?? 0);
  sourcesAttempted = computed(() => this.consensus()?.sourcesAttempted ?? 0);
  worstFactor = computed(() => this.consensus()?.worstFactor ?? null);

  // Only render on low consensus — high/medium are silent so the badge functions as a heads-up,
  // not a permanent UI fixture.
  visible = computed(() => this.level() === 'low');

  className = computed(() => {
    const lvl = this.level();
    return lvl ? `consensus consensus-${lvl}` : 'consensus consensus-unknown';
  });

  tooltip = computed(() => {
    const c = this.consensus();
    if (!c) return '';
    const parts = [`${c.sourcesReporting} of ${c.sourcesAttempted} sources reporting`];
    if (c.worstFactor) parts.push(`sources disagree on ${c.worstFactor.toLowerCase()}`);
    return parts.join(' · ');
  });
}
