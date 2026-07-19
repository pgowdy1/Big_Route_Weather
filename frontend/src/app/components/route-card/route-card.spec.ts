import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouteCard } from './route-card';
import { RouteSummary } from '../../models/route-conditions';
import { SettingsService } from '../../services/settings';

describe('RouteCard', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [RouteCard],
      providers: [provideRouter([])],
    });
  });

  it('renders the card as a routerLink to /peak/<slug>', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', summary('longs-peak'));
    fixture.detectChanges();

    const anchor = (fixture.nativeElement as HTMLElement).querySelector('a.card') as HTMLAnchorElement;
    expect(anchor).toBeTruthy();
    expect(anchor.getAttribute('href')).toBe('/peak/longs-peak');
  });

  it('shows a stale-data chip when isStale is true', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), isStale: true });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text.toLowerCase()).toContain('stale');
  });

  it('shows the range name as a chip', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), rangeName: 'Cascade Range' });
    fixture.detectChanges();

    const chip = (fixture.nativeElement as HTMLElement).querySelector('.range-chip');
    expect(chip?.textContent?.trim()).toBe('Cascade Range');
  });

  it('shows a smoky-air chip when AQI is 151 or above', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), airQualityUsAqi: 168 });
    fixture.detectChanges();

    const chip = (fixture.nativeElement as HTMLElement).querySelector('.aqi-chip');
    expect(chip).toBeTruthy();
    expect(chip!.textContent).toContain('168');
  });

  it('stays silent at AQI 150 and below, and when AQI is null', () => {
    for (const aqi of [150, 42, null]) {
      const fixture = TestBed.createComponent(RouteCard);
      fixture.componentRef.setInput('route', { ...summary('foo'), airQualityUsAqi: aqi });
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('.aqi-chip')).toBeNull();
    }
  });

  it('shows a muted glacier chip when the peak is glaciated', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('mount-baker'), isGlaciated: true });
    fixture.detectChanges();

    const chip = (fixture.nativeElement as HTMLElement).querySelector('.glacier-chip');
    expect(chip).toBeTruthy();
    expect(chip!.textContent ?? '').toContain('Glaciated');
  });

  it('stays silent when the peak is not glaciated', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('pikes-peak'), isGlaciated: false });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.glacier-chip')).toBeNull();
  });

  it('renders elevation in meters when the elevation setting is metric', () => {
    TestBed.inject(SettingsService).set('elevation', 'm');
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', summary('foo'));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('.route-name')?.textContent ?? '';
    expect(text).toContain('4,267 m'); // 14,000 ft × 0.3048 → 4,267.2 → '1.0-0'
    expect(text).not.toContain(`'`);
  });

  it(`renders elevation with the feet tick by default`, () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', summary('foo'));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.route-name')?.textContent).toContain(`14,000'`);
  });

  const nextWindow = (over: Partial<import('../../models/route-conditions').NextWindow> = {}) => ({
    startUtc: new Date(Date.now() + 30 * 3600_000).toISOString(),
    endUtc: new Date(Date.now() + 39 * 3600_000).toISOString(),
    grade: 'A' as const,
    lowConfidence: false,
    ...over,
  });

  it('shows the next-window line when a window exists', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), nextWindow: nextWindow() });
    fixture.detectChanges();

    const line = (fixture.nativeElement as HTMLElement).querySelector('.next-window');
    expect(line).toBeTruthy();
    expect(line!.textContent).toContain('A');
  });

  it('hides the line when nextWindow is null', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', summary('foo'));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.next-window')).toBeNull();
  });

  it('labels an underway window as starting now', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', {
      ...summary('foo'),
      nextWindow: nextWindow({ startUtc: new Date(Date.now() - 3600_000).toISOString() }),
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.next-window')!.textContent).toContain('Now');
  });

  it('marks low-confidence windows with a muted suffix', () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), nextWindow: nextWindow({ lowConfidence: true }) });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.next-window .win-conf')).toBeTruthy();
  });

  it('renders 24h clock times when the time format setting is 24h', () => {
    TestBed.inject(SettingsService).set('timeFormat', '24h');
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', { ...summary('foo'), nextWindow: nextWindow() });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('.next-window')!.textContent ?? '';
    expect(text).not.toMatch(/AM|PM/i);
  });

  it('shows the end day when the window crosses midnight', () => {
    const start = new Date(Date.now() + 30 * 3600_000);
    const end = new Date(start.getTime() + 26 * 3600_000); // >24h ⇒ different local day
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', {
      ...summary('foo'),
      nextWindow: nextWindow({ startUtc: start.toISOString(), endUtc: end.toISOString() }),
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('.next-window strong')!.textContent ?? '';
    const days = text.match(/\b(Sun|Mon|Tue|Wed|Thu|Fri|Sat)\b/g) ?? [];
    expect(days.length).toBe(2);
  });
});

function summary(slug: string): RouteSummary {
  return {
    slug,
    mountain: 'Test Mountain',
    routeName: 'Standard',
    summitElevationFt: 14000,
    classDifficulty: '3',
    isGlaciated: false,
    rangeSlug: 'colorado-14ers',
    rangeName: 'Colorado 14ers',
    summitLat: 39.0,
    summitLon: -106.0,
    grade: 'B',
    overallScore: 85,
    drivers: [],
    updatedAt: new Date().toISOString(),
    isStale: false,
    consensus: null,
    airQualityUsAqi: null,
    nextWindow: null,
  };
}
