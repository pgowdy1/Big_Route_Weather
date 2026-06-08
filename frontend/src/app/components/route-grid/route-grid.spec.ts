import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouteGrid } from './route-grid';
import { RouteSummary } from '../../models/route-conditions';

describe('RouteGrid', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RouteGrid],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders one card per route on success', async () => {
    const fixture = TestBed.createComponent(RouteGrid);
    fixture.detectChanges();
    const data: RouteSummary[] = [
      summary('a-peak'), summary('b-peak'),
    ];
    httpMock.expectOne('/api/routes').flush(data);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const cards = (fixture.nativeElement as HTMLElement).querySelectorAll('app-route-card');
    expect(cards.length).toBe(2);
  });

  it('shows an error message when the backend fails', async () => {
    const fixture = TestBed.createComponent(RouteGrid);
    fixture.detectChanges();
    httpMock.expectOne('/api/routes').error(new ProgressEvent('Network error'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text.toLowerCase()).toContain('retry');
  });

  it('filters peaks by mountain name (case-insensitive substring)', async () => {
    const fixture = TestBed.createComponent(RouteGrid);
    fixture.detectChanges();
    const data: RouteSummary[] = [
      summary('longs-peak', 'Longs Peak'),
      summary('pikes-peak', 'Pikes Peak'),
      summary('mount-elbert', 'Mount Elbert'),
    ];
    httpMock.expectOne('/api/routes').flush(data);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.componentInstance.onSearch('PEAK');
    fixture.detectChanges();

    const cards = (fixture.nativeElement as HTMLElement).querySelectorAll('app-route-card');
    expect(cards.length).toBe(2);
  });

  it('shows a no-match message when nothing matches the query', async () => {
    const fixture = TestBed.createComponent(RouteGrid);
    fixture.detectChanges();
    const data: RouteSummary[] = [summary('longs-peak', 'Longs Peak')];
    httpMock.expectOne('/api/routes').flush(data);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.componentInstance.onSearch('xyz');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No peaks match "xyz".');
    const cards = (fixture.nativeElement as HTMLElement).querySelectorAll('app-route-card');
    expect(cards.length).toBe(0);
  });

  it('treats whitespace-only query as empty and shows all peaks', async () => {
    const fixture = TestBed.createComponent(RouteGrid);
    fixture.detectChanges();
    const data: RouteSummary[] = [
      summary('longs-peak', 'Longs Peak'),
      summary('pikes-peak', 'Pikes Peak'),
    ];
    httpMock.expectOne('/api/routes').flush(data);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    fixture.componentInstance.onSearch('   ');
    fixture.detectChanges();

    const cards = (fixture.nativeElement as HTMLElement).querySelectorAll('app-route-card');
    expect(cards.length).toBe(2);
  });
});

function summary(slug: string, mountain = 'Test Mountain'): RouteSummary {
  return {
    slug,
    mountain,
    routeName: 'Standard',
    summitElevationFt: 14000,
    classDifficulty: '3',
    grade: 'B',
    overallScore: 85,
    drivers: [],
    updatedAt: new Date().toISOString(),
    isStale: false,
    consensus: null,
  };
}
