import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { PeakDetail } from './peak-detail';
import { RouteDetail } from '../../models/route-conditions';

describe('PeakDetail', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PeakDetail],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads detail for the slug and shows three window grades', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();

    const badges = (fixture.nativeElement as HTMLElement).querySelectorAll('.window app-grade-badge');
    expect(badges.length).toBe(3);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Next 12h');
    expect(text).toContain('Next 24h');
    expect(text).toContain('Next 48h');
  });

  it('renders all forecast rows, not just 12', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.forecast tbody tr');
    expect(rows.length).toBe(48);
  });

  it('shows "Peak not found" on 404', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'missing');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/missing').flush('Not found', { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text.toLowerCase()).toContain('not found');
  });

  it('badges partial windows when hoursCovered < target', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.windowGrades!.next48h.hoursCovered = 30;
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('partial');
    expect(text).toContain('30h');
  });

  it('renders inactive factors with dimmed card and "Not a factor today" label', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.factors = [
      { name: 'Wind', score: 95, weight: 0.25, detail: '5 mph', isActive: true },
      { name: 'Recent snow', score: 100, weight: 0.20, detail: '0.0" new snow in last 7 days', isActive: false },
      { name: 'Snowpack', score: 100, weight: 0.20, detail: 'SWE 0.0" (100% of normal)', isActive: false },
    ];
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const active = el.querySelectorAll('.factors .factor-card:not(.inactive)');
    const inactive = el.querySelectorAll('.factors .factor-card.inactive');

    expect(active.length).toBe(1);
    expect(inactive.length).toBe(2);
    expect(el.textContent ?? '').toContain('Not a factor today');
  });

  it('shows weights note summing active weights when factors are inactive', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.factors = [
      { name: 'Wind', score: 67, weight: 0.25, detail: '20 mph', isActive: true },
      { name: 'Temperature', score: 100, weight: 0.15, detail: '32°F', isActive: true },
      { name: 'Precipitation', score: 59, weight: 0.20, detail: '33% chance', isActive: true },
      { name: 'Recent snow', score: 100, weight: 0.20, detail: '0.0"', isActive: true },
      { name: 'Snowpack', score: 100, weight: 0.20, detail: 'SWE 0.1"', isActive: false },
    ];
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const noteText = (fixture.nativeElement as HTMLElement).querySelector('.factors-note')?.textContent ?? '';
    expect(noteText).toContain('80%');
    expect(noteText).toContain('snow factors excluded today');
  });

  it('shows the range name as a chip', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.rangeSlug = 'cascade-range';
    data.rangeName = 'Cascade Range';
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const chip = (fixture.nativeElement as HTMLElement).querySelector('.range-chip');
    expect(chip?.textContent?.trim()).toBe('Cascade Range');
  });

  it('hides the weights note when all factors are active', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.factors = [
      { name: 'Wind', score: 95, weight: 0.25, detail: '5 mph', isActive: true },
      { name: 'Recent snow', score: 100, weight: 0.20, detail: '0.0"', isActive: true },
    ];
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const note = (fixture.nativeElement as HTMLElement).querySelector('.factors-note');
    expect(note).toBeNull();
  });
});

function detail(): RouteDetail {
  const hourly = Array.from({ length: 48 }, (_, i) => ({
    time: new Date(Date.UTC(2026, 5, 1, i)).toISOString(),
    tempF: 40,
    windMph: 8,
    precipitationProbabilityPct: 10,
    shortForecast: 'Sunny',
  }));
  return {
    slug: 'longs-peak',
    mountain: 'Longs Peak',
    routeName: 'Keyhole',
    summitElevationFt: 14259,
    classDifficulty: '3',
    rangeSlug: 'colorado-14ers',
    rangeName: 'Colorado 14ers',
    grade: 'B',
    overallScore: 84,
    drivers: [],
    updatedAt: new Date().toISOString(),
    isStale: false,
    summitLat: 40.255,
    summitLon: -105.616,
    factors: [],
    rationale: 'Solid day.',
    forecastNext48h: hourly,
    snowpack: {
      snowWaterEquivalentIn: 1.2,
      snowDepthIn: 4.0,
      newSnowLast7DaysIn: 0,
      percentOfNormalSwe: 100,
      stationTriplet: 'TEST:CO:SNTL',
      dailyDepthIn: [],
    },
    windowGrades: {
      next12h: { grade: 'A', overallScore: 95, hoursCovered: 12, factors: [], drivers: [], rationale: '12h is great.' },
      next24h: { grade: 'B', overallScore: 82, hoursCovered: 24, factors: [], drivers: [], rationale: '24h is solid.' },
      next48h: { grade: 'C', overallScore: 70, hoursCovered: 48, factors: [], drivers: [], rationale: '48h has issues.' },
    },
    sources: {
      nws: { fetchedAt: new Date().toISOString() },
      snotel: { fetchedAt: new Date().toISOString() },
    },
    consensus: null,
    perSourceForecast: null,
  };
}
