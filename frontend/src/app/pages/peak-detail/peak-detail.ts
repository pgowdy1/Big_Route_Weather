import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { GradeBadge } from '../../components/grade-badge/grade-badge';
import { ConsensusBadge } from '../../components/consensus-badge/consensus-badge';
import { Sparkline, SparklinePoint } from '../../components/sparkline/sparkline';
import { RoutesService } from '../../services/routes-service';
import { FactorScore, RouteDetail, WindowGrade } from '../../models/route-conditions';

interface WindowView {
  key: 'next12h' | 'next24h' | 'next48h';
  label: string;
  target: number;
  data: WindowGrade;
}

@Component({
  selector: 'app-peak-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, GradeBadge, ConsensusBadge, Sparkline, RouterLink],
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
  lastFetchedAt = signal<number | null>(null);
  showAll48h = signal(false);

  displayedForecast = computed(() => {
    const all = this.detail()?.forecastNext48h ?? [];
    return this.showAll48h() ? all : all.slice(0, 24);
  });

  skyNow = computed(() => {
    const first = this.detail()?.forecastNext48h?.[0];
    return first
      ? { cloudCoverPct: first.cloudCoverPct, visibilityMiles: first.visibilityMiles, apparentTempF: first.apparentTempF }
      : null;
  });

  aqiCategory(aqi: number): string {
    if (aqi <= 50) return 'Good';
    if (aqi <= 100) return 'Moderate';
    if (aqi <= 150) return 'Unhealthy for sensitive groups';
    if (aqi <= 200) return 'Unhealthy';
    if (aqi <= 300) return 'Very unhealthy';
    return 'Hazardous';
  }

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

  activeFactors = computed<FactorScore[]>(() =>
    (this.detail()?.factors ?? []).filter(f => f.isActive),
  );

  inactiveFactors = computed<FactorScore[]>(() =>
    (this.detail()?.factors ?? []).filter(f => !f.isActive),
  );

  activeWeightPct = computed<number>(() =>
    Math.round(this.activeFactors().reduce((s, f) => s + f.weight, 0) * 100),
  );

  lastUpdatedLabel = computed<string | null>(() => {
    const t = this.lastFetchedAt();
    return t === null ? null : relativeFromNow(t);
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
    this.showAll48h.set(false);
    this.service.detail(slug).subscribe({
      next: d => {
        this.detail.set(d);
        this.lastFetchedAt.set(Date.now());
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

function relativeFromNow(ts: number): string {
  const diffMin = Math.max(0, Math.round((Date.now() - ts) / 60000));
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const hrs = Math.round(diffMin / 60);
  return `${hrs}h ago`;
}
