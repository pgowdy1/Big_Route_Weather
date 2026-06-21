import { SITE_NAME, SITE_URL } from './seo.constants';
import { PeakSeo } from './peak-seo';

export function websiteJsonLd(): object {
  return { '@context': 'https://schema.org', '@type': 'WebSite', name: SITE_NAME, url: `${SITE_URL}/` };
}

export function mountainJsonLd(p: PeakSeo): object {
  return {
    '@context': 'https://schema.org',
    '@type': 'Mountain',
    name: p.mountain,
    url: `${SITE_URL}/peak/${p.slug}/`,
    elevation: { '@type': 'QuantitativeValue', value: p.summitElevationFt, unitCode: 'FOT' },
    geo: { '@type': 'GeoCoordinates', latitude: p.summitLat, longitude: p.summitLon },
    containedInPlace: { '@type': 'Place', name: p.rangeName },
  };
}

export function breadcrumbJsonLd(p: PeakSeo): object {
  return {
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: [
      { '@type': 'ListItem', position: 1, name: 'Home', item: `${SITE_URL}/` },
      { '@type': 'ListItem', position: 2, name: 'All peaks', item: `${SITE_URL}/all/` },
      { '@type': 'ListItem', position: 3, name: p.mountain, item: `${SITE_URL}/peak/${p.slug}/` },
    ],
  };
}
