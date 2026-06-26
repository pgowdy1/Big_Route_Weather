import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouteCard } from './route-card';
import { RouteSummary } from '../../models/route-conditions';

describe('RouteCard', () => {
  beforeEach(() => {
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
  };
}
