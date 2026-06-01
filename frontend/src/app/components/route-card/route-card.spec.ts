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
