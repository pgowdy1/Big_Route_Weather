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
});

function summary(slug: string): RouteSummary {
  return {
    slug,
    mountain: 'Test Mountain',
    routeName: 'Standard',
    summitElevationFt: 14000,
    classDifficulty: '3',
    grade: 'B',
    overallScore: 85,
    drivers: [],
    updatedAt: new Date().toISOString(),
    isStale: false,
  };
}
