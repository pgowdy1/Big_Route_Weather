// Emits public/sitemap.xml from the committed manifest + static routes.
// Runs before the build so the file is picked up as a static asset.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const SITE_URL = 'https://bigrouteweather.com';
const here = dirname(fileURLToPath(import.meta.url));

export function buildSitemapUrls(peaks) {
  const staticPaths = ['/', '/all', '/about']; // NOT /diagnostics (noindexed)
  const peakPaths = peaks.map(p => `/peak/${p.slug}`);
  return [...staticPaths, ...peakPaths].map(p => (p === '/' ? `${SITE_URL}/` : SITE_URL + p));
}

function escapeXml(s) {
  return s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[c]));
}

export function buildSitemapXml(urls) {
  const body = urls.map(u => `  <url><loc>${escapeXml(u)}</loc></url>`).join('\n');
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${body}\n</urlset>\n`;
}

// Only write when run directly (not when imported by the test).
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const manifest = JSON.parse(readFileSync(join(here, '..', 'src', 'app', 'seo', 'peaks.manifest.json'), 'utf8'));
  const urls = buildSitemapUrls(manifest);
  writeFileSync(join(here, '..', 'public', 'sitemap.xml'), buildSitemapXml(urls));
  console.log(`Wrote sitemap with ${urls.length} URLs`);
}
