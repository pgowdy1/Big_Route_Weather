// Asserts the build emitted real, substantive static HTML for a peak page.
import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const dist = join(here, '..', 'dist');

function find(dir, rel) {
  if (!existsSync(dir)) return null;
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    const st = statSync(full);
    if (st.isDirectory()) {
      const hit = find(full, `${rel}/${name}`);
      if (hit) return hit;
    } else if (`${rel}/${name}`.endsWith('peak/mount-whitney/index.html')) {
      return full;
    }
  }
  return null;
}

const file = find(dist, '');
if (!file) {
  console.error('FAIL: no prerendered peak/mount-whitney/index.html under dist/');
  process.exit(1);
}
const html = readFileSync(file, 'utf8');
const required = [
  '<title>Mount Whitney Weather Forecast',  // baked per-page title
  'Mount Whitney Weather',                  // baked h1 (manifest-driven)
  "Mountaineer's Route",                    // facts kicker (route)
  '14,505',                                 // facts kicker (elevation)
  'Sierra Nevada',                          // facts kicker (range)
  'application/ld+json',                    // structured data
];
const missing = required.filter(s => !html.includes(s));
if (missing.length) {
  console.error('FAIL: prerendered Whitney page missing:', missing);
  process.exit(1);
}
console.log('verify-prerender OK:', file);
