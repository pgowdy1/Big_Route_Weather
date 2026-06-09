import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MapHome, markerIconSpec } from './map-home';

describe('MapHome', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MapHome],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushAll(opts: { ranges?: any[]; positions?: any[]; routes?: any[] } = {}) {
    httpMock.expectOne('/api/ranges').flush(opts.ranges ?? []);
    httpMock.expectOne('/api/routes/positions').flush(opts.positions ?? []);
    httpMock.expectOne('/api/routes').flush(opts.routes ?? []);
  }

  it('requests ranges, positions, and routes on init', () => {
    TestBed.createComponent(MapHome).detectChanges();
    flushAll();
  });

  it('paints ranges independently of routes (does not wait on routes)', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    httpMock.expectOne('/api/ranges').flush([
      {
        slug: 'r',
        name: 'R',
        color: '#f00',
        description: '',
        displayOrder: 1,
        perimeterGeoJson: { type: 'Polygon', coordinates: [[[0, 0], [1, 0], [1, 1], [0, 1], [0, 0]]] },
      },
    ]);

    expect(fixture.componentInstance.ranges().length).toBe(1);
    expect(fixture.componentInstance.loading()).toBe(true);
    expect(fixture.componentInstance.error()).toBeNull();

    httpMock.expectOne('/api/routes/positions').flush([]);
    httpMock.expectOne('/api/routes').flush([]);
  });

  it('captures positions for ghost markers when /api/routes/positions resolves', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    httpMock.expectOne('/api/ranges').flush([]);
    httpMock.expectOne('/api/routes/positions').flush([
      { slug: 'mt-x', mountain: 'Mt X', summitLat: 40, summitLon: -105, rangeSlug: 'r' },
    ]);

    expect(fixture.componentInstance.positions().length).toBe(1);
    expect(fixture.componentInstance.routes().length).toBe(0);

    httpMock.expectOne('/api/routes').flush([]);
  });

  it('flips loading false and stamps lastFetchedAt when routes resolves', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    httpMock.expectOne('/api/routes').flush([]);
    expect(fixture.componentInstance.loading()).toBe(false);
    expect(fixture.componentInstance.lastFetchedAt()).not.toBeNull();

    httpMock.expectOne('/api/ranges').flush([]);
    httpMock.expectOne('/api/routes/positions').flush([]);
  });

  it('shows a loading chip while conditions are pending and hides it after', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.loading-chip')).not.toBeNull();

    httpMock.expectOne('/api/ranges').flush([]);
    httpMock.expectOne('/api/routes/positions').flush([]);
    httpMock.expectOne('/api/routes').flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.loading-chip')).toBeNull();
  });

  it('hides the loading chip when an error overlay takes over', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    httpMock.expectOne('/api/ranges').flush([]);
    httpMock.expectOne('/api/routes/positions').flush([]);
    httpMock.expectOne('/api/routes').error(new ProgressEvent('boom'), { status: 500, statusText: 'boom' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.loading-chip')).toBeNull();
    expect(fixture.nativeElement.querySelector('.map-error-overlay')).not.toBeNull();
  });

  it('stays silent when ranges fails (no error overlay)', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    httpMock.expectOne('/api/ranges').error(new ProgressEvent('boom'));
    httpMock.expectOne('/api/routes/positions').flush([]);
    httpMock.expectOne('/api/routes').flush([]);

    fixture.detectChanges();
    expect(fixture.componentInstance.error()).toBeNull();
    expect(fixture.nativeElement.querySelector('.map-error-overlay')).toBeNull();
  });

  it('stays silent when positions fails (no error overlay)', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    httpMock.expectOne('/api/ranges').flush([]);
    httpMock.expectOne('/api/routes/positions').error(new ProgressEvent('boom'));
    httpMock.expectOne('/api/routes').flush([]);

    fixture.detectChanges();
    expect(fixture.componentInstance.error()).toBeNull();
    expect(fixture.nativeElement.querySelector('.map-error-overlay')).toBeNull();
  });

  it('surfaces an obvious overlay when routes fails', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    httpMock.expectOne('/api/ranges').flush([]);
    httpMock.expectOne('/api/routes/positions').flush([]);
    httpMock.expectOne('/api/routes').error(new ProgressEvent('boom'), { status: 500, statusText: 'boom' });

    fixture.detectChanges();
    const err = fixture.componentInstance.error();
    expect(err).not.toBeNull();
    expect(err!.kind).toBe('routes');

    const overlay = fixture.nativeElement.querySelector('.map-error-overlay');
    expect(overlay).not.toBeNull();
    expect(overlay.textContent).toContain("Couldn't load conditions");
    const button = overlay.querySelector('button');
    expect(button).not.toBeNull();
    expect(button.textContent).toContain('Retry');
  });

  it('clears the overlay and refires /api/routes when Retry is clicked', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();

    httpMock.expectOne('/api/ranges').flush([]);
    httpMock.expectOne('/api/routes/positions').flush([]);
    httpMock.expectOne('/api/routes').error(new ProgressEvent('boom'), { status: 500, statusText: 'boom' });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.map-error-overlay button') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toBeNull();
    expect(fixture.componentInstance.loading()).toBe(true);

    httpMock.expectOne('/api/routes').flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.map-error-overlay')).toBeNull();
    expect(fixture.componentInstance.loading()).toBe(false);
  });
});

describe('markerIconSpec', () => {
  it('renders graded routes as interactive grade dots', () => {
    for (const grade of ['A', 'B', 'C', 'D', 'F'] as const) {
      const spec = markerIconSpec({ grade });
      expect(spec.className).toBe('peak-marker');
      expect(spec.dotClass).toBe(`grade-${grade.toLowerCase()}`);
      expect(spec.interactive).toBe(true);
    }
  });

  it('renders null-grade routes as non-interactive ghost markers', () => {
    const spec = markerIconSpec({ grade: null });
    expect(spec.className).toBe('peak-marker peak-marker-ghost');
    expect(spec.dotClass).toBe('grade-ghost');
    expect(spec.interactive).toBe(false);
  });
});
