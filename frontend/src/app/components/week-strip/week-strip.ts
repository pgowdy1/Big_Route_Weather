import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ClimbWindow, DaylightSpan, HourlyQuality } from '../../models/route-conditions';

interface StripCell { cls: string; }
interface StripDay { label: string; cells: StripCell[]; }

// Hours past this horizon are hatched — model confidence drops sharply beyond
// four days out, matching the aggregator's low-confidence tail.
const CONFIDENCE_HORIZON_HOURS = 96;

@Component({
  selector: 'app-week-strip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './week-strip.html',
  styleUrl: './week-strip.scss',
})
export class WeekStrip {
  hours = input.required<HourlyQuality[] | null>();
  windows = input.required<ClimbWindow[] | null>();
  daylight = input.required<DaylightSpan[] | null>();

  days = computed<StripDay[]>(() => {
    const hours = this.hours() ?? [];
    if (hours.length === 0) return [];

    const spans = (this.daylight() ?? []).map(d => ({ rise: Date.parse(d.sunriseUtc), set: Date.parse(d.sunsetUtc) }));
    const wins = (this.windows() ?? []).map(w => ({ s: Date.parse(w.startUtc), e: Date.parse(w.endUtc) }));

    const days: StripDay[] = [];
    let current: StripDay | null = null;
    hours.forEach((h, i) => {
      const t = new Date(h.timeUtc);
      const ms = t.getTime();
      // Local day label — a new label starts a new column.
      const label = t.toLocaleDateString(undefined, { weekday: 'short' });
      if (!current || current.label !== label) {
        current = { label, cells: [] };
        days.push(current);
      }
      const cls = [
        h.qualifies ? (h.score >= 90 ? 'q-a' : 'q-b') : 'q-no',
        spans.some(d => ms >= d.rise && ms < d.set) ? '' : 'night',
        wins.some(w => ms >= w.s && ms < w.e) ? 'in-window' : '',
        i >= CONFIDENCE_HORIZON_HOURS ? 'low-conf' : '',
      ].filter(Boolean).join(' ');
      current.cells.push({ cls });
    });
    return days;
  });
}
