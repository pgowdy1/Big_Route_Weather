import { TestBed } from '@angular/core/testing';
import { WeekStrip } from './week-strip';
import { ClimbWindow, DaylightSpan, HourlyQuality } from '../../models/route-conditions';

describe('WeekStrip', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [WeekStrip] }));

  const T0 = Date.parse('2026-07-20T06:00:00Z');
  const iso = (h: number) => new Date(T0 + h * 3600_000).toISOString();

  const hours = (n: number): HourlyQuality[] =>
    Array.from({ length: n }, (_, i) => ({ timeUtc: iso(i), score: i % 5 === 0 ? 60 : 92, qualifies: i % 5 !== 0 }));

  const daylight: DaylightSpan[] = Array.from({ length: 9 }, (_, d) => ({
    sunriseUtc: new Date(T0 + (d * 24 + 6) * 3600_000).toISOString(),
    sunsetUtc: new Date(T0 + (d * 24 + 21) * 3600_000).toISOString(),
  }));

  const win: ClimbWindow = {
    startUtc: iso(10), endUtc: iso(20), grade: 'A', score: 95,
    endReason: 'ends with daylight', lowConfidence: false,
  };

  function create(h: HourlyQuality[] | null, w: ClimbWindow[] | null = [win], d: DaylightSpan[] | null = daylight) {
    const fixture = TestBed.createComponent(WeekStrip);
    fixture.componentRef.setInput('hours', h);
    fixture.componentRef.setInput('windows', w);
    fixture.componentRef.setInput('daylight', d);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders one cell per hour, grouped into day columns', () => {
    const el = create(hours(168));
    expect(el.querySelectorAll('.cell').length).toBe(168);
    expect(el.querySelectorAll('.day').length).toBeGreaterThanOrEqual(7);
  });

  it('marks hours inside a climb window', () => {
    const el = create(hours(48));
    expect(el.querySelectorAll('.cell.in-window').length).toBe(10);
  });

  it('marks night hours outside the daylight spans', () => {
    const el = create(hours(24));
    expect(el.querySelectorAll('.cell.night').length).toBeGreaterThan(0);
    expect(el.querySelectorAll('.cell:not(.night)').length).toBeGreaterThan(0);
  });

  it('hatches hours beyond the 96h confidence horizon', () => {
    const el = create(hours(168));
    expect(el.querySelectorAll('.cell.low-conf').length).toBe(72);
    expect(create(hours(48)).querySelectorAll('.cell.low-conf').length).toBe(0);
  });

  it('renders nothing without hourly data', () => {
    expect(create(null).querySelector('.strip')).toBeNull();
    expect(create([]).querySelector('.strip')).toBeNull();
  });

  it('exposes the strip as a labelled image for assistive tech', () => {
    const strip = create(hours(48)).querySelector('.strip')!;
    expect(strip.getAttribute('role')).toBe('img');
    expect(strip.getAttribute('aria-label')?.trim()).toBeTruthy();
  });
});
