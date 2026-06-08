import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Diagnostics } from './diagnostics';

describe('Diagnostics', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Diagnostics],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests /api/diagnostics/daily-calls?days=7 on init', () => {
    const fixture = TestBed.createComponent(Diagnostics);
    fixture.detectChanges();
    const req = httpMock.expectOne('/api/diagnostics/daily-calls?days=7');
    expect(req.request.method).toBe('GET');
    req.flush({ days: 7, asOfUtc: '2026-06-07T18:00:00Z', entries: [] });
  });

  it('renders a row per date and a column per source', async () => {
    const fixture = TestBed.createComponent(Diagnostics);
    fixture.detectChanges();
    httpMock.expectOne('/api/diagnostics/daily-calls?days=7').flush({
      days: 7,
      asOfUtc: '2026-06-07T18:00:00Z',
      entries: [
        { dateUtc: '2026-06-07', source: 'NWS',       count: 10, isToday: true  },
        { dateUtc: '2026-06-07', source: 'OpenMeteo', count: 20, isToday: true  },
        { dateUtc: '2026-06-06', source: 'NWS',       count:  5, isToday: false },
        { dateUtc: '2026-06-06', source: 'OpenMeteo', count: 15, isToday: false },
      ],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const headers = (fixture.nativeElement as HTMLElement).querySelectorAll('thead th');
    // Date col + 2 sources + Total = 4
    expect(headers.length).toBe(4);

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
  });

  it('marks the today row with the today indicator', async () => {
    const fixture = TestBed.createComponent(Diagnostics);
    fixture.detectChanges();
    httpMock.expectOne('/api/diagnostics/daily-calls?days=7').flush({
      days: 7,
      asOfUtc: '2026-06-07T18:00:00Z',
      entries: [
        { dateUtc: '2026-06-07', source: 'NWS', count: 10, isToday: true  },
        { dateUtc: '2026-06-06', source: 'NWS', count:  5, isToday: false },
      ],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const todayRow = (fixture.nativeElement as HTMLElement).querySelector('tbody tr.today');
    expect(todayRow).not.toBeNull();
    expect(todayRow!.querySelector('.today-tag')).not.toBeNull();
  });

  it('shows the empty-state message when the backend returns no entries', async () => {
    const fixture = TestBed.createComponent(Diagnostics);
    fixture.detectChanges();
    httpMock.expectOne('/api/diagnostics/daily-calls?days=7').flush({
      days: 7,
      asOfUtc: '2026-06-07T18:00:00Z',
      entries: [],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('table.calls')).toBeNull();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text.toLowerCase()).toContain('no data');
  });

  it('shows an error when the request fails', async () => {
    const fixture = TestBed.createComponent(Diagnostics);
    fixture.detectChanges();
    httpMock.expectOne('/api/diagnostics/daily-calls?days=7').error(new ProgressEvent('Network error'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const status = (fixture.nativeElement as HTMLElement).querySelector('.status.error');
    expect(status).not.toBeNull();
  });

  it('re-fetches when refresh is clicked', async () => {
    const fixture = TestBed.createComponent(Diagnostics);
    fixture.detectChanges();
    httpMock.expectOne('/api/diagnostics/daily-calls?days=7').flush({
      days: 7,
      asOfUtc: '2026-06-07T18:00:00Z',
      entries: [{ dateUtc: '2026-06-07', source: 'NWS', count: 1, isToday: true }],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector('button.refresh') as HTMLButtonElement;
    button.click();
    const req = httpMock.expectOne('/api/diagnostics/daily-calls?days=7');
    expect(req.request.method).toBe('GET');
    req.flush({ days: 7, asOfUtc: '2026-06-07T18:00:30Z', entries: [] });
  });
});
