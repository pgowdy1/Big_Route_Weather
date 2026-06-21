import { homeMeta, allPeaksMeta, aboutMeta, diagnosticsMeta, peakMeta } from './route-meta';
import { PeakSeo } from './peak-seo';

const peak: PeakSeo = {
  slug: 'mount-rainier', mountain: 'Mount Rainier', routeName: 'Disappointment Cleaver',
  summitElevationFt: 14411, classDifficulty: '4', rangeName: 'Cascade Range',
  rangeSlug: 'cascades', summitLat: 46.8523, summitLon: -121.7603,
};

describe('route-meta', () => {
  it('home/all/about have titles and index by default', () => {
    expect(homeMeta().title).toContain('Big Route Weather');
    expect(allPeaksMeta().path).toBe('/all');
    expect(aboutMeta().path).toBe('/about');
    expect(homeMeta().noindex).toBeFalsy();
  });

  it('diagnostics is noindex', () => {
    expect(diagnosticsMeta().noindex).toBe(true);
  });

  it('peak meta has a focused title, data-rich description, and JSON-LD', () => {
    const m = peakMeta(peak);
    expect(m.title).toBe('Mount Rainier Weather Forecast & Climbing Conditions | Big Route Weather');
    expect(m.description).toContain('14,411');
    expect(m.description).toContain('Disappointment Cleaver');
    expect(m.description).toContain('Cascade Range');
    expect(m.path).toBe('/peak/mount-rainier');
    expect(m.ogType).toBe('article');
    expect((m.jsonLd ?? []).length).toBe(2); // Mountain + BreadcrumbList
  });
});
