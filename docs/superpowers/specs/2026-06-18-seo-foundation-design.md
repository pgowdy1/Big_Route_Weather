# Spec: SEO foundation for bigrouteweather.com

**Date:** 2026-06-18
**Branch:** `feature/seo-foundation` (based off `dev`)
**Layers:** Frontend (prerendering + SEO metadata + structured data + sitemap/robots), a committed SEO manifest, one backend parity test, and a committed marketing playbook doc.
**Scope:** Large but cohesive — one SEO subsystem. Internally phased (metadata layer first, prerender conversion second) to de-risk.

## Goal & thesis

The site has **124 content-rich peak pages** (`/peak/:slug`) that are currently invisible to search and unshareable on social, because it's a **client-rendered Angular SPA** whose `index.html` is an empty `<app-root>` with a single static title and no meta/canonical/OG/structured-data/sitemap/robots.

The strategy: turn each peak page into a **crawlable, self-describing long-tail landing page** targeting high-intent queries like *"Mount Whitney weather"* / *"Mount Rainier climbing conditions"* / *"summit forecast"*, and make every page readable by search engines **and** social scrapers. The content already exists; we're building the discoverability surface.

**Canonical base URL:** `https://bigrouteweather.com` (from `backend/fly.toml` `FrontendOrigin`).

## Keyword architecture (decided)

- **Per-peak pages → long-tail (the engine).** Title pattern: `"<Mountain> Weather Forecast & Climbing Conditions | Big Route Weather"`. Description weaves the community/activity vocabulary **naturally and accurately**: *"Current forecast, summit conditions, and a route grade for <Mountain> (<elev> ft, Class <class>) via the <Route> in the <Range> — wind, temperature, precipitation, and snowpack for climbers, mountaineers, and hikers."*
- **Site level (home/about) → broad positioning.** Target the category: *"climbing & mountaineering weather forecast,"* *"alpine conditions for climbers, hikers, and trail runners."*
- **Explicitly NOT chasing head terms** ("alpine", "rock climbing") on peak pages, and **no keyword stuffing** — each page reads naturally for a human. (Range hub pages were considered and rejected: per-peak search intent dominates range-level terms, `/all` already serves as the crawl hub, and hubs add per-range maintenance as the catalog grows.)

## Non-goals (future work, noted explicitly)

- **Range hub pages** (`/range/:slug`) — rejected for this and the foreseeable scope.
- **Per-peak dynamic OG images** — `og:image` is architected as a per-page value so this is a clean future drop-in; not built now (zero search benefit, back-loaded social benefit).
- **SSR with live weather in the HTML** — prerender bakes static identity content; live data hydrates client-side.
- **Blog / guide content engine.**

## Architecture

Convert the SPA to **build-time prerendering (SSG)** so every route emits a static HTML file with full content + per-page head tags, deployed as static files on Cloudflare Pages (no server runtime). Layer a per-route SEO metadata service, JSON-LD structured data, a committed peaks manifest, and generated `sitemap.xml` / `robots.txt` on top. Live weather is fetched client-side after hydration (unchanged proxy data flow).

### Components / units

1. **Prerender setup (build-time only)** — `frontend/`
   - Add `@angular/ssr`; create `main.server.ts`, `app.config.server.ts`, and `app.routes.server.ts`. The server bundle is used **only at build time** for prerendering.
   - `app.routes.server.ts`: `/`, `/all`, `/about` → `RenderMode.Prerender`; `peak/:slug` → `RenderMode.Prerender` with `getPrerenderParams` returning `{ slug }` for every manifest entry; `/diagnostics` is kept out of the index via `noindex` + sitemap exclusion (its render mode is an implementation detail — default: prerender it too, since `noindex` already excludes it).
   - `angular.json`: enable the application builder's `server`/`ssr`/`prerender` options with `outputMode: 'static'` so the deploy stays static HTML. Enable `provideClientHydration()` in `app.config.ts`.
   - Output: `peak/<slug>/index.html` × 124 plus `/`, `/all`, `/about`.

2. **Peaks SEO manifest (generated; single source of truth = the seeder)** — `frontend/src/app/seo/peaks.manifest.json`
   - Committed array of `{ slug, mountain, routeName, summitElevationFt, classDifficulty, rangeName, rangeSlug, summitLat, summitLon }` × 124. JSON (not TS) so the backend test can read it cross-language.
   - **Generated, never hand-edited.** `frontend/scripts/generate-peaks-manifest.mjs` (run via `npm run seo:manifest`) fetches `/api/routes` from the running API — which already returns every field above — maps to the manifest shape (dropping live fields like `grade`), and writes the JSON. The committed output keeps the build deterministic and offline (no network at build time); regeneration is a dev-time action, not a build-time dependency.
   - **Adding a peak stays a one-place edit:** add it to `RouteSeeder.cs` (exactly as today) → `npm run seo:manifest` → commit the regenerated JSON. Descriptions/titles/tags are templated from these fields, so there is **no per-peak prose to write**. The backend parity test still guards against a stale or forgotten regenerate (CI fails if the committed manifest ≠ the seeder catalog).
   - Consumed by: the prerender route params, `SeoService` (peak meta), the structured-data builder, the sitemap generator, and `peak-detail` (to render static identity content at prerender without the API).

3. **SeoService** — `frontend/src/app/seo/seo.service.ts`
   - Sets per-route `<title>` + `<meta name="description">` (Angular `Title`/`Meta`), `<link rel="canonical">` (absolute), Open Graph (`og:title/description/url/image/type/site_name`), and Twitter card tags (`summary_large_image`). Manipulates `<head>` via the injected `DOCUMENT` token so it works in **both** prerender (server DOM) and the browser (SPA navigation updates tags).
   - `og:image` is a **per-page value** that currently resolves to a single static `https://bigrouteweather.com/og-image.png`; the per-page parameter means per-peak images later need no rearchitecting.
   - Input: a small `SeoMeta` object built per route — static config for home/all/about/diagnostics; derived from the manifest entry for peak pages.

4. **Structured data (JSON-LD)** — `frontend/src/app/seo/structured-data.ts` + injected by `SeoService`
   - Peak pages: schema.org **`Mountain`** (`name`, `elevation` in feet→meters or as `QuantitativeValue`, `geo` `GeoCoordinates` from lat/lon, `containedInPlace` = the range as a `Place`) **+ `BreadcrumbList`** (Home → Range → Peak).
   - Site-wide: **`WebSite`** (`name`, `url`).
   - Injected as `<script type="application/ld+json">` via `DOCUMENT` (works in prerender + browser). No weather structured data (no suitable schema.org type — not faking it).

5. **Sitemap + robots** — `frontend/scripts/generate-sitemap.mjs`, `frontend/public/robots.txt`
   - Build step (npm `prebuild`/`postbuild`) reads the manifest + the static route list, writes `sitemap.xml` (127 URLs: `/`, `/all`, `/about`, 124 peaks; **excludes** `/diagnostics`) into the build output. Deterministic (manifest is committed).
   - `robots.txt` (static in `public/`): `User-agent: *`, `Allow: /`, `Disallow: /diagnostics`, `Sitemap: https://bigrouteweather.com/sitemap.xml`.

6. **`/diagnostics` exclusion** — `noindex` meta (via `SeoService`), `Disallow` in robots, and excluded from the sitemap. (Render mode is irrelevant to indexing once `noindex` is set.)

7. **On-page semantics & internal linking** — audit `peak-detail`, `map-home`, `route-grid`, `about`
   - One descriptive `<h1>` per page (peak: `"<Mountain> Weather & Climbing Conditions"`), sensible heading order, descriptive link text, image `alt`. `/all` remains the hub linking every peak; peak pages link to their range's peers via the existing nav. Render the **indexable identity content** (name, elevation, route, range, a short descriptive paragraph) from the manifest at prerender — independent of the live API.

### Data flow

- **Build:** manifest → prerender params → Angular renders each route to static HTML (`SeoService` sets head tags + JSON-LD during render; identity content from manifest) → sitemap script writes `sitemap.xml` → static output → Cloudflare Pages deploys files.
- **Runtime:** request `/peak/mount-whitney` → Pages serves prerendered `peak/mount-whitney/index.html` (full content + meta) → Angular hydrates → live weather fetched client-side via the existing Pages Function `/api` proxy → grade/forecast update in place.

## Error handling / SSR-safety (the main implementation risk)

Prerendering runs in Node, so browser-only code must not execute during render:

- **Gate data fetching to the browser.** Components fetch live weather via `HttpClient` in init; wrap those triggers in `isPlatformBrowser(platformId)` so **no API calls happen at build time**. Prerendered HTML shows static identity content (from manifest) + loading placeholders for the dynamic weather/grade sections; the browser hydrates and fetches. The indexable content never depends on the API.
- **Guard browser globals.** Audit every `window`/`document`/`localStorage` access. `map-home` uses `afterNextRender` + dynamic `loadLeaflet()` (browser-only ✓) and `window.innerWidth`/`window.location.reload` inside those browser-only paths (✓); confirm nothing touches browser globals at construction. Mark the Leaflet map container `ngSkipHydration` so manual DOM map mutation doesn't trip hydration.
- **`_redirects` reconciliation.** Today `/*  /index.html  200` rewrites every path. Cloudflare Pages serves real static assets before applying `_redirects`, so prerendered routes win and the splat becomes the SPA fallback for unknown paths only. Verify `/peak/<slug>` resolves to its prerendered file; tighten the rule if needed.

## Affected files

**Added:**
- `frontend/src/main.server.ts`, `frontend/src/app/app.config.server.ts`, `frontend/src/app/app.routes.server.ts`
- `frontend/src/app/seo/peaks.manifest.json` (generated by `seo:manifest`, committed)
- `frontend/src/app/seo/seo.service.ts`, `frontend/src/app/seo/structured-data.ts`, `frontend/src/app/seo/seo.types.ts`
- `frontend/scripts/generate-peaks-manifest.mjs` (the `npm run seo:manifest` generator), `frontend/scripts/generate-sitemap.mjs`
- `frontend/public/robots.txt`, `frontend/public/og-image.png`
- `frontend/src/app/seo/*.spec.ts` (SeoService, structured-data, manifest)
- `backend/RouteWeather.Core.Tests/Seo/ManifestParityTests.cs`
- `docs/superpowers/specs/2026-06-18-seo-marketing-playbook.md` (the marketing deliverable)

**Modified:**
- `frontend/angular.json` (server/ssr/prerender + outputMode static)
- `frontend/src/app/app.config.ts` (`provideClientHydration()`)
- `frontend/package.json` (`@angular/ssr`, `seo:manifest` + sitemap build scripts)
- `frontend/src/index.html` (baseline default meta/OG that SeoService overrides per route)
- `frontend/public/_redirects` (reconcile with prerendered routes)
- `frontend/src/app/pages/peak-detail/*`, `pages/map-home/*`, `components/route-grid/*`, `pages/about/*` (SSR-safety guards, `SeoService` wiring, heading/`alt`/link-text semantics)

**Not changed:** backend API/grading, the data model, the `/api` proxy.

## Internal phasing (sequenced in the plan to de-risk)

- **Phase A — metadata layer (no prerender):** manifest + parity test, `SeoService`, structured data, sitemap + robots, on-page semantics, `og:image` static. Works client-side immediately (helps Googlebot; sitemap/robots help all crawlers). Low risk.
- **Phase B — prerender conversion:** `@angular/ssr` wiring, SSR-safety guards, hydration, `_redirects`, build verification. The crawlability + social win. Higher risk; isolated to last.

## Test plan

- **Frontend (`npm test`):**
  - `SeoService`: sets correct title/description/canonical/OG/Twitter for a sample peak and for static pages; emits `noindex` for `/diagnostics`; uses absolute canonical under the canonical base.
  - structured-data: valid `Mountain` + `BreadcrumbList` JSON-LD shape for a sample manifest entry; `WebSite` site-wide.
  - manifest: 124 entries, unique slugs, all required fields non-empty.
- **Backend (`dotnet test`):** `ManifestParityTests` reads `frontend/src/app/seo/peaks.manifest.json` (resolved by walking up from the test base dir to the repo root) and asserts its slug set **and** `mountain`/`summitElevationFt` per slug equal the `RouteSeeder` catalog — so a future peak PR that forgets the manifest fails CI.
- **Sitemap:** unit test on the generator → 127 URLs, includes home/all/about + all peaks, excludes `/diagnostics`, all absolute under the canonical base.
- **Prerender smoke (Phase B):** post-build verify script asserts the build output contains `peak/mount-whitney/index.html` and that it includes the expected `<title>` and `<h1>` text (proves content is actually in the static HTML).
- **Regression:** existing `dotnet test` (Core 193, API 53) and `npm test` stay green; update `map-home`/`peak-detail` specs for the SSR-safety guards.

## Verification commands

```bash
cd frontend && npm run seo:manifest   # regenerate peaks.manifest.json from the running API (after adding peaks)
cd frontend && npm test
cd frontend && npm run build   # prerenders all routes; emits sitemap.xml
dotnet test backend/RouteWeather.Core.Tests/RouteWeather.Core.Tests.csproj
# After build, confirm a peak page is real static HTML:
#   grep -l "Mount Whitney Weather" frontend/dist/**/peak/mount-whitney/index.html
# After deploy: validate a peak URL with Google Rich Results Test + a social card validator.
```

## Marketing deliverable

The free-traffic playbook is written as a committed doc: `docs/superpowers/specs/2026-06-18-seo-marketing-playbook.md` — Search Console/Bing setup + sitemap submission, the per-peak long-tail strategy, value-first community sharing, tool-roundup listings, and a measure-and-iterate loop. It is advice/process (not code), so it is delivered as a doc rather than plan tasks.

## Branching

Base off `dev`. PR targets `dev`; verify on the Pages preview (check a peak page's `view-source` for baked content + meta, and a social-card validator); then `dev` → `main`.
