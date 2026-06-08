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

  function badgeEl(consensus: Consensus | null): HTMLElement | null {
    const fixture = TestBed.createComponent(ConsensusBadge);
    fixture.componentRef.setInput('consensus', consensus);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('[data-testid="consensus-badge"]');
  }

  it('renders nothing when consensus is null', () => {
    expect(badgeEl(null)).toBeNull();
  });

  it('stays silent on high consensus', () => {
    expect(badgeEl(build('high'))).toBeNull();
  });

  it('stays silent on medium consensus', () => {
    expect(badgeEl(build('medium', { worstFactor: 'Wind' }))).toBeNull();
  });

  it('renders with data-level="low" only when consensus is low', () => {
    const el = badgeEl(build('low', { worstFactor: 'Precipitation' }));
    expect(el).toBeTruthy();
    expect(el?.getAttribute('data-level')).toBe('low');
  });

  it('surfaces source counts via the tooltip, not visible text', () => {
    const el = badgeEl(build('low', { sourcesReporting: 3, sourcesAttempted: 5, worstFactor: 'Wind' }));
    const title = el?.getAttribute('title') ?? '';
    expect(title).toContain('3');
    expect(title).toContain('5');
  });

  it('mentions the worst factor in the tooltip when present', () => {
    const el = badgeEl(build('low', { worstFactor: 'Wind' }));
    const title = el?.getAttribute('title') ?? '';
    expect(title.toLowerCase()).toContain('wind');
  });

  it('omits the worst factor from the tooltip when absent', () => {
    const el = badgeEl(build('low', { worstFactor: null }));
    const title = el?.getAttribute('title') ?? '';
    expect(title.toLowerCase()).not.toContain('disagree on');
  });
});
