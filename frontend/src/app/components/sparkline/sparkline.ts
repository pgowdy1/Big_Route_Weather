import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterRenderEffect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import {
  Chart,
  ChartConfiguration,
  Filler,
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  CategoryScale,
} from 'chart.js';

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler);

export interface SparklinePoint {
  label: string;
  value: number;
}

@Component({
  selector: 'app-sparkline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<canvas #canvas></canvas>',
  styles: [':host { display: block; width: 100%; height: 60px; }'],
})
export class Sparkline {
  points = input.required<SparklinePoint[]>();
  ariaLabel = input<string>('Trend');

  private canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart: Chart | null = null;

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterRenderEffect(() => {
      const data = this.points();
      const el = this.canvas().nativeElement;
      this.render(el, data);
    });
    destroyRef.onDestroy(() => this.chart?.destroy());
  }

  private render(el: HTMLCanvasElement, data: SparklinePoint[]) {
    const config: ChartConfiguration<'line'> = {
      type: 'line',
      data: {
        labels: data.map(p => p.label),
        datasets: [{
          data: data.map(p => p.value),
          borderColor: '#5cc8ff',
          backgroundColor: 'rgba(92, 200, 255, 0.18)',
          borderWidth: 2,
          tension: 0.25,
          pointRadius: 0,
          fill: true,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: { legend: { display: false }, tooltip: { enabled: false } },
        scales: {
          x: { display: false },
          y: { display: false, beginAtZero: true },
        },
        elements: { line: { borderJoinStyle: 'round' } },
      },
    };

    if (this.chart) {
      this.chart.data.labels = config.data.labels;
      this.chart.data.datasets[0].data = config.data.datasets[0].data;
      this.chart.update('none');
      return;
    }

    this.chart = new Chart(el, config);
  }
}
