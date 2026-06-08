import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

interface DailyCallDto {
  dateUtc: string;
  source: string;
  count: number;
  isToday: boolean;
}

interface DailyCallsResponse {
  days: number;
  asOfUtc: string;
  entries: DailyCallDto[];
}

@Component({
  selector: 'app-diagnostics',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './diagnostics.html',
  styleUrl: './diagnostics.scss',
})
export class Diagnostics implements OnInit {
  private http = inject(HttpClient);

  entries = signal<DailyCallDto[]>([]);
  asOfUtc = signal<string | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  sources = computed(() => {
    const set = new Set<string>();
    for (const e of this.entries()) set.add(e.source);
    return Array.from(set).sort();
  });

  rows = computed(() => {
    const bySource = new Map<string, Map<string, DailyCallDto>>();
    for (const e of this.entries()) {
      let row = bySource.get(e.dateUtc);
      if (!row) {
        row = new Map<string, DailyCallDto>();
        bySource.set(e.dateUtc, row);
      }
      row.set(e.source, e);
    }
    return Array.from(bySource.entries())
      .sort(([a], [b]) => (a < b ? 1 : a > b ? -1 : 0))
      .map(([dateUtc, perSource]) => ({
        dateUtc,
        isToday: Array.from(perSource.values()).some(v => v.isToday),
        counts: this.sources().map(src => perSource.get(src)?.count ?? 0),
        total: Array.from(perSource.values()).reduce((sum, v) => sum + v.count, 0),
      }));
  });

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set(null);
    this.http.get<DailyCallsResponse>('/api/diagnostics/daily-calls?days=7').subscribe({
      next: r => {
        this.entries.set(r.entries);
        this.asOfUtc.set(r.asOfUtc);
        this.loading.set(false);
      },
      error: e => {
        this.error.set(e?.message ?? 'Could not load diagnostics');
        this.loading.set(false);
      },
    });
  }
}
