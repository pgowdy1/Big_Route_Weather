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

  className = computed(() => {
    const lvl = this.level();
    return lvl ? `consensus consensus-${lvl}` : 'consensus consensus-unknown';
  });

  label = computed(() => {
    const lvl = this.level();
    if (lvl === 'high') return 'High consensus';
    if (lvl === 'medium') return 'Medium consensus';
    if (lvl === 'low') return 'Low consensus';
    return 'No source data';
  });
}
