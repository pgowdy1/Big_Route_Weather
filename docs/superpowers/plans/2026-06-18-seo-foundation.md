# SEO Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make bigrouteweather.com crawlable and shareable — give every route (esp. the 124 peak pages) real, self-describing static HTML with per-page meta/OG/structured-data, a sitemap, and substantive prerendered content — without adding a server runtime.

**Architecture:** Two phases. **Phase A** adds a client-side SEO metadata layer (generated peaks manifest + parity test, `SeoService`, JSON-LD, substantive per-peak content, sitemap/robots, static OG image) — shippable on its own and helps Googlebot immediately. **Phase B** converts the SPA to **build-time prerendering (SSG)** via `@angular/ssr` with `outputMode: static`, gating data fetches to the browser so prerender does no network I/O, then bakes Phase A's meta + content into static HTML for all crawlers and social scrapers.

**Tech Stack:** Angular 21 (zoneless, signal-based, `@angular/build:application`), Vitest/jsdom, `@angular/ssr` (build-time only), Cloudflare Pages (static), ASP.NET Core/xUnit (parity test).

**Branch:** `feature/seo-foundation` (already checked out, off `origin/dev`).

**Spec:** `docs/superpowers/specs/2026-06-18-seo-foundation-design.md`. Canonical base: `https://bigrouteweather.com`.

**Prerequisite:** the backend API must be running locally on `http://localhost:5150` (the project's fixed dev port) for `npm run seo:manifest` to generate the manifest. Do not start servers yourself — if it isn't up, report BLOCKED.

**Phase boundary:** Phase A is independently shippable. Recommended: review (and optionally PR) at the end of Phase A before starting Phase B, since Phase B's SSR-safety guards build directly on the Phase A component changes.

---

## File Structure

**Phase A — added**
- `frontend/scripts/generate-peaks-manifest.mjs` — dev-time generator: fetch `/api/routes`, write the manifest. Single responsibility: keep the manifest in sync with the seeder.
- `frontend/src/app/seo/peaks.manifest.json` — generated, committed catalog of 124 peaks' SEO fields.
- `frontend/src/app/seo/peak-seo.ts` — the `PeakSeo` type.
- `frontend/src/app/seo/peaks-catalog.ts` — typed access to the manifest (`ALL_PEAKS`, `getPeakBySlug`, `getPeaksInRange`).
- `frontend/src/app/seo/seo.constants.ts` — site URL/name/default OG image/default description.
- `frontend/src/app/seo/seo.service.ts` — sets title/description/canonical/OG/Twitter/robots + JSON-LD per route.
- `frontend/src/app/seo/structured-data.ts` — JSON-LD builders (`websiteJsonLd`, `mountainJsonLd`, `breadcrumbJsonLd`).
- `frontend/src/app/seo/route-meta.ts` — per-route `SeoMeta` builders (home/all/about/diagnostics/peak).
- `frontend/scripts/generate-sitemap.mjs` — build step: emit `sitemap.xml` from the manifest + static routes.
- `frontend/public/robots.txt`, `frontend/public/og-image.png` (branded 1200×630 asset).
- Tests: `frontend/src/app/seo/{seo.service,structured-data,peaks-catalog}.spec.ts`; `backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs`.

**Phase B — added/modified**
- `frontend/src/main.server.ts`, `frontend/src/app/app.config.server.ts`, `frontend/src/app/app.routes.server.ts` (scaffolded by `ng add @angular/ssr`, then adjusted).
- `frontend/scripts/verify-prerender.mjs` — post-build smoke check.
- Modified: `frontend/angular.json` (static SSG), `frontend/src/app/app.config.ts` (`provideClientHydration`), `frontend/package.json`, `frontend/public/_redirects`, and SSR-safety edits to `peak-detail`, `map-home`, `route-grid`.

**Phase A — modified**
- `frontend/tsconfig.json` (ensure `resolveJsonModule`), `frontend/package.json` (`seo:manifest`, sitemap build hook), `frontend/src/index.html` (baseline default tags), and per-route `SeoService` wiring + substantive content in `app.ts`/`app.html`, `map-home`, `route-grid`, `about`, and especially `peak-detail`.

---

# PHASE A — SEO metadata layer (client-side, shippable)

## Task A1: Peaks manifest + generator + catalog

**Files:**
- Create: `frontend/scripts/generate-peaks-manifest.mjs`
- Create: `frontend/src/app/seo/peak-seo.ts`, `frontend/src/app/seo/peaks-catalog.ts`, `frontend/src/app/seo/peaks.manifest.json` (generated)
- Modify: `frontend/package.json` (script), `frontend/tsconfig.json` (`resolveJsonModule`)
- Test: `frontend/src/app/seo/peaks-catalog.spec.ts`

- [ ] **Step 1: Write the generator script**

Create `frontend/scripts/generate-peaks-manifest.mjs`:

```js
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
```

- [ ] **Step 2: Add the npm script and ensure JSON imports work**

In `frontend/package.json` `scripts`, add:

```json
    "seo:manifest": "node scripts/generate-peaks-manifest.mjs",
```

In `frontend/tsconfig.json`, ensure `compilerOptions` contains (add if absent):

```json
    "resolveJsonModule": true,
```

- [ ] **Step 3: Generate the manifest (requires the API running on :5150)**

Run: `cd frontend && npm run seo:manifest`
Expected: `Wrote 124 peaks to .../peaks.manifest.json`. The file `frontend/src/app/seo/peaks.manifest.json` now exists with 124 entries sorted by slug. (If the API is not reachable, STOP and report BLOCKED — do not hand-write the manifest.)

- [ ] **Step 4: Write the type and catalog**

Create `frontend/src/app/seo/peak-seo.ts`:

```ts
export interface PeakSeo {
  slug: string;
  mountain: string;
  routeName: string;
  summitElevationFt: number;
  classDifficulty: string;
  rangeName: string;
  rangeSlug: string;
  summitLat: number;
  summitLon: number;
}
```

Create `frontend/src/app/seo/peaks-catalog.ts`:

```ts
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
```

- [ ] **Step 5: Write the failing catalog test**

Create `frontend/src/app/seo/peaks-catalog.spec.ts`:

```ts
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
```

- [ ] **Step 6: Run the test**

Run (from `frontend/`): `npm test`
Expected: PASS (the manifest was generated in Step 3; if `mount-whitney` isn't present, the manifest/API is wrong — investigate, don't change the test).

- [ ] **Step 7: Commit**

```bash
git add frontend/scripts/generate-peaks-manifest.mjs frontend/src/app/seo/peak-seo.ts frontend/src/app/seo/peaks-catalog.ts frontend/src/app/seo/peaks.manifest.json frontend/src/app/seo/peaks-catalog.spec.ts frontend/package.json frontend/tsconfig.json
git commit -m "feat(seo): generated peaks manifest + typed catalog"
```

## Task A2: Backend manifest parity test

Guards that the committed manifest matches the `RouteSeeder` catalog, so a future peak PR that forgets `npm run seo:manifest` fails CI.

**Files:**
- Create: `backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs`

- [ ] **Step 1: Write the parity test**

Create `backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RouteWeather.Data;
using Xunit;

namespace RouteWeather.Core.Tests.Seo;

public class ManifestParityTests
{
    private sealed record PeakSeo(
        string Slug, string Mountain, string RouteName, int SummitElevationFt,
        string ClassDifficulty, string RangeName, string RangeSlug,
        double SummitLat, double SummitLon);

    [Fact]
    public async Task Manifest_matches_the_seeder_catalog()
    {
        var manifest = LoadManifest();

        await using var db = NewContext();
        await RouteSeeder.SeedAsync(db);
        var seeded = await db.Routes.Include(r => r.Range).ToListAsync();

        // Same set of slugs.
        var manifestSlugs = manifest.Select(p => p.Slug).OrderBy(s => s).ToArray();
        var seededSlugs = seeded.Select(r => r.Slug).OrderBy(s => s).ToArray();
        Assert.Equal(seededSlugs, manifestSlugs);

        // Same key fields per slug (catches a stale regenerate).
        var bySlug = manifest.ToDictionary(p => p.Slug);
        foreach (var r in seeded)
        {
            var p = bySlug[r.Slug];
            Assert.Equal(r.Mountain, p.Mountain);
            Assert.Equal(r.RouteName, p.RouteName);
            Assert.Equal(r.SummitElevationFt, p.SummitElevationFt);
            Assert.Equal(r.ClassDifficulty, p.ClassDifficulty);
            Assert.Equal(r.Range!.Slug, p.RangeSlug);
            Assert.Equal(r.Range!.Name, p.RangeName);
        }
    }

    private static List<PeakSeo> LoadManifest()
    {
        var path = FindRepoFile("frontend/src/app/seo/peaks.manifest.json");
        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<PeakSeo>>(json, opts)!;
    }

    // Walk up from the test bin dir to the repo root and resolve a known file.
    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {relative} walking up from {AppContext.BaseDirectory}");
    }

    private static RouteWeatherContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<RouteWeatherContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RouteWeatherContext(opts);
    }
}
```

- [ ] **Step 2: Run the parity test**

Run: `dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj --filter ManifestParityTests`
Expected: PASS (the manifest generated in A1 reflects the current seeder). If it fails on a field mismatch, the manifest is stale — re-run `npm run seo:manifest`, don't weaken the test.

- [ ] **Step 3: Commit**

```bash
git add backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs
git commit -m "test(seo): backend parity guard between manifest and seeder"
```

## Task A3: SEO constants + SeoService

**Files:**
- Create: `frontend/src/app/seo/seo.constants.ts`, `frontend/src/app/seo/seo.service.ts`
- Test: `frontend/src/app/seo/seo.service.spec.ts`

- [ ] **Step 1: Write the constants**

Create `frontend/src/app/seo/seo.constants.ts`:

```ts
export const SITE_URL = 'https://bigrouteweather.com';
export const SITE_NAME = 'Big Route Weather';
export const DEFAULT_OG_IMAGE = `${SITE_URL}/og-image.png`;
export const DEFAULT_DESCRIPTION =
  'Climbing and mountaineering weather forecasts and route-condition grades for big peaks ' +
  'in the Cascades, Sierra Nevada, Wasatch, and beyond — wind, temperature, precipitation, ' +
  'and snowpack for climbers, hikers, and trail runners.';
```

- [ ] **Step 2: Write the failing SeoService test**

Create `frontend/src/app/seo/seo.service.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { DOCUMENT } from '@angular/common';
import { SeoService } from './seo.service';
import { SITE_URL } from './seo.constants';

describe('SeoService', () => {
  let svc: SeoService;
  let doc: Document;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [SeoService] });
    svc = TestBed.inject(SeoService);
    doc = TestBed.inject(DOCUMENT);
  });

  function meta(sel: string): string | null {
    return doc.head.querySelector(sel)?.getAttribute('content') ?? null;
  }

  it('sets title, description, absolute canonical, OG and Twitter tags', () => {
    svc.setMeta({ title: 'T', description: 'D', path: '/peak/mount-whitney' });

    expect(doc.title).toBe('T');
    expect(meta('meta[name="description"]')).toBe('D');
    expect(doc.head.querySelector('link[rel="canonical"]')?.getAttribute('href'))
      .toBe(`${SITE_URL}/peak/mount-whitney`);
    expect(meta('meta[property="og:title"]')).toBe('T');
    expect(meta('meta[property="og:url"]')).toBe(`${SITE_URL}/peak/mount-whitney`);
    expect(meta('meta[name="twitter:card"]')).toBe('summary_large_image');
    expect(meta('meta[name="robots"]')).toBe('index, follow');
  });

  it('emits noindex when asked and a single canonical across calls', () => {
    svc.setMeta({ title: 'A', description: 'D', path: '/a' });
    svc.setMeta({ title: 'B', description: 'D', path: '/b', noindex: true });

    expect(meta('meta[name="robots"]')).toBe('noindex, follow');
    expect(doc.head.querySelectorAll('link[rel="canonical"]').length).toBe(1);
    expect(doc.head.querySelector('link[rel="canonical"]')?.getAttribute('href'))
      .toBe(`${SITE_URL}/b`);
  });

  it('replaces (not stacks) its JSON-LD between navigations', () => {
    svc.setMeta({ title: 'A', description: 'D', path: '/a', jsonLd: [{ '@type': 'WebSite' }] });
    svc.setMeta({ title: 'B', description: 'D', path: '/b', jsonLd: [{ '@type': 'Mountain' }] });

    const scripts = doc.head.querySelectorAll('script[type="application/ld+json"][data-seo]');
    expect(scripts.length).toBe(1);
    expect(scripts[0].textContent).toContain('Mountain');
  });
});
```

- [ ] **Step 3: Run to verify it fails**

Run (from `frontend/`): `npm test`
Expected: FAIL (`SeoService` not implemented).

- [ ] **Step 4: Implement SeoService**

Create `frontend/src/app/seo/seo.service.ts`:

```ts
import { Injectable, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { Title, Meta } from '@angular/platform-browser';
import { SITE_NAME, SITE_URL, DEFAULT_OG_IMAGE } from './seo.constants';

export interface SeoMeta {
  title: string;
  description: string;
  path: string;       // e.g. '/', '/peak/mount-whitney'
  noindex?: boolean;
  ogType?: string;    // default 'website'
  jsonLd?: object[];  // structured-data objects to embed
}

@Injectable({ providedIn: 'root' })
export class SeoService {
  private title = inject(Title);
  private meta = inject(Meta);
  private doc = inject(DOCUMENT);

  setMeta(m: SeoMeta): void {
    const url = absoluteUrl(m.path);

    this.title.setTitle(m.title);
    this.meta.updateTag({ name: 'description', content: m.description });
    this.meta.updateTag({ name: 'robots', content: m.noindex ? 'noindex, follow' : 'index, follow' });
    this.setCanonical(url);

    this.meta.updateTag({ property: 'og:title', content: m.title });
    this.meta.updateTag({ property: 'og:description', content: m.description });
    this.meta.updateTag({ property: 'og:url', content: url });
    this.meta.updateTag({ property: 'og:type', content: m.ogType ?? 'website' });
    this.meta.updateTag({ property: 'og:image', content: DEFAULT_OG_IMAGE });
    this.meta.updateTag({ property: 'og:site_name', content: SITE_NAME });

    this.meta.updateTag({ name: 'twitter:card', content: 'summary_large_image' });
    this.meta.updateTag({ name: 'twitter:title', content: m.title });
    this.meta.updateTag({ name: 'twitter:description', content: m.description });
    this.meta.updateTag({ name: 'twitter:image', content: DEFAULT_OG_IMAGE });

    this.setJsonLd(m.jsonLd ?? []);
  }

  private setCanonical(url: string): void {
    let link = this.doc.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    if (!link) {
      link = this.doc.createElement('link');
      link.setAttribute('rel', 'canonical');
      this.doc.head.appendChild(link);
    }
    link.setAttribute('href', url);
  }

  private setJsonLd(objects: object[]): void {
    this.doc.head.querySelectorAll('script[type="application/ld+json"][data-seo]').forEach(n => n.remove());
    for (const obj of objects) {
      const script = this.doc.createElement('script');
      script.setAttribute('type', 'application/ld+json');
      script.setAttribute('data-seo', '');
      script.textContent = JSON.stringify(obj);
      this.doc.head.appendChild(script);
    }
  }
}

function absoluteUrl(path: string): string {
  const clean = path.startsWith('/') ? path : `/${path}`;
  return clean === '/' ? `${SITE_URL}/` : SITE_URL + clean;
}
```

- [ ] **Step 5: Run to verify it passes**

Run (from `frontend/`): `npm test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/seo/seo.constants.ts frontend/src/app/seo/seo.service.ts frontend/src/app/seo/seo.service.spec.ts
git commit -m "feat(seo): SeoService for per-route head tags"
```

## Task A4: Structured-data (JSON-LD) builders

Breadcrumb reflects the **actual** nav (Home → All peaks → Peak) — there are no range pages (range hubs were rejected in the spec).

**Files:**
- Create: `frontend/src/app/seo/structured-data.ts`
- Test: `frontend/src/app/seo/structured-data.spec.ts`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/app/seo/structured-data.spec.ts`:

```ts
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
    expect(m.url).toBe(`${SITE_URL}/peak/mount-whitney`);
    expect(m.geo.latitude).toBe(36.5786);
    expect(m.elevation.value).toBe(14505);
  });

  it('builds a 3-level breadcrumb ending at the peak', () => {
    const b = breadcrumbJsonLd(peak) as any;
    expect(b['@type']).toBe('BreadcrumbList');
    expect(b.itemListElement.map((i: any) => i.name)).toEqual(['Home', 'All peaks', 'Mount Whitney']);
    expect(b.itemListElement[2].item).toBe(`${SITE_URL}/peak/mount-whitney`);
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run (from `frontend/`): `npm test` → FAIL (module not found).

- [ ] **Step 3: Implement the builders**

Create `frontend/src/app/seo/structured-data.ts`:

```ts
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
    url: `${SITE_URL}/peak/${p.slug}`,
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
      { '@type': 'ListItem', position: 2, name: 'All peaks', item: `${SITE_URL}/all` },
      { '@type': 'ListItem', position: 3, name: p.mountain, item: `${SITE_URL}/peak/${p.slug}` },
    ],
  };
}
```

- [ ] **Step 4: Run to verify it passes** — `npm test` → PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/seo/structured-data.ts frontend/src/app/seo/structured-data.spec.ts
git commit -m "feat(seo): JSON-LD builders (WebSite, Mountain, BreadcrumbList)"
```

## Task A5: Per-route meta builders + wiring

**Files:**
- Create: `frontend/src/app/seo/route-meta.ts`
- Modify: `frontend/src/app/pages/map-home/map-home.ts`, `frontend/src/app/components/route-grid/route-grid.ts`, `frontend/src/app/pages/about/about.ts`, `frontend/src/app/pages/diagnostics/diagnostics.ts`
- Test: `frontend/src/app/seo/route-meta.spec.ts`

- [ ] **Step 1: Write the failing meta-builder test**

Create `frontend/src/app/seo/route-meta.spec.ts`:

```ts
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
```

- [ ] **Step 2: Run to verify it fails** — `npm test` → FAIL.

- [ ] **Step 3: Implement the meta builders**

Create `frontend/src/app/seo/route-meta.ts`:

```ts
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
```

- [ ] **Step 4: Run to verify it passes** — `npm test` → PASS.

- [ ] **Step 5: Wire the static pages to SeoService**

In `frontend/src/app/pages/map-home/map-home.ts`: add imports and set meta in the constructor. Add to the imports at top:

```ts
import { SeoService } from '../../seo/seo.service';
import { homeMeta } from '../../seo/route-meta';
```

Add a field near the other `inject(...)` lines:

```ts
  private seo = inject(SeoService);
```

At the **start** of the existing `constructor() {` body (before `this.fetchRanges();`), add:

```ts
    this.seo.setMeta(homeMeta());
```

In `frontend/src/app/components/route-grid/route-grid.ts`: add the same imports (`SeoService`, and `allPeaksMeta` from `'../../seo/route-meta'`), add `private seo = inject(SeoService);`, and in `ngOnInit()` add `this.seo.setMeta(allPeaksMeta());` as the first line.

In `frontend/src/app/pages/about/about.ts`: convert to set meta on construction:

```ts
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SeoService } from '../../seo/seo.service';
import { aboutMeta } from '../../seo/route-meta';

@Component({
  selector: 'app-about',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './about.html',
  styleUrl: './about.scss',
})
export class About {
  constructor() {
    inject(SeoService).setMeta(aboutMeta());
  }
}
```

In `frontend/src/app/pages/diagnostics/diagnostics.ts`: inject `SeoService` and call `setMeta(diagnosticsMeta())` in the constructor (import `diagnosticsMeta` from `'../../seo/route-meta'`). (peak-detail is wired in Task A6.)

- [ ] **Step 6: Run the suite** — `npm test` → PASS (existing specs for these components still pass; the constructor `inject(SeoService)` resolves via the root provider).

- [ ] **Step 7: Commit**

```bash
git add frontend/src/app/seo/route-meta.ts frontend/src/app/seo/route-meta.spec.ts frontend/src/app/pages/map-home/map-home.ts frontend/src/app/components/route-grid/route-grid.ts frontend/src/app/pages/about/about.ts frontend/src/app/pages/diagnostics/diagnostics.ts
git commit -m "feat(seo): per-route meta builders wired into pages"
```

## Task A6: Substantive prerendered content + per-page h1 on peak pages

Render a manifest-driven identity block **always** (independent of the API load), so the page is substantive even before/without JS. Also fix headings: today the global hero is the only `<h1>` and the peak name is an `<h2>`.

**Files:**
- Modify: `frontend/src/app/app.html` (hero `<h1>` → brand element so pages own their `<h1>`)
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.ts` (manifest-driven identity + SeoService) and `peak-detail.html` (static block + `<h1>`)
- Test: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts` (extend)

- [ ] **Step 1: Demote the global hero heading so pages own their h1**

In `frontend/src/app/app.html`, change the hero brand from an `<h1>` to a non-heading element (CSS unaffected — restyle the class if needed). Replace:

```html
      <a routerLink="/" class="hero-link">
        <h1>Big Route Weather</h1>
      </a>
```

with:

```html
      <a routerLink="/" class="hero-link">
        <span class="brand">Big Route Weather</span>
      </a>
```

If `app.scss` styles `.hero-text h1`, add/duplicate the same rules for `.hero-text .brand` so the visual is unchanged.

- [ ] **Step 2: Add manifest-driven identity + SEO to peak-detail.ts**

In `frontend/src/app/pages/peak-detail/peak-detail.ts`, add imports:

```ts
import { SeoService } from '../../seo/seo.service';
import { peakMeta } from '../../seo/route-meta';
import { getPeakBySlug, getPeaksInRange } from '../../seo/peaks-catalog';
```

Add fields after `private service = inject(RoutesService);`:

```ts
  private seo = inject(SeoService);

  // Static identity from the committed manifest — available at prerender and in
  // the browser, so the page has real, peak-specific content without the API.
  peak = computed(() => getPeakBySlug(this.slug()) ?? null);
  rangePeers = computed(() => {
    const p = this.peak();
    return p ? getPeaksInRange(p.rangeSlug, p.slug) : [];
  });
```

Replace the existing `constructor()` with one that sets SEO meta from the manifest (so it bakes in at prerender) and still loads the live detail:

```ts
  constructor() {
    effect(() => {
      const slug = this.slug();
      const p = getPeakBySlug(slug);
      if (p) untracked(() => this.seo.setMeta(peakMeta(p)));
      untracked(() => this.load(slug));
    });
  }
```

(Phase B will gate `this.load(slug)` to the browser; in Phase A it runs as before.)

- [ ] **Step 3: Add the substantive static block to peak-detail.html**

In `frontend/src/app/pages/peak-detail/peak-detail.html`, immediately after the closing `</p>` of the `.back` nav (line ~6) and **before** the `@if (loading())` block, insert the identity block (rendered whenever the slug is in the catalog):

```html
  @if (peak(); as p) {
    <header class="identity">
      <h1>{{ p.mountain }} Weather &amp; Climbing Conditions</h1>
      <p class="lede">
        Live weather forecast, summit conditions, and a climbing-route grade for
        {{ p.mountain }} ({{ p.summitElevationFt | number }}&nbsp;ft, Class {{ p.classDifficulty }})
        via the {{ p.routeName }} in the {{ p.rangeName }}.
      </p>
      <dl class="facts">
        <div><dt>Elevation</dt><dd>{{ p.summitElevationFt | number }} ft</dd></div>
        <div><dt>Standard route</dt><dd>{{ p.routeName }} (Class {{ p.classDifficulty }})</dd></div>
        <div><dt>Range</dt><dd>{{ p.rangeName }}</dd></div>
        <div><dt>Summit</dt><dd>{{ p.summitLat | number:'1.4-4' }}, {{ p.summitLon | number:'1.4-4' }}</dd></div>
      </dl>
      <p class="covers">
        This forecast covers wind, temperature, precipitation, snowpack, daylight, and air quality —
        the conditions that decide a safe summit window for climbers, mountaineers, and hikers.
      </p>
      @if (rangePeers().length > 0) {
        <nav class="range-peers" aria-label="Other peaks in the {{ p.rangeName }}">
          <span class="range-peers-label">More in the {{ p.rangeName }}:</span>
          @for (peer of rangePeers(); track peer.slug) {
            <a [routerLink]="['/peak', peer.slug]">{{ peer.mountain }}</a>
          }
        </nav>
      }
    </header>
  }
```

Then **remove** the now-duplicated `<h2>{{ d.mountain }}</h2>` and the `.route-name`/`.coords` lines from the dynamic `<header class="head">` (lines ~16-24), leaving the dynamic `.head-meta` chips (range/stale/consensus/updated) in place. The dynamic header becomes just the live chips.

- [ ] **Step 4: Extend the peak-detail spec for the static block**

In `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`, add a test that the identity block renders from the manifest **before any HTTP response** (use a real manifest slug). Pattern (adapt to the file's existing setup/imports):

```ts
  it('renders the manifest identity block (h1 + facts) before the detail loads', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'mount-whitney');
    fixture.detectChanges();

    const h1 = fixture.nativeElement.querySelector('h1');
    expect(h1.textContent).toContain('Mount Whitney');
    expect(fixture.nativeElement.querySelector('.identity .facts')).not.toBeNull();

    // The component still fires the detail request (gated to browser in Phase B).
    httpMock.expectOne('/api/routes/mount-whitney').flush({} as any);
  });
```

- [ ] **Step 5: Run the suite** — `npm test` → PASS. (If existing peak-detail specs assert the old `<h2>` header, update them to the new `<h1>` identity block.)

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/app.html frontend/src/app/app.scss frontend/src/app/pages/peak-detail/peak-detail.ts frontend/src/app/pages/peak-detail/peak-detail.html frontend/src/app/pages/peak-detail/peak-detail.spec.ts
git commit -m "feat(seo): substantive manifest-driven peak content + per-page h1"
```

## Task A7: Sitemap, robots, OG image, default head tags

**Files:**
- Create: `frontend/scripts/generate-sitemap.mjs`, `frontend/public/robots.txt`, `frontend/public/og-image.png`
- Modify: `frontend/package.json` (build hook), `frontend/src/index.html` (default tags)
- Test: `frontend/scripts/generate-sitemap.test.mjs` (run via node) — see Step 3

- [ ] **Step 1: Write the sitemap generator**

Create `frontend/scripts/generate-sitemap.mjs`:

```js
// Emits public/sitemap.xml from the committed manifest + static routes.
// Runs before the build so the file is picked up as a static asset.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const SITE_URL = 'https://bigrouteweather.com';
const here = dirname(fileURLToPath(import.meta.url));
const manifest = JSON.parse(readFileSync(join(here, '..', 'src', 'app', 'seo', 'peaks.manifest.json'), 'utf8'));

export function buildSitemapUrls(peaks) {
  const staticPaths = ['/', '/all', '/about']; // NOT /diagnostics (noindexed)
  const peakPaths = peaks.map(p => `/peak/${p.slug}`);
  return [...staticPaths, ...peakPaths].map(p => (p === '/' ? `${SITE_URL}/` : SITE_URL + p));
}

export function buildSitemapXml(urls) {
  const body = urls.map(u => `  <url><loc>${u}</loc></url>`).join('\n');
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${body}\n</urlset>\n`;
}

// Only write when run directly (not when imported by the test).
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const urls = buildSitemapUrls(manifest);
  writeFileSync(join(here, '..', 'public', 'sitemap.xml'), buildSitemapXml(urls));
  console.log(`Wrote sitemap with ${urls.length} URLs`);
}
```

- [ ] **Step 2: Add robots.txt, an OG image, default head tags, and the build hook**

Create `frontend/public/robots.txt`:

```
User-agent: *
Allow: /
Disallow: /diagnostics

Sitemap: https://bigrouteweather.com/sitemap.xml
```

Add `frontend/public/og-image.png` — a branded **1200×630** PNG with the site name (a simple solid-background branded card is fine to start; the design can be improved later without code changes). If no asset is available yet, create a minimal placeholder PNG so the file exists; flag it in the report as needing a real design.

In `frontend/src/index.html`, add default/baseline tags inside `<head>` (SeoService overrides per route; these are the fallback for any non-wired path and the prerender base). After the existing `<meta name="viewport" ...>` line add:

```html
  <meta name="description" content="Climbing and mountaineering weather forecasts and route-condition grades for big peaks.">
  <meta property="og:site_name" content="Big Route Weather">
  <meta property="og:image" content="https://bigrouteweather.com/og-image.png">
  <meta name="twitter:card" content="summary_large_image">
```

In `frontend/package.json` `scripts`, add a prebuild hook so the sitemap is regenerated on every build:

```json
    "prebuild": "node scripts/generate-sitemap.mjs",
    "seo:sitemap": "node scripts/generate-sitemap.mjs",
```

- [ ] **Step 3: Write and run the sitemap test**

Create `frontend/scripts/generate-sitemap.test.mjs`:

```js
import assert from 'node:assert/strict';
import { buildSitemapUrls, buildSitemapXml } from './generate-sitemap.mjs';

const peaks = [{ slug: 'mount-whitney' }, { slug: 'mount-rainier' }];
const urls = buildSitemapUrls(peaks);

assert.equal(urls.length, 5); // / , /all, /about + 2 peaks
assert.ok(urls.includes('https://bigrouteweather.com/'));
assert.ok(urls.includes('https://bigrouteweather.com/all'));
assert.ok(urls.includes('https://bigrouteweather.com/peak/mount-whitney'));
assert.ok(!urls.some(u => u.includes('/diagnostics')));
assert.ok(urls.every(u => u.startsWith('https://bigrouteweather.com')));

const xml = buildSitemapXml(urls);
assert.ok(xml.includes('<loc>https://bigrouteweather.com/peak/mount-rainier</loc>'));
assert.ok(xml.startsWith('<?xml'));
console.log('sitemap test OK');
```

Run: `node frontend/scripts/generate-sitemap.test.mjs`
Expected: prints `sitemap test OK`, exit 0.

- [ ] **Step 4: Generate the real sitemap and verify the build picks it up**

Run: `cd frontend && npm run seo:sitemap`
Expected: `Wrote sitemap with 127 URLs` and `frontend/public/sitemap.xml` exists.

- [ ] **Step 5: Commit**

```bash
git add frontend/scripts/generate-sitemap.mjs frontend/scripts/generate-sitemap.test.mjs frontend/public/robots.txt frontend/public/og-image.png frontend/public/sitemap.xml frontend/package.json frontend/src/index.html
git commit -m "feat(seo): sitemap generator, robots.txt, OG image, default head tags"
```

### Phase A checkpoint

Run the full suites before starting Phase B:

```bash
cd frontend && npm test                 # all green
node frontend/scripts/generate-sitemap.test.mjs
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
```

Phase A is independently shippable (client-side meta + sitemap/robots + structured data help Googlebot now). Recommended: review/PR here before Phase B.

---

# PHASE B — Prerender / SSG conversion

> Phase B turns the Phase A meta + content into static HTML for **all** crawlers and social scrapers. The riskiest piece; isolated last. Use the schematic for version-correct SSR scaffolding, then adjust to static output.

## Task B1: Add SSR scaffolding and switch to static output

**Files:**
- Create (via schematic, then adjust): `frontend/src/main.server.ts`, `frontend/src/app/app.config.server.ts`, `frontend/src/app/app.routes.server.ts`
- Modify: `frontend/angular.json`, `frontend/src/app/app.config.ts`, `frontend/package.json`
- Delete: any Express `frontend/src/server.ts` the schematic adds (not needed for static Pages)

- [ ] **Step 1: Scaffold SSR**

Run: `cd frontend && npx ng add @angular/ssr --skip-confirmation`
Expected: adds `@angular/ssr`, creates `main.server.ts`, `app.config.server.ts`, `app.routes.server.ts` (and possibly `server.ts`), and updates `angular.json` with `server`/`ssr`/`outputMode`/`prerender` keys. Commit nothing yet.

- [ ] **Step 2: Switch to static output (no runtime server)**

In `frontend/angular.json`, under `projects.frontend.architect.build.options`, ensure these keys (add/adjust; remove any `"ssr"` entry and any `"outputMode": "server"`):

```json
            "server": "src/main.server.ts",
            "outputMode": "static",
            "prerender": true,
```

Delete `frontend/src/server.ts` if the schematic created it (it's an Express runtime server; Cloudflare Pages serves static files). Remove any `"serve:ssr"`/server scripts the schematic added to `package.json`.

Ensure `frontend/src/app/app.config.server.ts` provides server rendering and merges the browser config. It should look like:

```ts
import { mergeApplicationConfig, ApplicationConfig } from '@angular/core';
import { provideServerRendering } from '@angular/ssr';
import { appConfig } from './app.config';

const serverConfig: ApplicationConfig = {
  providers: [provideServerRendering()],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
```

- [ ] **Step 3: Enable hydration**

In `frontend/src/app/app.config.ts`, add `provideClientHydration()` to the providers array:

```ts
import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideClientHydration } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideClientHydration(),
    provideHttpClient(),
    provideRouter(routes, withComponentInputBinding()),
  ],
};
```

- [ ] **Step 4: Verify the app still builds (CSR unaffected) and tests pass**

Run: `cd frontend && npm test`
Expected: PASS (hydration provider is inert under the unit-test harness).

- [ ] **Step 5: Commit**

```bash
git add frontend/angular.json frontend/package.json frontend/src/main.server.ts frontend/src/app/app.config.server.ts frontend/src/app/app.routes.server.ts frontend/src/app/app.config.ts
git rm --cached frontend/src/server.ts 2>/dev/null || true
git commit -m "build(seo): add @angular/ssr scaffolding, static output, hydration"
```

## Task B2: Prerender routes from the manifest

**Files:**
- Modify: `frontend/src/app/app.routes.server.ts`

- [ ] **Step 1: Configure prerendering for all routes incl. the 124 peaks**

Replace `frontend/src/app/app.routes.server.ts` with:

```ts
import { RenderMode, ServerRoute } from '@angular/ssr';
import { ALL_PEAKS } from './seo/peaks-catalog';

export const serverRoutes: ServerRoute[] = [
  {
    path: 'peak/:slug',
    renderMode: RenderMode.Prerender,
    getPrerenderParams: async () => ALL_PEAKS.map(p => ({ slug: p.slug })),
  },
  // Everything else (home, /all, /about, /diagnostics) prerenders as-is.
  { path: '**', renderMode: RenderMode.Prerender },
];
```

- [ ] **Step 2: Build and confirm peak pages are emitted**

Run: `cd frontend && npm run build`
Expected: build succeeds and prerenders without network errors **only after Task B3** gates data fetches. If the build hangs/errors trying to reach `/api/...`, that's expected here — proceed to B3 (which removes build-time fetches), then re-run. Do not point the build at a live API.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/app/app.routes.server.ts
git commit -m "build(seo): prerender all peak routes from the manifest"
```

## Task B3: SSR-safety — gate data fetches to the browser

Prerender runs in Node; the live forecast must load only in the browser so prerender does zero network I/O and the static HTML is the manifest-driven content.

**Files:**
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.ts`, `frontend/src/app/pages/map-home/map-home.ts`, `frontend/src/app/components/route-grid/route-grid.ts`, `frontend/src/app/pages/map-home/map-home.html` (ngSkipHydration)

- [ ] **Step 1: Gate peak-detail's load to the browser**

In `frontend/src/app/pages/peak-detail/peak-detail.ts`, add to the `@angular/core` import: `PLATFORM_ID`. Add `import { isPlatformBrowser } from '@angular/common';`. Add field `private platformId = inject(PLATFORM_ID);`. Change the constructor effect so only the SEO meta runs during prerender:

```ts
  constructor() {
    effect(() => {
      const slug = this.slug();
      const p = getPeakBySlug(slug);
      if (p) untracked(() => this.seo.setMeta(peakMeta(p)));
      if (isPlatformBrowser(this.platformId)) {
        untracked(() => this.load(slug));
      }
    });
  }
```

- [ ] **Step 2: Gate route-grid and map-home fetches to the browser**

In `frontend/src/app/components/route-grid/route-grid.ts`: add `PLATFORM_ID` to the core import, `import { isPlatformBrowser } from '@angular/common';`, `private platformId = inject(PLATFORM_ID);`, and wrap the load:

```ts
  ngOnInit() {
    this.seo.setMeta(allPeaksMeta());
    if (isPlatformBrowser(this.platformId)) this.load();
  }
```

In `frontend/src/app/pages/map-home/map-home.ts`: add `PLATFORM_ID` to the core import and `isPlatformBrowser` from `@angular/common` (already imports nothing from common — add it), `private platformId = inject(PLATFORM_ID);`, and gate the three fetches in the constructor:

```ts
    this.seo.setMeta(homeMeta());
    if (isPlatformBrowser(this.platformId)) {
      this.fetchRanges();
      this.fetchPositions();
      this.fetchRoutes();
    }
    afterNextRender(() => this.initMap());
```

(`afterNextRender` is already browser-only — leave it.)

- [ ] **Step 3: Mark the Leaflet map container ngSkipHydration**

In `frontend/src/app/pages/map-home/map-home.html`, add `ngSkipHydration` to the map container element (the `<div #mapEl ...>`), e.g. `<div #mapEl class="map" ngSkipHydration>`. Leaflet mutates this DOM manually after render; skipping hydration here prevents mismatch warnings.

- [ ] **Step 4: Run the suite**

Run: `cd frontend && npm test`
Expected: PASS. Update any spec that assumed an immediate fetch on init for map-home/route-grid — under the jsdom test platform `isPlatformBrowser` is **true**, so fetches still fire in tests and existing HTTP expectations hold. (If a spec uses a server platform override, adjust accordingly.)

- [ ] **Step 5: Build and prerender cleanly**

Run: `cd frontend && npm run build`
Expected: build + prerender succeed with **no** network calls (fetches are browser-gated). Output contains `peak/<slug>/index.html` files.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/app/pages/peak-detail/peak-detail.ts frontend/src/app/components/route-grid/route-grid.ts frontend/src/app/pages/map-home/map-home.ts frontend/src/app/pages/map-home/map-home.html
git commit -m "fix(seo): gate data fetches to the browser for clean prerender"
```

## Task B4: `_redirects` reconciliation + prerender smoke verification

**Files:**
- Modify: `frontend/public/_redirects`
- Create: `frontend/scripts/verify-prerender.mjs`
- Modify: `frontend/package.json` (`postbuild` hook)

- [ ] **Step 1: Reconcile the SPA fallback with prerendered routes**

The current `frontend/public/_redirects` is `/*  /index.html  200`, which rewrites **every** path to the SPA shell. Cloudflare Pages serves matching static assets **before** applying `_redirects`, so prerendered files (`/peak/<slug>/index.html`, `/all/index.html`, etc.) are served directly and the splat only catches unknown paths. Keep the fallback but make its intent explicit:

```
# Prerendered routes are served as static files first; this is the SPA
# fallback for any path without a prerendered file.
/*  /index.html  200
```

(No functional change needed — but verify on the Pages preview that `view-source:https://<preview>/peak/mount-whitney` shows the baked `<h1>`/title, not the bare shell. If Pages serves the shell for prerendered routes, switch the fallback to `/404.html` or remove the splat and rely on Pages' built-in SPA handling.)

- [ ] **Step 2: Write the post-build smoke check**

Create `frontend/scripts/verify-prerender.mjs`:

```js
// Asserts the build emitted real, substantive static HTML for a peak page.
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { globSync } from 'node:fs';

const here = dirname(fileURLToPath(import.meta.url));
const dist = join(here, '..', 'dist');

// Find the prerendered Whitney page wherever the builder placed it.
const matches = globSync('**/peak/mount-whitney/index.html', { cwd: dist });
if (matches.length === 0) {
  console.error('FAIL: no prerendered peak/mount-whitney/index.html under dist/');
  process.exit(1);
}
const html = readFileSync(join(dist, matches[0]), 'utf8');

const required = [
  '<title>Mount Whitney Weather Forecast',   // baked title
  'Mount Whitney Weather &amp; Climbing',     // baked h1 (escaped &)
  "Mountaineer's Route",                      // route fact
  '14,505',                                   // elevation fact
  'Sierra Nevada',                            // range fact
  'application/ld+json',                      // structured data
];
const missing = required.filter(s => !html.includes(s));
if (missing.length) {
  console.error('FAIL: prerendered Whitney page missing:', missing);
  process.exit(1);
}
console.log('verify-prerender OK:', matches[0]);
```

(If `node:fs` `globSync` is unavailable in the project's Node version, replace with a small recursive directory walk — same assertions.)

- [ ] **Step 3: Add the postbuild hook and run a full build + verify**

In `frontend/package.json` `scripts`, add:

```json
    "postbuild": "node scripts/verify-prerender.mjs",
```

Run: `cd frontend && npm run build`
Expected: build prerenders all routes, then `verify-prerender OK: .../peak/mount-whitney/index.html` prints. This proves the page ships substantive, non-thin content in static HTML.

- [ ] **Step 4: Commit**

```bash
git add frontend/public/_redirects frontend/scripts/verify-prerender.mjs frontend/package.json
git commit -m "build(seo): SPA-fallback note + prerender smoke verification"
```

### Phase B checkpoint / full verification

```bash
cd frontend && npm test
cd frontend && npm run build      # prerenders + emits sitemap + verify-prerender passes
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
dotnet test backend/RouteWeather.API.Tests/RouteWeather.API.Tests.csproj
```

Manual (needs the user's running servers / the Pages preview — do not start servers yourself):
- `view-source` on a peak preview URL shows the baked `<title>`, `<h1>`, facts, and JSON-LD.
- Google Rich Results Test validates the `Mountain` + `BreadcrumbList` JSON-LD.
- A social-card validator renders the OG card.
- `https://<preview>/sitemap.xml` lists 127 URLs; `/robots.txt` references it.

---

## Self-Review

**Spec coverage:**
- Prerender/SSG, static output, hydration → B1/B2/B3. ✓
- Generated manifest + parity test → A1/A2. ✓
- SeoService (title/desc/canonical/OG/Twitter/noindex) → A3. ✓
- Structured data (Mountain/Breadcrumb/WebSite) → A4 (breadcrumb = Home→All peaks→Peak, reconciled with no-range-hubs). ✓
- Per-route wiring incl. diagnostics noindex → A5. ✓
- Substantive prerendered content + per-page h1 + same-range internal links → A6. ✓
- Sitemap + robots + diagnostics exclusion + OG image + default tags → A7. ✓
- SSR-safety (browser-gated fetches, ngSkipHydration) → B3. ✓
- `_redirects` reconciliation + prerender smoke (asserts route/elevation/range in static HTML) → B4. ✓
- Marketing playbook → already delivered as a committed doc (out of plan scope, correctly). ✓

**Placeholder scan:** No TBD/TODO. `og-image.png` is a real asset to add (a placeholder PNG is acceptable to start, flagged). The `ng add @angular/ssr` schematic is used for version-correct SSR config, with exact end-states given for the files that matter — this is deliberate, not a hand-wave.

**Type consistency:** `SeoMeta` (A3) is consumed by `route-meta.ts` (A5) and `SeoService.setMeta`. `PeakSeo` (A1) flows through catalog → structured-data → route-meta → peak-detail. `ALL_PEAKS`/`getPeakBySlug`/`getPeaksInRange` names are consistent across catalog, peak-detail, sitemap, and `app.routes.server.ts`. Manifest field names match between the generator, the JSON, the catalog type, and the backend parity record.
