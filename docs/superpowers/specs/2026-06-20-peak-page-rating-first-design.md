# Spec: Peak page — rating-first redesign

**Date:** 2026-06-20
**Branch:** `feature/seo-foundation` (folds into the open PR #38, revising the Task A6 content before it ships)
**Layer:** Frontend only — `peak-detail` template/styles/spec. No backend, no new SEO services (reuses the A1–A6 manifest, `SeoService`, and JSON-LD).
**Scope:** Small, focused — one page's layout + a content trim.

## Problem

The Task A6 "substantive content" block on the peak page reads as copy-paste and buries the grade:

1. `.lede` — a mad-lib sentence with the same skeleton on all 124 pages.
2. `.covers` — *byte-identical* on every page ("This forecast covers wind, temperature…"). Near-duplicate boilerplate: it doesn't help SEO and slightly dilutes site quality.
3. `.range-peers` — links to **every** peer in the range (57 for Colorado 14ers, ~20 for the Sierra) → a crowded wall.
4. The grade (the product's whole point) renders *below* all of the above.

The goal: make the rating the hero, cut the boilerplate, and keep SEO strong through non-text means.

## SEO rationale (why cutting text is safe — net positive)

The visible prose was the *wrong kind* of substance. The real SEO signal lives where it doesn't clutter the UI:
- **`<title>` + meta description** — per-peak, keyword-rich, SERP-facing, already set by `peakMeta` (Task A5).
- **`Mountain` + `BreadcrumbList` JSON-LD** — elevation, geo, route, hierarchy conveyed invisibly (Task A4).
- **Concise unique facts** — elevation/route/class/range, retained as a one-line kicker.
- **The JS-rendered real data** — grade, per-window rationale, factors, forecast — genuinely unique content Googlebot sees after render.

Removing the identical `.covers` boilerplate is a **net SEO improvement** (kills near-duplicate content). Phase B's prerender smoke test asserts route/elevation/range appear in the static HTML — those stay in the kicker, so that guard still holds.

## Design

Replace the A6 `.identity` block + the three large `.windows` cards with a **rating-first hero + a compact window strip**. Supporting sections are unchanged.

### Hero grade block (the focal point)
- `<h1>`: `"<Mountain> Weather & Climbing Conditions"` — keyword h1 retained; the peak name is the visually emphasized part.
- A **large Next-24h grade badge** (the existing `app-grade-badge`, scaled up) as the focal element, with a `Next 24h · <quality>` label.
- The **Next-24h rationale** + **driver chips** (from `windowGrades.next24h`) — unique per peak/conditions; the page's real content.
- A slim **facts kicker** row: `14,505 ft · Mountaineer's Route · Class 3 · Sierra Nevada` (from the manifest `peak()` — always present).
- A subtle **meta line**: `Updated <ago>` · stale chip (if stale) · `app-consensus-badge` (the chips currently in `.head` move here; the duplicate range-chip is dropped since range is in the kicker).

**Data sources:** identity + facts come from `peak()` (manifest — render immediately, even pre-data and at prerender). Grade/rationale/drivers/meta come from the live `detail()` and hydrate in.

### Secondary window strip
- A compact row of **Next 12h / 24h / 48h** grade badges + labels below the hero (full breakdown retained, de-emphasized). Replaces the three large `.windows` cards. Per-window rationale/drivers for 12h/48h are not shown in v1 (the 24h rationale lives in the hero); the data remains available. *(A future enhancement could let tapping a strip window swap the hero — out of scope here.)*

### Supporting detail (unchanged)
Factor breakdown, Snowpack, Sky & air, Hourly forecast, Per-source forecast, Sources footer — exactly as today, below the strip.

### Internal linking
Replace the `.range-peers` wall with a **single** `<a routerLink="/all">All <Range> peaks →</a>`. `/all` is the crawl hub (links every peak) and the breadcrumb JSON-LD encodes hierarchy, so one link is ample for SEO and clean for UX. The `rangePeers` computed + the now-unused `getPeaksInRange` import are removed (leave `getPeaksInRange` in the catalog only if another consumer needs it).

### Loading / prerender / error states
- Hero name + facts render instantly from `peak()` — the page is never a bare spinner.
- Grade area: show a **graceful placeholder badge** while `loading()` and no `detail()` yet; render the real badge/rationale/drivers once `detail()` resolves.
- `notFound()` (slug reaches the API but 404s) → keep the static identity + a quiet "conditions unavailable" note rather than blanking the page.
- Slug absent from the manifest (`peak()` is null) → existing not-found behavior.

## Affected files

- `frontend/src/app/pages/peak-detail/peak-detail.html` — replace `.identity` + `.windows`; add hero block + strip + single peer link; move `.head` meta chips into the hero meta line.
- `frontend/src/app/pages/peak-detail/peak-detail.scss` — hero + strip + kicker styles; remove `.lede`/`.covers`/`.facts` (dl) / `.range-peers` / old `.windows` card styles. Stay within the 6 kB component-style budget (removals roughly offset additions).
- `frontend/src/app/pages/peak-detail/peak-detail.ts` — add a `heroWindow` computed (`detail()?.windowGrades?.next24h`); drop the `rangePeers` computed and the `getPeaksInRange` import (no longer used). `peak()`, `windows()`, SEO wiring unchanged.
- `frontend/src/app/pages/peak-detail/peak-detail.spec.ts` — update: h1 still contains the mountain; facts kicker present; **no** `.lede`/`.covers`; exactly **one** range link (not N); hero grade renders after `detail()` flush; identity (h1 + facts) still renders before the HTTP response.

**Not changed:** `SeoService`, `route-meta`/`peakMeta`, structured-data, the manifest, backend. (The descriptive text was always in the meta description, not the body — nothing to move.)

## Testing

- `npm test` green, with `peak-detail.spec.ts` updated to the new structure (assertions above).
- Manual: a peak page leads with the grade as the focal point; no boilerplate paragraphs; one "All <Range> peaks →" link; supporting sections intact; the page still shows identity + facts before live data arrives.

## Sequencing

Fold into PR #38 on `feature/seo-foundation` (revises A6's content so the boilerplate version never ships). Frontend-only; works in the current client-rendered Phase A and carries unchanged into Phase B (the hero's static identity/facts are exactly what prerender needs).
