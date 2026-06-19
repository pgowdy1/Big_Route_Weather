import { ALL_PEAKS, getPeakBySlug, getPeaksInRange } from './peaks-catalog';

describe('peaks-catalog', () => {
  it('loads a non-empty manifest with unique slugs and complete fields', () => {
    expect(ALL_PEAKS.length).toBeGreaterThanOrEqual(124);
    const slugs = ALL_PEAKS.map(p => p.slug);
    expect(new Set(slugs).size).toBe(slugs.length); // unique
    for (const p of ALL_PEAKS) {
      expect(p.slug).toBeTruthy();
      expect(p.mountain).toBeTruthy();
      expect(p.routeName).toBeTruthy();
      expect(p.rangeName).toBeTruthy();
      expect(p.rangeSlug).toBeTruthy();
      expect(p.summitElevationFt).toBeGreaterThan(0);
      expect(Number.isFinite(p.summitLat)).toBe(true);
      expect(Number.isFinite(p.summitLon)).toBe(true);
    }
  });

  it('looks up a known peak and its range peers', () => {
    const whitney = getPeakBySlug('mount-whitney');
    expect(whitney?.mountain).toBe('Mount Whitney');
    const peers = getPeaksInRange(whitney!.rangeSlug, whitney!.slug);
    expect(peers.length).toBeGreaterThan(0);
    expect(peers.some(p => p.slug === 'mount-whitney')).toBe(false);
  });
});
