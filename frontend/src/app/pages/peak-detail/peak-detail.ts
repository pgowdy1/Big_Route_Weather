import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { GradeBadge } from '../../components/grade-badge/grade-badge';
import { Sparkline, SparklinePoint } from '../../components/sparkline/sparkline';
import { RoutesService } from '../../services/routes-service';
import { RouteDetail, WindowGrade } from '../../models/route-conditions';

interface WindowView {
  key: 'next12h' | 'next24h' | 'next48h';
  label: string;
  target: number;
  data: WindowGrade;
}

@Component({
  selector: 'app-peak-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, GradeBadge, Sparkline, RouterLink],
  templateUrl: './peak-detail.html',
  styleUrl: './peak-detail.scss',
})
export class PeakDetail {
  private service = inject(RoutesService);

  slug = input.required<string>();

  detail = signal<RouteDetail | null>(null);
  loading = signal(true);
  notFound = signal(false);
  error = signal<string | null>(null);

  windows = computed<WindowView[]>(() => {
    const w = this.detail()?.windowGrades;
    if (!w) return [];
    return [
      { key: 'next12h', label: 'Next 12h', target: 12, data: w.next12h },
      { key: 'next24h', label: 'Next 24h', target: 24, data: w.next24h },
      { key: 'next48h', label: 'Next 48h', target: 48, data: w.next48h },
    ];
  });

  sparklinePoints = computed<SparklinePoint[]>(() => {
    const series = this.detail()?.snowpack?.dailyDepthIn ?? [];
    return series.map(p => ({ label: p.date, value: p.depthIn }));
  });

  constructor() {
    effect(() => {
      const slug = this.slug();
      untracked(() => this.load(slug));
    });
  }

  private load(slug: string) {
    this.loading.set(true);
    this.notFound.set(false);
    this.error.set(null);
    this.service.detail(slug).subscribe({
      next: d => {
        this.detail.set(d);
        this.loading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        if (e.status === 404) this.notFound.set(true);
        else this.error.set(e.message ?? 'Failed to load detail');
        this.loading.set(false);
      },
    });
  }
}
