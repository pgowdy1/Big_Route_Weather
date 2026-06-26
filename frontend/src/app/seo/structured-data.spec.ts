import { websiteJsonLd, mountainJsonLd, breadcrumbJsonLd } from './structured-data';
import { PeakSeo } from './peak-seo';
import { SITE_URL } from './seo.constants';

const peak: PeakSeo = {
  slug: 'mount-whitney', mountain: 'Mount Whitney', routeName: "Mountaineer's Route",
  summitElevationFt: 14505, classDifficulty: '3', rangeName: 'Sierra Nevada',
  rangeSlug: 'sierra-nevada', summitLat: 36.5786, summitLon: -118.292,
};

describe('structured-data', () => {
  it('builds a WebSite node', () => {
    const w = websiteJsonLd() as any;
    expect(w['@type']).toBe('WebSite');
    expect(w.url).toBe(`${SITE_URL}/`);
  });

  it('builds a Mountain node with geo + elevation', () => {
    const m = mountainJsonLd(peak) as any;
    expect(m['@type']).toBe('Mountain');
    expect(m.name).toBe('Mount Whitney');
    expect(m.url).toBe(`${SITE_URL}/peak/mount-whitney/`);
    expect(m.geo.latitude).toBe(36.5786);
    expect(m.elevation.value).toBe(14505);
  });

  it('builds a 3-level breadcrumb ending at the peak', () => {
    const b = breadcrumbJsonLd(peak) as any;
    expect(b['@type']).toBe('BreadcrumbList');
    expect(b.itemListElement.map((i: any) => i.name)).toEqual(['Home', 'All peaks', 'Mount Whitney']);
    expect(b.itemListElement[2].item).toBe(`${SITE_URL}/peak/mount-whitney/`);
  });
});
