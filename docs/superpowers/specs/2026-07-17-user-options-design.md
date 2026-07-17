# User Options — Design Spec

**Date:** 2026-07-17
**Status:** Approved (brainstorming session with visual companion)
**Scope:** Frontend only. No API, model, warmer, or database changes.

## Overview

Give visitors control over how the site displays information: measurement units, time format, and color theme. International visitors get sensible metric defaults automatically; everyone else keeps today's imperial/dark experience untouched. Settings apply instantly and persist per-device.

## Decisions (locked during brainstorming)

| Decision | Choice |
|---|---|
| Units model | Hybrid — per-measurement state with `Imperial` / `Metric` quick-set preset buttons |
| First-visit defaults | Units auto-detect from browser locale (US → imperial, other region → metric); theme defaults **Dark** regardless of OS |
| Settings UI | Gear icon in the header nav opening a dropdown popover; below 640px the same DOM renders as a full-width sheet under the header |
| Theme control | `Dark / Light / System` — Dark is default; Light and System are explicit opt-ins |
| Persistence | `localStorage`, written only when the user changes a setting |

### The settings menu

| Setting | Choices | Default |
|---|---|---|
| Quick preset | `Imperial` / `Metric` buttons (batch-set the six unit fields) | — |
| Temperature | °F / °C | locale |
| Wind speed | mph / km/h / m/s | locale (mph or km/h) |
| Elevation | ft / m | locale |
| Snow depth & SWE | in / cm | locale |
| Visibility | mi / km | locale |
| Time format | 12h / 24h | locale |
| Theme | Dark / Light / System | Dark |

Preset button highlight is computed: all-imperial → `Imperial` lit, all-metric → `Metric` lit, mixed → neither.

## Architecture

### SettingsService (`frontend/src/app/services/settings.ts`)

One signal per preference:

```ts
interface SiteSettings {
  temperature: 'F' | 'C';
  windSpeed:  'mph' | 'kmh' | 'ms';
  elevation:  'ft' | 'm';
  snowDepth:  'in' | 'cm';      // SWE, depth, and new-snow share this
  visibility: 'mi' | 'km';
  timeFormat: '12h' | '24h';
  theme:      'dark' | 'light' | 'system';
}
```

**Startup resolution (browser only; SSR/prerender sees defaults):**

1. Parse `localStorage["brw.settings.v1"]` (JSON). Each field validates independently against its enum; an invalid or missing field falls back alone — a corrupt blob never resets valid fields.
2. No stored value → detect in memory, **do not persist**: locale region `US` or undetectable (bare `en`) → imperial set; any other region → metric set (`kmh` wind — `ms` is only ever an explicit choice). Theme → `dark`. Detection is a pure function `detectDefaults(locale: string)` for testability.
3. Storage is written only when the user changes a setting. `localStorage` access is wrapped in try/catch; on failure (private mode, quota) settings work in memory for the session.

**Hydration safety:** signals hold imperial/dark defaults through prerender and hydration — matching the baked static HTML — and stored preferences apply in `afterNextRender`. A metric user may see prerendered imperial text (e.g. hero elevation) for a frame before conversion; accepted trade-off for zero NG0500 mismatches. Theme is exempt via the pre-paint script below.

The service also exposes display computeds consumed by templates: unit labels (`°F`/`°C`, `mph`/`km/h`/`m/s`, `ft`/`m`, `"`/`cm`, `mi`/`km`) and date-pipe format strings (hourly `'EEE h a'` ↔ `'EEE HH:mm'`, clock `'h:mm a'` ↔ `'HH:mm'`, timestamps `'M/d/yy, h:mm a'` ↔ `'M/d/yy, HH:mm'`).

### Units module (`frontend/src/app/units/`)

- Pure conversion functions, number → number: ft→m (×0.3048), mi→km (×1.609344), mph→km/h (×1.609344), mph→m/s (×0.44704), in→cm (×2.54), °F→°C ((f−32)×5⁄9). Imperial passthrough is identity.
- Thin standalone pure pipes wrap them, taking the target unit as an argument: `{{ h.windMph | speed: u.windSpeed() }}`. The signal read lives in the template and the changed argument busts pipe memoization — the zoneless-safe idiom.
- **Pipes never format.** Existing `| number:'…'` and `| date:'…'` pipes keep doing formatting, so every surface inherits its current precision (hourly wind stays integer, per-source stays one decimal, elevation keeps thousands separators).

### Theming

- **Tokens:** every hardcoded hex in `styles.scss` and component SCSS becomes a CSS custom property (`--bg`, `--panel`, `--text`, `--muted`, `--border`, `--accent`, plus pill/chip variants). Dark values are defined on `:root` and remain the default. Light values override under `:root[data-theme="light"]` in the global sheet only.
- **Attribute convention:** `data-theme="light"` is stamped on `<html>` only when the resolved theme is light; otherwise the attribute is absent (absent = dark). `system` resolves via `matchMedia('(prefers-color-scheme: …)')`, with a change listener active only while theme is `system` (removed on change away; jsdom/prerender guard: no `matchMedia` → dark).
- **No wrong-theme flash:** a small inline script in `index.html` (before the app bundle, wrapped in try/catch so a corrupt blob can never block paint) reads the stored theme and stamps the attribute pre-paint. On boot the service adopts the stamped attribute as the resolved starting state rather than re-deriving — no double-flip.
- **Map:** `MapHome` gains an effect swapping CARTO basemaps `dark_all` ↔ `light_all` via `L.TileLayer.setUrl` — same provider and attribution, no layer rebuild. Leaflet popup/marker chrome already lives in the global sheet and themes through tokens.
- **Approved light palette direction** (fine-tuning allowed during implementation within WCAG AA ≥4.5:1 for body text): page `#f2f5f8`, panel `#ffffff`, text `#1a2733`, muted `#55677a`, border `#d5dee6`, accent `#33567f`, pill colors positive `#1f8a4d` / negative `#b45309` / neutral `#55677a`, panel shadow `rgba(23,39,51,.06)`. Grade colors (`#3ecf78` A … `#d04848` F) are identical in both themes.
- **SCSS budget:** `peak-detail.scss` sits at ~6.6kB of the 7kB `anyComponentStyle` warning. The hex→`var()` refactor is roughly length-neutral, but the implementation plan must include the compact-or-bump decision if it tips.

### SettingsMenu component (`frontend/src/app/components/settings-menu/`)

Standalone, OnPush, signal-driven. Rendered from the app shell header as the fourth nav item (`Map · All peaks · About · ⚙`).

- Desktop: right-anchored popover under the header. Below 640px: the same DOM renders as a full-width sheet pinned under the header (CSS-only switch). Align the breakpoint with an existing site breakpoint during implementation if one exists.
- Contents top-to-bottom: preset buttons, six segmented unit rows, Appearance 3-way.
- Every control writes its service signal on click; the page behind converts live; the service persists.
- Closes on outside click, `Escape` (focus returns to the gear), and route navigation. Lightweight popover, not a modal: no focus trap, natural tab order, `aria-expanded` + `aria-controls` on the gear button.

### Map popups

`popupHtml()` builds raw HTML outside Angular, so it takes the pre-formatted elevation string as a parameter. The marker-render effect reads the elevation-unit signal, so a unit change re-binds popups automatically.

## Display touchpoints

- `peak-detail.html` — hero elevation; feels-like tile; hourly table (temp header + cells, wind, gust, time column); per-source table (wind, gust, min-temp header); snowpack tiles (SWE, depth, new-snow); visibility; daylight times; sources-footer timestamps.
- `route-card.html` — elevation (`14,411'` ↔ `4,392 m`).
- Map popup elevation line.
- Sparkline input points (snow depth) convert through the same function.

**Deliberately untouched:** diagnostics page (internal tooling, stays imperial); all SEO surfaces (structured data keeps `unitCode: 'FOT'`, meta text stays imperial — crawlers see defaults); CAPE (J/kg is SI); AQI (stays US AQI; tile may be labeled "US AQI"); all percentages.

## Error handling

- Storage unavailable or throwing → in-memory settings for the session; no user-facing error.
- Corrupt/partial stored JSON → per-field fallback to defaults.
- No `matchMedia` (prerender, jsdom) → dark.
- Inline theme script failure → page paints dark (default); Angular recovers state normally.

## Testing

- **Conversions:** one table-driven spec covering all five conversions and rounding boundaries.
- **SettingsService:** `detectDefaults` as a pure function (`'en-GB'` → metric, `'en-US'`/`'en'` → imperial); storage round-trip with stubbed `localStorage`; corrupt JSON → per-field fallback; theme stamping asserted on `document.documentElement`; System mode with stubbed `matchMedia`.
- **SettingsMenu:** structure-anchored (per project testing rules — no prose assertions): gear with `aria-expanded`, panel toggles, preset click sets all six unit signals, segmented change persists.
- **Component cases:** `peak-detail` metric mode (°C header, converted cells, cm snowpack, 24h times) by configuring the service before first `detectChanges`; `route-card` metric elevation; `popupHtml` formatted-elevation parameter.
- **Invariants:** no model/HTTP changes, so existing specs and `RouteSummary` fixtures are untouched.
- **Beyond jsdom:** hydration cleanliness, theme flash, and tile swap verified on the dev Pages preview after the PR (SSR issues only surface in a real browser).

## Out of scope / future

- Language/i18n (day names, prose) — separate project.
- European AQI scale — requires warmer/backend data changes.
- Wind in knots; default landing view (Map vs All peaks) — cut for v1, cheap to add later.
- Slide-over drawer upgrade: the `SettingsMenu` form slots into a drawer shell if the popover ever feels limiting.
