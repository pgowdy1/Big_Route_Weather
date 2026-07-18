import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { PeakDetail } from './peak-detail';
import { RouteDetail } from '../../models/route-conditions';
import { SettingsService } from '../../services/settings';

describe('PeakDetail', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
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

  it('makes the 24h grade the hero and shows the 12h + 48h breakdown in the strip', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    // Hero = the 24h grade, its rationale, and the quality word for grade B ("Good").
    expect(el.querySelector('.hero app-grade-badge')).not.toBeNull();
    expect(el.querySelector('.hero .rationale')?.textContent).toContain('24h is solid.');
    expect(el.textContent ?? '').toContain('Good');
    // Strip = the OTHER two windows (12h + 48h), each with its rationale; the 24h is not repeated there.
    const strip = el.querySelector('.window-strip')!;
    expect(strip.querySelectorAll('app-grade-badge').length).toBe(2);
    expect(strip.textContent).toContain('Next 12h');
    expect(strip.textContent).toContain('Next 48h');
    expect(strip.textContent).not.toContain('Next 24h');
    expect(strip.textContent).toContain('12h is great.');
    expect(strip.textContent).toContain('48h has issues.');
  });

  it('renders the collapsed 24-row forecast by default, not just 12', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.forecast tbody tr');
    expect(rows.length).toBe(24);
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

  it('names every inactive factor in the note, with no weight percentage', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.factors = [
      { name: 'Wind', score: 67, weight: 0.20, detail: '20 mph', isActive: true },
      { name: 'Temperature', score: 100, weight: 0.12, detail: '32°F', isActive: true },
      { name: 'Precipitation', score: 59, weight: 0.18, detail: '33% chance', isActive: true },
      { name: 'Thunderstorm', score: 100, weight: 0.20, detail: 'No meaningful storm energy in window', isActive: false },
      { name: 'Gusts', score: 95, weight: 0.10, detail: 'Gusts close to sustained wind', isActive: false },
      { name: 'Snowpack', score: 100, weight: 0.10, detail: 'SWE 0.1"', isActive: false },
    ];
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const noteText = (fixture.nativeElement as HTMLElement).querySelector('.factors-note')?.textContent ?? '';
    expect(noteText).toContain('Thunderstorm');
    expect(noteText).toContain('Gusts');
    expect(noteText).toContain('Snowpack');
    expect(noteText).not.toContain('%');
  });

  it('shows the range in the facts kicker', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();

    const facts = (fixture.nativeElement as HTMLElement).querySelector('.hero .facts');
    expect(facts?.textContent).toContain('Colorado 14ers');
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

  it('renders the Sky & Air section with AQI category and daylight', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.airQuality = { usAqi: 95, pm25: 30, fetchedAt: '2026-06-10T12:00:00Z' };
    data.daylight = { sunriseUtc: '2026-06-10T12:11:00Z', sunsetUtc: '2026-06-11T04:11:00Z', daylightHours: 16.0 };
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const sky = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="sky-air"]');
    expect(sky).not.toBeNull();
    const text = sky?.textContent ?? '';
    expect(text).toContain('Moderate');
    expect(text).toContain('95');
    expect(text).toContain('16.0');
  });

  it('colors the AQI value by EPA band', async () => {
    const cases: Array<[number, string]> = [
      [42, 'aqi-good'],
      [95, 'aqi-moderate'],
      [168, 'aqi-unhealthy'],
      [250, 'aqi-very-unhealthy'],
    ];
    for (const [usAqi, cssClass] of cases) {
      const fixture = TestBed.createComponent(PeakDetail);
      fixture.componentRef.setInput('slug', 'longs-peak');
      fixture.detectChanges();

      const data = detail();
      data.airQuality = { usAqi, pm25: 10, fetchedAt: '2026-06-10T12:00:00Z' };
      httpMock.expectOne('/api/routes/longs-peak').flush(data);
      await fixture.whenStable();
      fixture.detectChanges();

      const value = (fixture.nativeElement as HTMLElement)
        .querySelector(`[data-testid="sky-air"] .value.${cssClass}`);
      expect(value, `AQI ${usAqi} should carry .${cssClass}`).not.toBeNull();
      expect(value!.textContent).toContain(String(usAqi));
    }
  });

  it('shows unavailable in the AQI tile when airQuality is null', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.airQuality = null;
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const sky = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="sky-air"]');
    expect(sky?.textContent ?? '').toContain('unavailable');
  });

  it('collapses the hourly table to 24 rows with a Show 48h toggle', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.forecast tbody tr').length).toBe(24);

    const toggle = el.querySelector('[data-testid="forecast-toggle"]') as HTMLButtonElement;
    expect(toggle?.textContent ?? '').toContain('48');

    toggle.click();
    fixture.detectChanges();

    expect(el.querySelectorAll('.forecast tbody tr').length).toBe(48);
    expect(toggle.textContent ?? '').toContain('24');
  });

  it('renders Gust and Clouds columns with em-dash for nulls', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.forecastNext48h![0].gustMph = 32;
    data.forecastNext48h![0].cloudCoverPct = 80;
    data.forecastNext48h![1].gustMph = null;
    data.forecastNext48h![1].cloudCoverPct = null;
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const headerText = Array.from(el.querySelectorAll('.forecast thead th')).map(th => th.textContent ?? '');
    expect(headerText).toContain('Gust');
    expect(headerText).toContain('Clouds');

    const rows = el.querySelectorAll('.forecast tbody tr');
    expect(rows[0].textContent ?? '').toContain('32');
    expect(rows[1].textContent ?? '').toContain('—');
  });

  it('renders the hero identity (h1 + facts) before detail loads, with no boilerplate', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'mount-whitney');
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h1')?.textContent).toContain('Mount Whitney');
    expect(el.querySelector('.hero .facts')).not.toBeNull();
    // A6 boilerplate is gone.
    expect(el.querySelector('.lede')).toBeNull();
    expect(el.querySelector('.covers')).toBeNull();
    expect(el.querySelector('.range-peers')).toBeNull();

    httpMock.expectOne('/api/routes/mount-whitney').flush({} as RouteDetail);
  });

  it('shows a single "All <range> peaks" link, not a peer wall', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'mount-whitney');
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const links = el.querySelectorAll('.range-link a');
    expect(links.length).toBe(1);
    expect(links[0].textContent).toContain('Sierra Nevada'); // Whitney's manifest range

    httpMock.expectOne('/api/routes/mount-whitney').flush({} as RouteDetail);
  });

  it('renders per-source Max gust and CAPE columns', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    const data = detail();
    data.perSourceForecast = [
      { sourceName: 'HRRR', windMph: 20, tempF: 30, precipitationProbabilityPct: 40, maxGustMph: 42.5, maxCapeJkg: 850, fetchedAt: '2026-06-10T12:00:00Z' },
      { sourceName: 'GFS', windMph: 18, tempF: 28, precipitationProbabilityPct: 35, maxGustMph: null, maxCapeJkg: null, fetchedAt: '2026-06-10T12:00:00Z' },
    ];
    httpMock.expectOne('/api/routes/longs-peak').flush(data);
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const headerText = Array.from(el.querySelectorAll('.per-source thead th')).map(th => th.textContent ?? '');
    expect(headerText).toContain('Max gust');
    expect(headerText).toContain('CAPE');

    const rows = el.querySelectorAll('.per-source tbody tr');
    expect(rows[1].textContent ?? '').toContain('—');
  });

  it('clears stale detail when navigating to a new slug', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();
    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.window-strip')).not.toBeNull();

    // Navigate to a new peak — the old strip must disappear until the new data loads.
    fixture.componentRef.setInput('slug', 'mount-whitney');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.window-strip')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('.hero-meta .updated')).toBeNull();

    httpMock.expectOne('/api/routes/mount-whitney').flush(detail());
  });

  it('shows the glaciated warning banner for a glaciated peak', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'mount-rainier'); // glaciated in the manifest
    fixture.detectChanges();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('.glacier-warning');
    expect(banner).not.toBeNull();
    expect(banner!.textContent ?? '').toContain('Glaciated peak');

    httpMock.expectOne('/api/routes/mount-rainier').flush({} as RouteDetail);
  });

  it('shows no glaciated warning for a non-glaciated peak', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'pikes-peak'); // not glaciated
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.glacier-warning')).toBeNull();

    httpMock.expectOne('/api/routes/pikes-peak').flush({} as RouteDetail);
  });

  describe('metric display mode', () => {
    async function renderMetric() {
      const svc = TestBed.inject(SettingsService);
      svc.applyPreset('metric');
      const fixture = TestBed.createComponent(PeakDetail);
      fixture.componentRef.setInput('slug', 'longs-peak');
      fixture.detectChanges();
      httpMock.expectOne('/api/routes/longs-peak').flush(detail());
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('converts the hero elevation to meters', async () => {
      const el = await renderMetric();
      // Hero elevation renders from the static SEO manifest (longs-peak: 14,255 ft),
      // NOT the API detail() fixture — 14255 × 0.3048 = 4344.9 → '1.0-0' → 4,345.
      expect(el.querySelector('.facts b')?.textContent).toMatch(/4,345\s?m/);
    });

    it('renders the forecast table in °C, km/h, and 24h time', async () => {
      const el = await renderMetric();
      const headers = Array.from(el.querySelectorAll('.forecast thead th')).map(th => th.textContent?.trim());
      expect(headers).toContain('°C');
      const firstRow = el.querySelector('.forecast tbody tr')!;
      expect(firstRow.textContent).toContain('4');            // 40°F → 4°C
      expect(firstRow.textContent).toContain('13 km/h');      // 8 mph → 12.87 → 13
      expect(firstRow.textContent).toMatch(/\d{2}:\d{2}/);    // 24h clock (TZ-agnostic)
      expect(firstRow.textContent).not.toContain('mph');
    });

    it('converts snowpack tiles to centimeters', async () => {
      const el = await renderMetric();
      const tiles = el.querySelector('.snowpack')!.textContent!;
      expect(tiles).toContain('3.0 cm');   // SWE 1.2 in
      expect(tiles).toContain('10.2 cm');  // depth 4.0 in
    });
  });
});

function detail(): RouteDetail {
  const hourly = Array.from({ length: 48 }, (_, i) => ({
    time: new Date(Date.UTC(2026, 5, 1, i)).toISOString(),
    tempF: 40,
    windMph: 8,
    precipitationProbabilityPct: 10,
    shortForecast: 'Sunny',
    gustMph: null,
    capeJkg: null,
    precipitationIn: null,
    cloudCoverPct: null,
    visibilityMiles: null,
    apparentTempF: null,
  }));
  return {
    slug: 'longs-peak',
    mountain: 'Longs Peak',
    routeName: 'Keyhole',
    summitElevationFt: 14259,
    classDifficulty: '3',
    isGlaciated: false,
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
    airQualityUsAqi: null,
    perSourceForecast: null,
    airQuality: null,
    daylight: null,
  };
}
