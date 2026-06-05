import { TestBed } from '@angular/core/testing';
import { ConsensusBadge } from './consensus-badge';
import { Consensus } from '../../models/route-conditions';

describe('ConsensusBadge', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ConsensusBadge] });
  });

  function build(level: Consensus['level'], overrides: Partial<Consensus> = {}): Consensus {
    return {
      level,
      worstFactor: null,
      coefficientOfVariationByFactor: { Wind: 0.1, Temperature: 0.05, Precipitation: 0.2 },
      sourcesReporting: 4,
      sourcesAttempted: 5,
      ...overrides,
    };
  }

  it('renders nothing when consensus is null', () => {
    const fixture = TestBed.createComponent(ConsensusBadge);
    fixture.componentRef.setInput('consensus', null);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="consensus-badge"]')).toBeNull();
  });

  it('exposes the level via a data-level attribute', () => {
    const fixture = TestBed.createComponent(ConsensusBadge);
    fixture.componentRef.setInput('consensus', build('high'));
    fixture.detectChanges();

    const badge = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="consensus-badge"]');
    expect(badge?.getAttribute('data-level')).toBe('high');
  });

  it('renders a sources count element showing "N of M sources"', () => {
    const fixture = TestBed.createComponent(ConsensusBadge);
    fixture.componentRef.setInput('consensus', build('medium', { sourcesReporting: 3, sourcesAttempted: 5 }));
    fixture.detectChanges();

    const count = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="sources-count"]');
    expect(count).toBeTruthy();
    const text = count?.textContent ?? '';
    expect(text).toContain('3');
    expect(text).toContain('5');
  });

  it('shows the worst factor name when level is medium', () => {
    const fixture = TestBed.createComponent(ConsensusBadge);
    fixture.componentRef.setInput('consensus', build('medium', { worstFactor: 'Wind' }));
    fixture.detectChanges();

    const worst = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="worst-factor"]');
    expect(worst?.textContent ?? '').toContain('Wind');
  });

  it('shows the worst factor name when level is low', () => {
    const fixture = TestBed.createComponent(ConsensusBadge);
    fixture.componentRef.setInput('consensus', build('low', { worstFactor: 'Precipitation' }));
    fixture.detectChanges();

    const worst = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="worst-factor"]');
    expect(worst?.textContent ?? '').toContain('Precipitation');
  });

  it('omits worst factor element when level is high', () => {
    const fixture = TestBed.createComponent(ConsensusBadge);
    fixture.componentRef.setInput('consensus', build('high', { worstFactor: null }));
    fixture.detectChanges();

    const worst = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="worst-factor"]');
    expect(worst).toBeNull();
  });
});
