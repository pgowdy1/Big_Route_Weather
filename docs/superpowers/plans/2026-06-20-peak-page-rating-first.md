# Peak Page Rating-First Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Next-24h grade the hero of the peak page, delete the A6 boilerplate (mad-lib lede + identical "what this forecast covers" paragraph), replace the 20–57-link peer wall with one "All <Range> peaks →" link, and demote the 12/24/48h grades to a compact strip — without weakening SEO.

**Architecture:** Frontend-only change to the `peak-detail` component. Static identity (name, facts) renders from the committed manifest (`peak()`); the grade/rationale/drivers hydrate from the live `detail()`. The reused `app-grade-badge` is sized via a CSS custom property so the hero badge is large and the strip badges are small, with no change to the badge's default size elsewhere.

**Tech Stack:** Angular 21 (zoneless, signals, standalone, OnPush), Vitest + jsdom.

**Branch:** `feature/seo-foundation` (already checked out) — folds into the open PR #38, revising the A6 content before it ships.

**Spec:** `docs/superpowers/specs/2026-06-20-peak-page-rating-first-design.md`.

---

## File Structure

- `frontend/src/app/pages/peak-detail/peak-detail.ts` — add `heroWindow` (next-24h) + `gradeWord()`; drop `rangePeers`/`getPeaksInRange`.
- `frontend/src/app/pages/peak-detail/peak-detail.html` — hero block + compact strip + single range link; remove identity prose, `.head`, big window cards.
- `frontend/src/app/pages/peak-detail/peak-detail.scss` — hero/strip/kicker styles; remove `.identity` prose, `.head`, `.range-chip`, big `.windows` cards.
- `frontend/src/app/components/grade-badge/grade-badge.scss` — make size a CSS custom property (1-line, non-breaking) so the hero/strip can resize the badge. (Small, justified scope addition.)
- `frontend/src/app/pages/peak-detail/peak-detail.spec.ts` — update the 3 structure-coupled tests; add 2 (single peer link, no boilerplate).

---

## Task 1: Update the peak-detail tests to the new structure (RED)

Three existing tests assert the old structure (`.window` cards, `.range-chip`, `.identity .facts`) and must be retargeted; add two guards. The `detail()` fixture and all section tests (factors/snowpack/sky-air/forecast/per-source/404/partial) stay as-is.

**Files:**
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.spec.ts`

- [ ] **Step 1: Retarget the "three window grades" test to hero + strip**

Replace the test `it('loads detail for the slug and shows three window grades', ...)` (currently lines 25–41) with:

```ts
  it('makes the 24h grade the hero and shows all three windows in the strip', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    // Hero = the 24h grade, its rationale, and the quality word for grade B ("Good").
    expect(el.querySelector('.hero app-grade-badge')).not.toBeNull();
    expect(el.querySelector('.hero .rationale')?.textContent).toContain('24h is solid.');
    expect(el.textContent ?? '').toContain('Good');
    // Strip = all three windows.
    expect(el.querySelectorAll('.window-strip app-grade-badge').length).toBe(3);
    const text = el.textContent ?? '';
    expect(text).toContain('Next 12h');
    expect(text).toContain('Next 24h');
    expect(text).toContain('Next 48h');
  });
```

- [ ] **Step 2: Retarget the range test to the facts kicker**

Replace `it('shows the range name as a chip', ...)` (currently lines 134–148) with (the kicker shows the **manifest** range for `longs-peak`, which is "Colorado 14ers" — so the per-test `data.rangeName` override is dropped):

```ts
  it('shows the range in the facts kicker', async () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'longs-peak');
    fixture.detectChanges();

    httpMock.expectOne('/api/routes/longs-peak').flush(detail());
    await fixture.whenStable();
    fixture.detectChanges();

    const facts = (fixture.nativeElement as HTMLElement).querySelector('.hero .facts');
    expect(facts?.textContent).toContain('Colorado 14ers');
  });
```

- [ ] **Step 3: Retarget the identity test + add the no-boilerplate and single-link guards**

Replace `it('renders the manifest identity block (h1 + facts) before the detail loads', ...)` (currently lines 274–285) with these **two** tests:

```ts
  it('renders the hero identity (h1 + facts) before detail loads, with no boilerplate', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'mount-whitney');
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h1')?.textContent).toContain('Mount Whitney');
    expect(el.querySelector('.hero .facts')).not.toBeNull();
    // A6 boilerplate is gone.
    expect(el.querySelector('.lede')).toBeNull();
    expect(el.querySelector('.covers')).toBeNull();
    expect(el.querySelector('.range-peers')).toBeNull();

    httpMock.expectOne('/api/routes/mount-whitney').flush({} as RouteDetail);
  });

  it('shows a single "All <range> peaks" link, not a peer wall', () => {
    const fixture = TestBed.createComponent(PeakDetail);
    fixture.componentRef.setInput('slug', 'mount-whitney');
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const links = el.querySelectorAll('.range-link a');
    expect(links.length).toBe(1);
    expect(links[0].textContent).toContain('Sierra Nevada'); // Whitney's manifest range

    httpMock.expectOne('/api/routes/mount-whitney').flush({} as RouteDetail);
  });
```

- [ ] **Step 4: Run the suite to confirm RED**

Run (from `frontend/`): `npm test`
Expected: FAIL — the rewritten tests reference `.hero`, `.window-strip`, `.range-link` which don't exist yet (and `.hero .facts` is null because the markup is still the old `.identity`). The unchanged section tests still pass.

- [ ] **Step 5: Commit the RED tests**

```bash
git add frontend/src/app/pages/peak-detail/peak-detail.spec.ts
git commit -m "test(peak): retarget specs to rating-first hero + strip (red)"
```

---

## Task 2: Implement the rating-first redesign (GREEN)

**Files:**
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.ts`
- Modify: `frontend/src/app/components/grade-badge/grade-badge.scss`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.html`
- Modify: `frontend/src/app/pages/peak-detail/peak-detail.scss`

- [ ] **Step 1: Component logic — `peak-detail.ts`**

(a) Update the model import (line 9) to include `Grade`:

```ts
import { FactorScore, Grade, RouteDetail, WindowGrade } from '../../models/route-conditions';
```

(b) Update the seo-catalog import (the A6 line `import { getPeakBySlug, getPeaksInRange } from '../../seo/peaks-catalog';`) to drop the now-unused helper:

```ts
import { getPeakBySlug } from '../../seo/peaks-catalog';
```

(c) Delete the `rangePeers` computed (the A6 block):

```ts
  rangePeers = computed(() => {
    const p = this.peak();
    return p ? getPeaksInRange(p.rangeSlug, p.slug) : [];
  });
```

(d) Add a `heroWindow` computed next to `peak` (the 24h window drives the hero), and a `gradeWord` helper (used by the hero label). Place `heroWindow` after the `peak` computed, and `gradeWord` as a public method near `aqiCategory`:

```ts
  heroWindow = computed<WindowGrade | null>(() => this.detail()?.windowGrades?.next24h ?? null);
```

```ts
  gradeWord(grade: Grade | null): string {
    switch (grade) {
      case 'A': return 'Excellent';
      case 'B': return 'Good';
      case 'C': return 'Fair';
      case 'D': return 'Poor';
      case 'F': return 'Avoid';
      default: return 'Pending';
    }
  }
```

Leave `peak`, `windows`, `displayedForecast`, `activeFactors`, `lastUpdatedLabel`, the constructor effect, and everything else unchanged.

- [ ] **Step 2: Make the grade badge resizable — `grade-badge.scss`**

So the hero badge can be large and strip badges small without `::ng-deep`, drive size from CSS custom properties (defaults keep every existing usage pixel-identical). Replace the `.badge` rule's `width`/`height`/`font-size` lines:

```scss
.badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--grade-badge-size, 3rem);
  height: var(--grade-badge-size, 3rem);
  border-radius: var(--grade-badge-radius, 0.75rem);
  font-weight: 700;
  font-size: var(--grade-badge-font, 1.5rem);
  color: #fff;
  font-family: 'Inter', system-ui, sans-serif;
}
```

(The `.badge-a`…`.badge-unknown` color rules stay unchanged.)

- [ ] **Step 3: Template — `peak-detail.html`**

Make three edits; the factors/snowpack/sky-air/forecast/per-source/sources markup stays **verbatim**.

**(a)** Replace the `.identity` block (current lines 8–35, the whole `@if (peak(); as p) { <header class="identity">…</header> }`) with the hero:

```html
  @if (peak(); as p) {
    <header class="hero">
      <div class="hero-grade">
        @if (heroWindow(); as w) {
          <app-grade-badge [grade]="w.grade" />
        } @else {
          <span class="grade-skeleton" aria-hidden="true"></span>
        }
      </div>
      <div class="hero-body">
        <h1>{{ p.mountain }} <span class="h1-tail">Weather &amp; Climbing Conditions</span></h1>
        <p class="facts">
          <b>{{ p.summitElevationFt | number }} ft</b><span class="dot">·</span>{{ p.routeName }}<span class="dot">·</span>Class {{ p.classDifficulty }}<span class="dot">·</span>{{ p.rangeName }}
        </p>
        @if (heroWindow(); as w) {
          <p class="hero-win">Next 24h · <span [class]="'q grade-' + (w.grade ? w.grade.toLowerCase() : 'x')">{{ gradeWord(w.grade) }}</span></p>
          <p class="rationale">{{ w.rationale }}</p>
          @if (w.drivers.length > 0) {
            <ul class="drivers">
              @for (driver of w.drivers; track driver.label) {
                <li [class]="'pill pill-' + driver.severity">{{ driver.label }}</li>
              }
            </ul>
          }
        } @else if (loading()) {
          <p class="hero-win muted">Loading conditions…</p>
        }
        <p class="hero-meta">
          @if (detail()?.isStale) { <span class="stale-chip">Stale data</span> }
          @if (detail()?.consensus; as c) { <app-consensus-badge [consensus]="c" /> }
          @if (lastUpdatedLabel(); as label) { <span class="updated">Updated {{ label }}</span> }
        </p>
      </div>
    </header>
  }
```

**(b)** Replace the status/head/windows region (current lines 37–86 — the `@if (loading()) … @else if (detail(); as d) { <header class="head">…</header> … <section class="windows">…</section>` down to the end of the windows `</section>`, i.e. everything up to but **not including** the `@if (activeFactors()…` factors section) with:

```html
  @if (notFound()) {
    @if (peak()) {
      <p class="status error">Conditions are unavailable for this peak right now.</p>
    } @else {
      <p class="status error">Peak not found.</p>
    }
  } @else if (error()) {
    <p class="status error">{{ error() }}</p>
  }

  @if (detail(); as d) {
    @if (windows().length > 0) {
      <section class="window-strip">
        @for (w of windows(); track w.key) {
          <div class="ws">
            <app-grade-badge [grade]="w.data.grade" />
            <div class="ws-meta">
              <span class="ws-label">{{ w.label }}</span>
              @if (w.data.hoursCovered < w.target) {
                <span class="ws-partial">partial — {{ w.data.hoursCovered }}h</span>
              }
            </div>
          </div>
        }
      </section>
    }
```

This **reopens** the `@if (detail(); as d) {` block, so the existing factors → sources markup (current lines 88–247) now follows directly as that block's body, unchanged. Keep its closing `}` (current line 248).

**(c)** Immediately before the final `</section>` (closing `.peak-detail`), add the single range link:

```html
  @if (peak(); as p) {
    <nav class="range-link" aria-label="More peaks">
      <a [routerLink]="['/all']">All {{ p.rangeName }} peaks →</a>
    </nav>
  }
```

- [ ] **Step 4: Styles — `peak-detail.scss`**

(a) Inside `.peak-detail`, **replace** the `.identity { … }` block and the `.head { … }`, `.range-chip { … }`, `.head-meta`, `.updated`, `.status.inline` rules (current lines 28–85) with the hero + kicker styles below. **Keep** `.back`, `.back-sep`, `.status`, `.stale-chip` (still used in the hero meta), and `.updated` (re-added below):

```scss
  .hero {
    display: flex;
    gap: 1.1rem;
    align-items: flex-start;

    .hero-grade { flex: 0 0 auto; --grade-badge-size: 5.25rem; --grade-badge-font: 2.9rem; }
    .grade-skeleton {
      display: block; width: 5.25rem; height: 5.25rem; border-radius: 0.75rem;
      background: #1a2632; border: 1px solid #2c3e50;
    }
    .hero-body { min-width: 0; }

    h1 { margin: 0; font-size: 1.75rem; line-height: 1.12; font-weight: 800; }
    .h1-tail { font-weight: 500; color: #8aa0b4; }

    .facts { margin: 0.4rem 0 0; font-size: 0.8rem; color: #8aa0b4; }
    .facts b { color: #cfd8dc; font-weight: 600; }
    .facts .dot { margin: 0 0.5rem; color: #41566b; }

    .hero-win { margin: 0.7rem 0 0; font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.06em; color: #8aa0b4; }
    .hero-win .q { font-weight: 700; }
    .hero-win.muted { text-transform: none; letter-spacing: 0; font-style: italic; color: #92a4b5; }
    .q.grade-a { color: #81c784; } .q.grade-b { color: #aed581; }
    .q.grade-c { color: #ffd54f; } .q.grade-d { color: #ffb74d; } .q.grade-f { color: #ef9a9a; }

    .rationale { margin: 0.45rem 0 0; color: #cfd8dc; font-size: 0.95rem; line-height: 1.4; }
    .drivers { list-style: none; padding: 0; margin: 0.6rem 0 0; display: flex; flex-wrap: wrap; gap: 0.3rem; }
    .hero-meta { margin: 0.7rem 0 0; display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; min-height: 1px; }
    .updated { font-size: 0.75rem; color: #8aa0b4; }
  }

  .window-strip {
    display: flex;
    gap: 0.6rem;
    .ws {
      flex: 1; display: flex; align-items: center; gap: 0.55rem;
      background: #11202c; border: 1px solid #2c3e50; border-radius: 0.6rem; padding: 0.55rem 0.65rem;
      --grade-badge-size: 1.9rem; --grade-badge-font: 0.95rem; --grade-badge-radius: 0.45rem;
    }
    .ws-meta { display: flex; flex-direction: column; gap: 0.1rem; min-width: 0; }
    .ws-label { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.05em; color: #8aa0b4; }
    .ws-partial { font-size: 0.66rem; color: #ffd54f; }
  }

  .range-link {
    a { color: #5fa8d8; text-decoration: none; font-size: 0.85rem; &:hover { color: #cfe5f5; } }
  }
```

(b) **Delete** the old `.windows { … }` block (current lines 88–137) **except** move its `.pill` rules to top level (the hero drivers reuse them). Add this near the bottom of the file (outside `.peak-detail`):

```scss
.pill { font-size: 0.7rem; padding: 0.15rem 0.5rem; border-radius: 999px; border: 1px solid transparent; }
.pill-positive { background: #1b3a1b; color: #a5d6a7; border-color: #2e7d32; }
.pill-neutral  { background: #2a3340; color: #b0bec5; border-color: #455a64; }
.pill-negative { background: #4a1f1f; color: #ef9a9a; border-color: #c62828; }
```

(c) Update the responsive rule at the bottom (current `@media (max-width: 720px) { .windows { grid-template-columns: 1fr; } }`) to stack the hero and wrap the strip:

```scss
@media (max-width: 600px) {
  .peak-detail .hero { flex-direction: column; }
  .peak-detail .window-strip { flex-wrap: wrap; }
}
```

Leave the `.factors`, `.snowpack`/`.sky-air`, `.forecast`, `.forecast-toggle`, AQI, and `.sources` styles unchanged.

- [ ] **Step 5: Run the suite to confirm GREEN**

Run (from `frontend/`): `npm test`
Expected: PASS — all peak-detail tests green (hero + strip + kicker + single link), and every unchanged section test (forecast/factors/sky-air/per-source/404/partial) still passes.

- [ ] **Step 6: Build and check the style budget**

Run (from `frontend/`): `npm run build`
Expected: builds; `peak-detail.scss` stays under the 6 kB component-style warning budget (the removals roughly offset the additions). If it warns, compact the new rules before proceeding — do not bump the budget.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/app/pages/peak-detail/peak-detail.ts frontend/src/app/components/grade-badge/grade-badge.scss frontend/src/app/pages/peak-detail/peak-detail.html frontend/src/app/pages/peak-detail/peak-detail.scss
git commit -m "feat(peak): rating-first hero + compact window strip; drop A6 boilerplate"
```

---

## Self-Review

**Spec coverage:**
- Remove `.lede`/`.covers` boilerplate → Task 2 Step 3a (replaced by hero) + spec test guards (Task 1 Step 3). ✓
- Hero = big Next-24h grade + rationale + drivers + facts kicker + meta line → Task 2 Steps 1/3a/4a. ✓
- Compact 12/24/48h strip → Task 2 Steps 3b/4a. ✓
- Single "All <Range> peaks →" link, peer wall removed → Task 2 Step 3c + guard test (Task 1 Step 3). ✓
- Loading/notFound states (manifest identity always; placeholder badge; "Peak not found" only when not in manifest) → Task 2 Step 3a/3b; 404 test still passes ('missing' is not in the manifest → `peak()` null → "Peak not found"). ✓
- SEO posture: route/elevation/range stay in the kicker (Phase B smoke guard holds); title/meta/JSON-LD untouched. ✓ (no `SeoService` change needed.)
- Grade-badge sizing without `::ng-deep` → Task 2 Step 2 (CSS custom property, default-preserving). ✓

**Placeholder scan:** No TBD/TODO. "Keep verbatim" instructions reference exact current line ranges of unchanged markup — precise, not vague.

**Type consistency:** `heroWindow` is typed `WindowGrade | null` matching `windowGrades.next24h`; `gradeWord(grade: Grade | null)` matches `WindowGrade.grade`'s type; `Grade` is added to the model import. The hero reuses `app-grade-badge [grade]` (same input as the old windows). `--grade-badge-size`/`--grade-badge-font`/`--grade-badge-radius` custom-property names match between `grade-badge.scss` (consumers) and `peak-detail.scss` (setters). The `.window-strip`/`.hero`/`.range-link`/`.hero .facts`/`.hero .rationale` selectors used in the tests match the markup.
