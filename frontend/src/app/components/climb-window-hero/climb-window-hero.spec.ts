import { TestBed } from '@angular/core/testing';
import { ClimbWindowHero } from './climb-window-hero';
import { ClimbWindow } from '../../models/route-conditions';

describe('ClimbWindowHero', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [ClimbWindowHero] });
  });

  const win = (over: Partial<ClimbWindow> = {}): ClimbWindow => ({
    startUtc: new Date(Date.now() + 30 * 3600_000).toISOString(),
    endUtc: new Date(Date.now() + 39 * 3600_000).toISOString(),
    grade: 'A',
    score: 95,
    endReason: 'closes as storm energy builds',
    lowConfidence: false,
    ...over,
  });

  function create(windows: ClimbWindow[] | null) {
    const fixture = TestBed.createComponent(ClimbWindowHero);
    fixture.componentRef.setInput('windows', windows);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the strongest upcoming window with its end reason', () => {
    const el = create([
      win({ grade: 'B', score: 84, endUtc: new Date(Date.now() + 10 * 3600_000).toISOString(), startUtc: new Date(Date.now() + 2 * 3600_000).toISOString() }),
      win(), // A/95 → the hero
    ]);

    const hero = el.querySelector('.cwh');
    expect(hero).toBeTruthy();
    expect(hero!.textContent).toContain('storm energy builds');
    expect(el.querySelector('app-grade-badge')).toBeTruthy();
  });

  it('skips windows that already ended', () => {
    const past = win({
      startUtc: new Date(Date.now() - 20 * 3600_000).toISOString(),
      endUtc: new Date(Date.now() - 10 * 3600_000).toISOString(),
    });
    const el = create([past]);

    expect(el.querySelector('.cwh')).toBeNull();
    expect(el.querySelector('.cwh-none')).toBeTruthy();
  });

  it('states plainly when there is no window this week', () => {
    const el = create([]);
    expect(el.querySelector('.cwh-none')).toBeTruthy();
  });

  it('renders nothing at all when windows is null (ghost route)', () => {
    const el = create(null);
    expect(el.querySelector('.cwh')).toBeNull();
    expect(el.querySelector('.cwh-none')).toBeNull();
  });

  it('flags low-confidence windows', () => {
    const el = create([win({ lowConfidence: true })]);
    expect(el.querySelector('.cwh-conf')).toBeTruthy();
  });
});
