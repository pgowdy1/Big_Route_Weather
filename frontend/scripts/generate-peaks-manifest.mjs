// Generates src/app/seo/peaks.manifest.json from the running API's /api/routes.
// The manifest is the single build-time source for prerender + SEO; it must stay
// in sync with the backend seeder (guarded by a backend parity test).
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const API_BASE = process.env.API_BASE ?? 'http://localhost:5150';
const FIELDS = ['slug', 'mountain', 'routeName', 'summitElevationFt',
  'classDifficulty', 'rangeName', 'rangeSlug', 'summitLat', 'summitLon'];

const res = await fetch(`${API_BASE}/api/routes`);
if (!res.ok) throw new Error(`GET /api/routes failed: ${res.status} ${res.statusText}`);
const routes = await res.json();

const manifest = routes
  .map(r => Object.fromEntries(FIELDS.map(f => [f, r[f]])))
  .sort((a, b) => a.slug.localeCompare(b.slug)); // deterministic output

for (const p of manifest) {
  for (const f of FIELDS) {
    if (p[f] === undefined || p[f] === null || p[f] === '') {
      throw new Error(`Peak ${p.slug} is missing field "${f}" from /api/routes`);
    }
  }
}

const out = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'app', 'seo', 'peaks.manifest.json');
writeFileSync(out, JSON.stringify(manifest, null, 2) + '\n');
console.log(`Wrote ${manifest.length} peaks to ${out}`);
