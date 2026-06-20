import { SeoMeta } from './seo.service';
import { SITE_NAME, DEFAULT_DESCRIPTION } from './seo.constants';
import { PeakSeo } from './peak-seo';
import { websiteJsonLd, mountainJsonLd, breadcrumbJsonLd } from './structured-data';

export function homeMeta(): SeoMeta {
  return {
    title: 'Big Route Weather — Climbing & Mountaineering Weather for Big Peaks',
    description: DEFAULT_DESCRIPTION,
    path: '/',
    jsonLd: [websiteJsonLd()],
  };
}

export function allPeaksMeta(): SeoMeta {
  return {
    title: `All Peaks — Climbing Weather & Route Conditions | ${SITE_NAME}`,
    description:
      'Browse current climbing-weather grades and route conditions for every peak we track — ' +
      'the Cascades, Sierra Nevada, Wasatch, Colorado 14ers, and more.',
    path: '/all',
  };
}

export function aboutMeta(): SeoMeta {
  return {
    title: `About | ${SITE_NAME}`,
    description: 'How Big Route Weather grades climbing conditions on big objectives, and the weather sources behind it.',
    path: '/about',
  };
}

export function diagnosticsMeta(): SeoMeta {
  return { title: `Diagnostics | ${SITE_NAME}`, description: 'Internal diagnostics.', path: '/diagnostics', noindex: true };
}

export function peakMeta(p: PeakSeo): SeoMeta {
  const elev = p.summitElevationFt.toLocaleString('en-US');
  return {
    title: `${p.mountain} Weather Forecast & Climbing Conditions | ${SITE_NAME}`,
    description:
      `Current forecast, summit conditions, and a route grade for ${p.mountain} ` +
      `(${elev} ft, Class ${p.classDifficulty}) via the ${p.routeName} in the ${p.rangeName} — ` +
      `wind, temperature, precipitation, and snowpack for climbers, mountaineers, and hikers.`,
    path: `/peak/${p.slug}`,
    ogType: 'article',
    jsonLd: [mountainJsonLd(p), breadcrumbJsonLd(p)],
  };
}
