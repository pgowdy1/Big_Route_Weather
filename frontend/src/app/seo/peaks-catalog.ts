import manifest from './peaks.manifest.json';
import { PeakSeo } from './peak-seo';

export const ALL_PEAKS: readonly PeakSeo[] = manifest as PeakSeo[];

const BY_SLUG = new Map(ALL_PEAKS.map(p => [p.slug, p] as const));

export function getPeakBySlug(slug: string): PeakSeo | undefined {
  return BY_SLUG.get(slug);
}

// Other peaks in the same range — used for internal links on a peak page.
export function getPeaksInRange(rangeSlug: string, excludeSlug?: string): PeakSeo[] {
  return ALL_PEAKS.filter(p => p.rangeSlug === rangeSlug && p.slug !== excludeSlug);
}
