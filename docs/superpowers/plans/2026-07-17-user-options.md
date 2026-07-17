# User Options (Units + Theme + Settings Menu) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** User-configurable display options — per-measurement units with Imperial/Metric presets, 12h/24h time, and a Dark/Light/System theme — behind a gear popover in the header, persisted to localStorage.

**Architecture:** A signal-based `SettingsService` (localStorage-persisted, locale-detected defaults, browser-gated for SSG) feeds pure number→number unit pipes in templates (existing `number`/`date` pipes keep formatting) and a CSS-custom-property theme layer (dark values on `:root` stay the default; light overrides under `:root[data-theme="light"]`; pre-paint inline script prevents flash). The map swaps CARTO `dark_all`/`light_all` tiles via an effect. No backend changes.

**Tech Stack:** Angular 21 zoneless + signals, SCSS, Vitest + jsdom, Leaflet (UMD via `loadLeaflet()`).

**Spec:** `docs/superpowers/specs/2026-07-17-user-options-design.md`
**Branch:** `feature/user-options` (based off `dev`). All commands below run from `frontend/` unless noted.

**Deviations from spec (agreed rationale, flag in PR description):**
1. *Sparkline points are NOT converted.* The chart hides both axes and shows no numbers (`sparkline.ts` sets `x/y display: false`, tooltips disabled); in→cm is a linear transform of invisible data, so conversion is a pixel-identical no-op. Skipped per YAGNI.
2. *Theme loads eagerly, not in `afterNextRender`.* Only **stored unit prefs** wait for `afterNextRender`; locale-**detected** defaults apply eagerly at construction (required — `applyStored` is storage-only, so deferring detection would strand no-storage metric-locale visitors on imperial forever). Consequence: a non-US first visit renders metric text against imperial prerendered HTML for a frame — a text-content mismatch Angular tolerates (NG0500 is structural), same accepted trade-off as the spec's stored-prefs flash; verify on the dev preview with a non-US browser locale once Task 9 lands (Task 12 checklist #7/#8). The theme signal must initialize from storage in the service constructor: the `<html>` attribute is outside Angular's hydrated DOM, and waiting would let the effect strip the attribute the inline script stamped (dark flash). The spec's "adopts the stamped attribute" is implemented as "reads the same storage the script read, eagerly" — same result, testable.
3. *`consensus-badge.scss` and grade fills stay literal.* Grade colors are brand-constant in both themes (spec). The consensus badge is deliberately faint neutral (`rgba(0,0,0,…)`, `#4a5560`) and reads correctly on both themes unchanged.

---

## File Structure

**Create**
- `src/app/units/conversions.ts` — pure conversion + `formatElevationText`
- `src/app/units/conversions.spec.ts`
- `src/app/units/unit-pipes.ts` — `temp`/`speed`/`elev`/`depth`/`dist` pipes + `UNIT_PIPES`
- `src/app/units/unit-pipes.spec.ts`
- `src/app/services/settings-defaults.ts` — `SiteSettings`, allowed values, `detectDefaults`, `sanitizeStored`, `STORAGE_KEY`
- `src/app/services/settings-defaults.spec.ts`
- `src/app/services/settings.ts` — `SettingsService`
- `src/app/services/settings.spec.ts`
- `src/app/components/settings-menu/settings-menu.ts` / `.html` / `.scss` / `.spec.ts`

**Modify**
- `src/styles.scss` — token block (`:root` dark + light overrides), own rules → `var()`
- `src/index.html` — pre-paint theme script
- `src/app/pages/peak-detail/peak-detail.scss`, `src/app/components/route-card/route-card.scss`, `src/app/pages/map-home/map-home.scss`, `src/app/app.scss`, `src/app/components/route-grid/route-grid.scss`, `src/app/pages/about/about.scss`, `src/app/pages/diagnostics/diagnostics.scss` — hex → `var()` sweep
- `angular.json` — `anyComponentStyle` budget 7/8kB → 8/9kB (tokenization adds ~0.4kB to peak-detail: `#8aa0b4`→`var(--muted)`)
- `src/app/app.ts`, `src/app/app.html`, `src/app/app.scss` — gear + menu in header
- `src/app/pages/peak-detail/peak-detail.ts`, `.html`, `.spec.ts` — unit pipes + metric spec
- `src/app/components/route-card/route-card.ts`, `.html`, `.spec.ts`
- `src/app/pages/map-home/map-home.ts`, `.spec.ts` — `popupHtml` elevation param, tile theme effect, elevation re-render effect

**Untouched by design:** all backend, `src/app/models/*`, SEO files, `grade-badge.scss`, `consensus-badge.scss`, `sparkline.ts`, diagnostics *templates* (its SCSS is tokenized so the page isn't a dark island in light mode; its units stay imperial).

---

### Task 1: Pure unit conversions

**Files:**
- Create: `src/app/units/conversions.ts`
- Test: `src/app/units/conversions.spec.ts`

- [ ] **Step 1: Write the failing test**

```ts
// src/app/units/conversions.spec.ts
import { convertTemp, convertSpeed, convertElev, convertDepth, convertDist, formatElevationText } from './conversions';

describe('conversions', () => {
  it('temperature: F passthrough, F→C', () => {
    expect(convertTemp(40, 'F')).toBe(40);
    expect(convertTemp(32, 'C')).toBe(0);
    expect(convertTemp(40, 'C')).toBeCloseTo(4.444, 3);
    expect(convertTemp(-40, 'C')).toBe(-40);
  });

  it('speed: mph passthrough, mph→km/h, mph→m/s', () => {
    expect(convertSpeed(8, 'mph')).toBe(8);
    expect(convertSpeed(8, 'kmh')).toBeCloseTo(12.875, 3);
    expect(convertSpeed(8, 'ms')).toBeCloseTo(3.576, 3);
  });

  it('elevation: ft passthrough, ft→m', () => {
    expect(convertElev(14259, 'ft')).toBe(14259);
    expect(convertElev(14259, 'm')).toBeCloseTo(4346.14, 2);
  });

  it('depth: in passthrough, in→cm', () => {
    expect(convertDepth(1.2, 'in')).toBe(1.2);
    expect(convertDepth(1.2, 'cm')).toBeCloseTo(3.048, 3);
    expect(convertDepth(4.0, 'cm')).toBeCloseTo(10.16, 2);
  });

  it('distance: mi passthrough, mi→km', () => {
    expect(convertDist(10, 'mi')).toBe(10);
    expect(convertDist(10, 'km')).toBeCloseTo(16.093, 3);
  });

  it('formatElevationText renders a localized rounded value with its unit', () => {
    expect(formatElevationText(14259, 'ft')).toBe('14,259 ft');
    expect(formatElevationText(14259, 'm')).toBe('4,346 m');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — `conversions.spec.ts` cannot resolve `./conversions`.

- [ ] **Step 3: Write minimal implementation**

```ts
// src/app/units/conversions.ts
// All API values are imperial-canonical; converters take the imperial value and
// the user's target unit. Rounding/formatting stays with Angular's number pipe.
export type TempUnit = 'F' | 'C';
export type SpeedUnit = 'mph' | 'kmh' | 'ms';
export type ElevUnit = 'ft' | 'm';
export type DepthUnit = 'in' | 'cm';
export type DistUnit = 'mi' | 'km';

export function convertTemp(f: number, unit: TempUnit): number {
  return unit === 'C' ? (f - 32) * 5 / 9 : f;
}

export function convertSpeed(mph: number, unit: SpeedUnit): number {
  if (unit === 'kmh') return mph * 1.609344;
  if (unit === 'ms') return mph * 0.44704;
  return mph;
}

export function convertElev(ft: number, unit: ElevUnit): number {
  return unit === 'm' ? ft * 0.3048 : ft;
}

export function convertDepth(inches: number, unit: DepthUnit): number {
  return unit === 'cm' ? inches * 2.54 : inches;
}

export function convertDist(mi: number, unit: DistUnit): number {
  return unit === 'km' ? mi * 1.609344 : mi;
}

// For map popups, which build HTML strings outside Angular's pipe pipeline.
// en-US pinned to match route-meta.ts and keep output deterministic in tests.
export function formatElevationText(ft: number, unit: ElevUnit): string {
  return `${Math.round(convertElev(ft, unit)).toLocaleString('en-US')} ${unit}`;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS (all suites; new file green).

- [ ] **Step 5: Commit**

```bash
git add src/app/units/conversions.ts src/app/units/conversions.spec.ts
git commit -m "feat(units): pure unit conversion functions"
```

---

### Task 2: Settings model, locale detection, validation

**Files:**
- Create: `src/app/services/settings-defaults.ts`
- Test: `src/app/services/settings-defaults.spec.ts`

- [ ] **Step 1: Write the failing test**

```ts
// src/app/services/settings-defaults.spec.ts
import { DEFAULT_SETTINGS, detectDefaults, sanitizeStored } from './settings-defaults';

describe('detectDefaults', () => {
  it('US and region-less locales get imperial + dark', () => {
    for (const locale of ['en-US', 'en', undefined]) {
      const s = detectDefaults(locale);
      expect(s).toEqual(DEFAULT_SETTINGS);
      expect(s.temperature).toBe('F');
      expect(s.theme).toBe('dark');
    }
  });

  it('non-US regions get metric with km/h wind, still dark', () => {
    for (const locale of ['en-GB', 'de-DE', 'fr-CA', 'zh-Hant-TW']) {
      const s = detectDefaults(locale);
      expect(s.temperature).toBe('C');
      expect(s.windSpeed).toBe('kmh'); // m/s is only ever an explicit choice
      expect(s.elevation).toBe('m');
      expect(s.snowDepth).toBe('cm');
      expect(s.visibility).toBe('km');
      expect(s.timeFormat).toBe('24h');
      expect(s.theme).toBe('dark');
    }
  });

  it('garbage locales fall back to imperial', () => {
    expect(detectDefaults('!!not-a-locale!!')).toEqual(DEFAULT_SETTINGS);
  });
});

describe('sanitizeStored', () => {
  it('keeps only valid fields, dropping unknown keys and bad values', () => {
    expect(sanitizeStored({ temperature: 'C', windSpeed: 'knots', theme: 'light', bogus: 1 }))
      .toEqual({ temperature: 'C', theme: 'light' });
  });

  it('returns empty for non-objects', () => {
    for (const raw of [null, undefined, 42, 'x', []]) {
      expect(sanitizeStored(raw)).toEqual({});
    }
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot resolve `./settings-defaults`.

- [ ] **Step 3: Write minimal implementation**

```ts
// src/app/services/settings-defaults.ts
import { DepthUnit, DistUnit, ElevUnit, SpeedUnit, TempUnit } from '../units/conversions';

export type TimeFormat = '12h' | '24h';
export type ThemeSetting = 'dark' | 'light' | 'system';

export interface SiteSettings {
  temperature: TempUnit;
  windSpeed: SpeedUnit;
  elevation: ElevUnit;
  snowDepth: DepthUnit;
  visibility: DistUnit;
  timeFormat: TimeFormat;
  theme: ThemeSetting;
}

export const STORAGE_KEY = 'brw.settings.v1';

export const SETTING_VALUES: { [K in keyof SiteSettings]: readonly SiteSettings[K][] } = {
  temperature: ['F', 'C'],
  windSpeed: ['mph', 'kmh', 'ms'],
  elevation: ['ft', 'm'],
  snowDepth: ['in', 'cm'],
  visibility: ['mi', 'km'],
  timeFormat: ['12h', '24h'],
  theme: ['dark', 'light', 'system'],
};

export const DEFAULT_SETTINGS: SiteSettings = {
  temperature: 'F', windSpeed: 'mph', elevation: 'ft',
  snowDepth: 'in', visibility: 'mi', timeFormat: '12h', theme: 'dark',
};

const METRIC_SETTINGS: SiteSettings = {
  temperature: 'C', windSpeed: 'kmh', elevation: 'm',
  snowDepth: 'cm', visibility: 'km', timeFormat: '24h', theme: 'dark',
};

// US region or no discernible region → imperial; any other region → metric.
// Theme is always dark by default (brand decision — light is an explicit opt-in).
export function detectDefaults(locale: string | undefined): SiteSettings {
  if (!locale) return { ...DEFAULT_SETTINGS };
  let region: string | undefined;
  try {
    region = new Intl.Locale(locale).region;
  } catch {
    region = undefined;
  }
  if (!region || region === 'US') return { ...DEFAULT_SETTINGS };
  return { ...METRIC_SETTINGS };
}

// Per-field validation: a corrupt or stale blob never nukes valid fields.
export function sanitizeStored(raw: unknown): Partial<SiteSettings> {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return {};
  const out: Partial<SiteSettings> = {};
  for (const key of Object.keys(SETTING_VALUES) as (keyof SiteSettings)[]) {
    const value = (raw as Record<string, unknown>)[key];
    if ((SETTING_VALUES[key] as readonly unknown[]).includes(value)) {
      (out as Record<string, unknown>)[key] = value;
    }
  }
  return out;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/services/settings-defaults.ts src/app/services/settings-defaults.spec.ts
git commit -m "feat(settings): settings model, locale detection, stored-value validation"
```

---

### Task 3: SettingsService

**Files:**
- Create: `src/app/services/settings.ts`
- Test: `src/app/services/settings.spec.ts`

- [ ] **Step 1: Write the failing test**

```ts
// src/app/services/settings.spec.ts
import { TestBed } from '@angular/core/testing';
import { SettingsService } from './settings';
import { STORAGE_KEY } from './settings-defaults';

describe('SettingsService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  function create(): SettingsService {
    return TestBed.inject(SettingsService);
  }

  it('starts with imperial + dark defaults (no stored value, US-ish jsdom locale)', () => {
    const s = create();
    expect(s.temperature()).toBe('F');
    expect(s.windSpeed()).toBe('mph');
    expect(s.theme()).toBe('dark');
    expect(s.unitPreset()).toBe('imperial');
  });

  it('does not write storage for passive visitors', () => {
    const s = create();
    s.applyStored();
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('persists the full settings object when a setting changes', () => {
    const s = create();
    s.set('temperature', 'C');
    const stored = JSON.parse(localStorage.getItem(STORAGE_KEY)!);
    expect(stored.temperature).toBe('C');
    expect(stored.theme).toBe('dark');
  });

  it('applyStored applies valid stored fields and ignores corrupt ones', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ windSpeed: 'ms', elevation: 'nope' }));
    const s = create();
    s.applyStored();
    expect(s.windSpeed()).toBe('ms');
    expect(s.elevation()).toBe('ft');
  });

  it('survives a non-JSON blob', () => {
    localStorage.setItem(STORAGE_KEY, '{{{{');
    const s = create();
    s.applyStored();
    expect(s.temperature()).toBe('F');
  });

  it('applyPreset(metric) flips all six unit fields and persists; preset highlight follows', () => {
    const s = create();
    s.applyPreset('metric');
    expect(s.temperature()).toBe('C');
    expect(s.windSpeed()).toBe('kmh');
    expect(s.elevation()).toBe('m');
    expect(s.snowDepth()).toBe('cm');
    expect(s.visibility()).toBe('km');
    expect(s.timeFormat()).toBe('24h');
    expect(s.unitPreset()).toBe('metric');
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!).temperature).toBe('C');
    s.set('temperature', 'F');
    expect(s.unitPreset()).toBe('mixed');
  });

  it('labels and date formats follow the unit signals', () => {
    const s = create();
    expect(s.tempLabel()).toBe('°F');
    expect(s.windLabel()).toBe('mph');
    expect(s.hourFmt()).toBe('EEE h a');
    expect(s.clockFmt()).toBe('h:mm a');
    s.applyPreset('metric');
    s.set('windSpeed', 'ms');
    expect(s.tempLabel()).toBe('°C');
    expect(s.windLabel()).toBe('m/s');
    expect(s.elevLabel()).toBe('m');
    expect(s.elevSuffix()).toBe(' m');
    expect(s.depthSuffix()).toBe(' cm');
    expect(s.distLabel()).toBe('km');
    expect(s.hourFmt()).toBe('EEE HH:mm');
    expect(s.stampFmt()).toBe('M/d/yy, HH:mm');
  });

  it('stamps data-theme="light" only when resolved theme is light', async () => {
    const s = create();
    expect(s.resolvedTheme()).toBe('dark');
    s.set('theme', 'light');
    await Promise.resolve(); // let the effect run (zoneless scheduler)
    TestBed.tick();
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    s.set('theme', 'dark');
    TestBed.tick();
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('system theme resolves via matchMedia when present, dark when absent', () => {
    const s = create();
    s.set('theme', 'system');
    // jsdom has no matchMedia → resolves dark
    expect(s.resolvedTheme()).toBe('dark');
  });

  it('adopts an eagerly stored theme at construction (inline-script parity)', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ theme: 'light' }));
    const s = create();
    expect(s.theme()).toBe('light');
    expect(s.temperature()).toBe('F'); // units wait for applyStored/afterNextRender
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot resolve `./settings`.

- [ ] **Step 3: Write the implementation**

```ts
// src/app/services/settings.ts
import { DestroyRef, Injectable, PLATFORM_ID, WritableSignal, afterNextRender, computed, effect, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import {
  DEFAULT_SETTINGS, SETTING_VALUES, SiteSettings, STORAGE_KEY, ThemeSetting,
  detectDefaults, sanitizeStored,
} from './settings-defaults';

type UnitKey = Exclude<keyof SiteSettings, 'theme'>;
const UNIT_KEYS: readonly UnitKey[] = ['temperature', 'windSpeed', 'elevation', 'snowDepth', 'visibility', 'timeFormat'];

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly sig: { [K in keyof SiteSettings]: WritableSignal<SiteSettings[K]> };

  // Reads for templates: u.temperature(), u.theme(), ...
  readonly temperature; readonly windSpeed; readonly elevation;
  readonly snowDepth; readonly visibility; readonly timeFormat; readonly theme;

  // 'light' only ever comes from an explicit user choice; OS light mode alone
  // never overrides the dark brand default (theme must be 'system' for that).
  private readonly systemTheme = signal<'dark' | 'light'>('dark');
  readonly resolvedTheme = computed<'dark' | 'light'>(() => {
    const t = this.theme();
    return t === 'system' ? this.systemTheme() : t;
  });

  readonly unitPreset = computed<'imperial' | 'metric' | 'mixed'>(() => {
    const imperial = UNIT_KEYS.every(k => this.sig[k]() === DEFAULT_SETTINGS[k]);
    if (imperial) return 'imperial';
    const metric = UNIT_KEYS.every(k => this.sig[k]() !== DEFAULT_SETTINGS[k]
      && this.sig[k]() !== 'ms'); // m/s is an override, not part of the metric preset
    return metric ? 'metric' : 'mixed';
  });

  readonly tempLabel = computed(() => this.temperature() === 'C' ? '°C' : '°F');
  readonly windLabel = computed(() => ({ mph: 'mph', kmh: 'km/h', ms: 'm/s' }[this.windSpeed()]));
  readonly elevLabel = computed(() => this.elevation());
  readonly elevSuffix = computed(() => this.elevation() === 'm' ? ' m' : `'`);
  readonly depthSuffix = computed(() => this.snowDepth() === 'cm' ? ' cm' : '"');
  readonly distLabel = computed(() => this.visibility());
  readonly hourFmt = computed(() => this.timeFormat() === '24h' ? 'EEE HH:mm' : 'EEE h a');
  readonly clockFmt = computed(() => this.timeFormat() === '24h' ? 'HH:mm' : 'h:mm a');
  readonly stampFmt = computed(() => this.timeFormat() === '24h' ? 'M/d/yy, HH:mm' : 'M/d/yy, h:mm a');

  constructor() {
    const detected = this.isBrowser
      ? detectDefaults(typeof navigator !== 'undefined' ? navigator.language : undefined)
      : { ...DEFAULT_SETTINGS };

    // Theme loads eagerly (the <html> attribute is outside hydrated DOM and the
    // inline index.html script already stamped it); units wait for afterNextRender
    // so client render matches prerendered imperial text during hydration.
    const storedTheme = this.isBrowser ? sanitizeStored(this.readRaw()).theme : undefined;

    this.sig = {
      temperature: signal(detected.temperature),
      windSpeed: signal(detected.windSpeed),
      elevation: signal(detected.elevation),
      snowDepth: signal(detected.snowDepth),
      visibility: signal(detected.visibility),
      timeFormat: signal(detected.timeFormat),
      theme: signal<ThemeSetting>(storedTheme ?? detected.theme),
    };
    this.temperature = this.sig.temperature.asReadonly();
    this.windSpeed = this.sig.windSpeed.asReadonly();
    this.elevation = this.sig.elevation.asReadonly();
    this.snowDepth = this.sig.snowDepth.asReadonly();
    this.visibility = this.sig.visibility.asReadonly();
    this.timeFormat = this.sig.timeFormat.asReadonly();
    this.theme = this.sig.theme.asReadonly();

    if (this.isBrowser) {
      this.watchSystemTheme();
      effect(() => {
        const el = document.documentElement;
        if (this.resolvedTheme() === 'light') el.setAttribute('data-theme', 'light');
        else el.removeAttribute('data-theme');
      });
      afterNextRender(() => this.applyStored());
    }
  }

  /** Apply stored unit preferences. Public so specs can invoke what afterNextRender schedules. */
  applyStored(): void {
    const stored = sanitizeStored(this.readRaw());
    for (const key of Object.keys(stored) as (keyof SiteSettings)[]) {
      (this.sig[key] as WritableSignal<unknown>).set(stored[key]);
    }
  }

  set<K extends keyof SiteSettings>(key: K, value: SiteSettings[K]): void {
    this.sig[key].set(value);
    this.persist();
  }

  /** String-typed setter for menu rows; ignores values not in the allowed list. */
  setFromMenu(key: keyof SiteSettings, value: string): void {
    if ((SETTING_VALUES[key] as readonly string[]).includes(value)) {
      this.set(key, value as SiteSettings[typeof key]);
    }
  }

  valueFor(key: keyof SiteSettings): string {
    return this.sig[key]() as string;
  }

  applyPreset(preset: 'imperial' | 'metric'): void {
    const source = preset === 'metric'
      ? ({ temperature: 'C', windSpeed: 'kmh', elevation: 'm', snowDepth: 'cm', visibility: 'km', timeFormat: '24h' } as const)
      : ({ temperature: 'F', windSpeed: 'mph', elevation: 'ft', snowDepth: 'in', visibility: 'mi', timeFormat: '12h' } as const);
    for (const key of UNIT_KEYS) {
      (this.sig[key] as WritableSignal<unknown>).set(source[key]);
    }
    this.persist();
  }

  private current(): SiteSettings {
    return {
      temperature: this.temperature(), windSpeed: this.windSpeed(), elevation: this.elevation(),
      snowDepth: this.snowDepth(), visibility: this.visibility(), timeFormat: this.timeFormat(),
      theme: this.theme(),
    };
  }

  private readRaw(): unknown {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw === null ? null : JSON.parse(raw);
    } catch {
      return null; // private mode / corrupt blob → in-memory settings
    }
  }

  private persist(): void {
    if (!this.isBrowser) return;
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.current()));
    } catch {
      // storage unavailable — settings still work for the session
    }
  }

  private watchSystemTheme(): void {
    if (typeof window.matchMedia !== 'function') return; // jsdom / old browsers → stay dark
    const query = window.matchMedia('(prefers-color-scheme: light)');
    this.systemTheme.set(query.matches ? 'light' : 'dark');
    const onChange = (e: MediaQueryListEvent) => this.systemTheme.set(e.matches ? 'light' : 'dark');
    query.addEventListener('change', onChange);
    inject(DestroyRef).onDestroy(() => query.removeEventListener('change', onChange));
  }
}
```

**Watch out:** `inject(DestroyRef)` inside `watchSystemTheme` only works because it's called from the constructor (injection context). Keep the call there. `effect(...)` in the constructor is likewise fine.

Note the `unitPreset` nuance: jsdom's `navigator.language` is `en-US`, so tests start imperial. The `!== 'ms'` guard makes `metric + wind:m/s` report `mixed` — the preset buttons then show neither lit, which is correct (m/s is an override).

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS. If the theme-stamping test flakes on effect timing, `TestBed.tick()` after each `set` is the fix (already in the spec code).

- [ ] **Step 5: Commit**

```bash
git add src/app/services/settings.ts src/app/services/settings.spec.ts
git commit -m "feat(settings): SettingsService with persistence, presets, theme resolution"
```

---

### Task 4: Unit pipes

**Files:**
- Create: `src/app/units/unit-pipes.ts`
- Test: `src/app/units/unit-pipes.spec.ts`

- [ ] **Step 1: Write the failing test**

```ts
// src/app/units/unit-pipes.spec.ts
import { TempPipe, SpeedPipe, ElevPipe, DepthPipe, DistPipe } from './unit-pipes';

describe('unit pipes', () => {
  it('convert to the requested unit and pass imperial through', () => {
    expect(new TempPipe().transform(40, 'F')).toBe(40);
    expect(new TempPipe().transform(40, 'C')).toBeCloseTo(4.444, 3);
    expect(new SpeedPipe().transform(8, 'kmh')).toBeCloseTo(12.875, 3);
    expect(new ElevPipe().transform(14259, 'm')).toBeCloseTo(4346.14, 2);
    expect(new DepthPipe().transform(1.2, 'cm')).toBeCloseTo(3.048, 3);
    expect(new DistPipe().transform(10, 'km')).toBeCloseTo(16.093, 3);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot resolve `./unit-pipes`.

- [ ] **Step 3: Write the implementation**

```ts
// src/app/units/unit-pipes.ts
// Pure pipes: the unit argument comes from a SettingsService signal read in the
// template, so the changed argument busts memoization — the zoneless-safe idiom.
// They convert number → number; Angular's number/date pipes keep formatting.
import { Pipe, PipeTransform } from '@angular/core';
import {
  DepthUnit, DistUnit, ElevUnit, SpeedUnit, TempUnit,
  convertDepth, convertDist, convertElev, convertSpeed, convertTemp,
} from './conversions';

@Pipe({ name: 'temp' })
export class TempPipe implements PipeTransform {
  transform(valueF: number, unit: TempUnit): number { return convertTemp(valueF, unit); }
}

@Pipe({ name: 'speed' })
export class SpeedPipe implements PipeTransform {
  transform(valueMph: number, unit: SpeedUnit): number { return convertSpeed(valueMph, unit); }
}

@Pipe({ name: 'elev' })
export class ElevPipe implements PipeTransform {
  transform(valueFt: number, unit: ElevUnit): number { return convertElev(valueFt, unit); }
}

@Pipe({ name: 'depth' })
export class DepthPipe implements PipeTransform {
  transform(valueIn: number, unit: DepthUnit): number { return convertDepth(valueIn, unit); }
}

@Pipe({ name: 'dist' })
export class DistPipe implements PipeTransform {
  transform(valueMi: number, unit: DistUnit): number { return convertDist(valueMi, unit); }
}

export const UNIT_PIPES = [TempPipe, SpeedPipe, ElevPipe, DepthPipe, DistPipe] as const;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/units/unit-pipes.ts src/app/units/unit-pipes.spec.ts
git commit -m "feat(units): number-to-number unit conversion pipes"
```

---

### Task 5: Design tokens + pre-paint theme script

**Files:**
- Modify: `src/styles.scss` (token block at top; own rules → vars)
- Modify: `src/index.html` (inline script)

This task must be a **visual no-op in dark mode** for structural colors — with one sanctioned exception: a handful of near-duplicate grays intentionally collapse onto shared tokens (listed per-row below and in Tasks 6-7). Those rows shift dark-mode rendering by a near-imperceptible delta; that consolidation is part of the approved design (a finite token set instead of one-off tokens per stray hex). Everything not explicitly listed as a consolidation must be byte-exact in dark mode.

- [ ] **Step 1: Add the token block at the very top of `src/styles.scss`** (after the three `@import` lines, before `html, body`):

```scss
// ---------------------------------------------------------------------------
// Design tokens. Dark is the default (:root); light overrides via
// [data-theme="light"], stamped pre-paint by the inline script in index.html
// and kept in sync by SettingsService. Grade fills (badges, dots) are brand
// constants and intentionally NOT tokenized.
// ---------------------------------------------------------------------------
:root {
  --bg: #0d1620;
  --surface: #0a1525;            // map bg, Leaflet popup chrome
  --panel: #11202c;
  --panel-raised: #1a2632;
  --panel-hover: #22313f;
  --text: #e8eef3;
  --text-soft: #cfd8dc;
  --muted: #8aa0b4;
  --muted-deep: #6b7e90;
  --border: #2c3e50;
  --border-soft: #243240;
  --accent: #5fa8d8;
  --accent-hover: #cfe5f5;
  --accent-strong: #3a5a8a;
  --nav-active: #c5e1a5;
  --teal: #80cbc4;
  --focus: #4a90e2;
  --ink-on-grade: #0a1525;       // text on grade chips — dark in BOTH themes

  --warn-bg: #6d4c00;
  --warn-text: #ffd54f;
  --danger-text: #ef9a9a;
  --danger-strong: #c44;
  --danger-strong-hover: #d55;

  --pill-pos-bg: #1b3a1b; --pill-pos-text: #a5d6a7; --pill-pos-border: #2e7d32;
  --pill-neu-bg: #2a3340; --pill-neu-text: #b0bec5; --pill-neu-border: #455a64;
  --pill-neg-bg: #4a1f1f; --pill-neg-text: #ef9a9a; --pill-neg-border: #c62828;

  --chip-range-bg: rgba(95, 168, 216, 0.18); --chip-range-text: #cfe5f5;
  --chip-glacier-bg: rgba(120, 170, 200, 0.16); --chip-glacier-text: #cfe2ee;
  --glacier-text: #bcd9ea;
  --glacier-badge: #d6ecf7;
  --glacier-badge-halo: #0b1620;

  --gw-bg: #2a1414; --gw-border: #c62828; --gw-edge: #ef5350;
  --gw-text: #ffd9d9; --gw-title: #ff8a80; --gw-strong: #fff;

  --aqi-good: #81c784; --aqi-moderate: #ffd54f; --aqi-usg: #ffb74d;
  --aqi-unhealthy: #e57373; --aqi-very-unhealthy: #ba68c8; --aqi-hazardous: #c75b6d;

  --grade-text-a: #81c784; --grade-text-b: #aed581; --grade-text-c: #ffd54f;
  --grade-text-d: #ffb74d; --grade-text-f: #ef9a9a;

  --overlay-bg: rgba(10, 21, 37, 0.85);
  --overlay-dim: rgba(10, 21, 37, 0.78);
  --err-card-bg: #1a2942;
  --map-label: rgba(207, 229, 245, 0.75);
  --map-label-halo: rgba(0, 0, 0, 0.6);
  --panel-shadow: rgba(0, 0, 0, 0.4);
}

// Approved light palette direction (spec §Theming); fine-tuning allowed within
// WCAG AA (>= 4.5:1) for body text.
:root[data-theme='light'] {
  --bg: #f2f5f8;
  --surface: #e6edf3;
  --panel: #ffffff;
  --panel-raised: #f6f8fa;
  --panel-hover: #e9eef3;
  --text: #1a2733;
  --text-soft: #3d4f60;
  --muted: #55677a;
  --muted-deep: #7d8fa3;
  --border: #d5dee6;
  --border-soft: #e4ebf1;
  --accent: #33567f;
  --accent-hover: #24405e;
  --accent-strong: #33567f;
  --nav-active: #4a7c2a;
  --teal: #0f766e;
  --focus: #2f6db3;

  --warn-bg: #f5e9c8;
  --warn-text: #7a5c00;
  --danger-text: #b3453c;
  --danger-strong: #c0392b;
  --danger-strong-hover: #a93226;

  --pill-pos-bg: #e3f2e7; --pill-pos-text: #1f7a43; --pill-pos-border: #a9d5b5;
  --pill-neu-bg: #eef2f5; --pill-neu-text: #55677a; --pill-neu-border: #c6d2dc;
  --pill-neg-bg: #f7ede2; --pill-neg-text: #b45309; --pill-neg-border: #dbb28c;

  --chip-range-bg: rgba(51, 86, 127, 0.12); --chip-range-text: #33567f;
  --chip-glacier-bg: rgba(43, 106, 146, 0.12); --chip-glacier-text: #2b6a92;
  --glacier-text: #2b6a92;
  --glacier-badge: #2b6a92;
  --glacier-badge-halo: #ffffff;

  --gw-bg: #fdf1f1; --gw-border: #e5a29d; --gw-edge: #d9534f;
  --gw-text: #7a2e2e; --gw-title: #b3453c; --gw-strong: #5c1f1f;

  --aqi-good: #2e7d32; --aqi-moderate: #a67c00; --aqi-usg: #c05621;
  --aqi-unhealthy: #c62828; --aqi-very-unhealthy: #7b1fa2; --aqi-hazardous: #8e2437;

  --grade-text-a: #2e7d32; --grade-text-b: #558b2f; --grade-text-c: #a67c00;
  --grade-text-d: #c05621; --grade-text-f: #c62828;

  --overlay-bg: rgba(255, 255, 255, 0.88);
  --overlay-dim: rgba(242, 245, 248, 0.8);
  --err-card-bg: #ffffff;
  --map-label: rgba(38, 60, 80, 0.85);
  --map-label-halo: rgba(255, 255, 255, 0.7);
  --panel-shadow: rgba(23, 39, 51, 0.1);
}
```

- [ ] **Step 2: Convert `styles.scss`'s own rules to tokens.** Exact replacements in the existing rules:

| Location | Old | New |
|---|---|---|
| `html, body` | `background: #0d1620` | `background: var(--bg)` |
| `html, body` | `color: #e8eef3` | `color: var(--text)` |
| `.range-label` | `color: rgba(207, 229, 245, 0.75)` | `color: var(--map-label)` |
| `.range-label` | `text-shadow: 0 1px 2px rgba(0,0,0,0.6)` | `text-shadow: 0 1px 2px var(--map-label-halo)` |
| `.peak-marker .glacier-badge` | `color: #d6ecf7` | `color: var(--glacier-badge)` |
| `.peak-marker .glacier-badge` | `text-shadow: 0 0 2px #0b1620, 0 0 2px #0b1620` | `text-shadow: 0 0 2px var(--glacier-badge-halo), 0 0 2px var(--glacier-badge-halo)` |
| `.peak-popup .popup-name` | `color: #f5f8fb` | `color: var(--text)` |
| `.peak-popup .popup-sub` | `color: #9fb5d5` | `color: var(--muted)` |
| `.peak-popup .popup-glacier` | `color: #bcd9ea` | `color: var(--glacier-text)` |
| `.peak-popup .popup-grade` | `color: #0a1525` | `color: var(--ink-on-grade)` |
| `.peak-popup .popup-driver-positive` | `color: #3ecf78` | *leave literal (grade-green driver accent on map only; brand)* |
| `.peak-popup .popup-driver-negative` | `color: #e88848` | *leave literal (matches D-grade dot)* |
| `.peak-popup .popup-driver-neutral` | `color: #9fb5d5` | `color: var(--muted)` |
| `.peak-popup .popup-cta` | `background: #3a5a8a` | `background: var(--accent-strong)` |
| `.leaflet-popup-content-wrapper` | `background: #0a1525; color: #cfd9e8` | `background: var(--surface); color: var(--text-soft)` |
| `.leaflet-popup-tip` | `background: #0a1525` | `background: var(--surface)` |

Intentional near-merge consolidations in this file (dark-mode drift sanctioned): `#f5f8fb` → `--text` (#e8eef3), `#9fb5d5` → `--muted` (#8aa0b4, two occurrences), `#cfd9e8` → `--text-soft` (#cfd8dc). Without these, light mode would render near-white popup text on a light panel.

Leave untouched: `.peak-marker .dot` fills (`#3ecf78`…`#6f7a8e`), ghost `#3a4a62`, dot border `#ffffff`, `.popup-grade.grade-*` fills — brand constants.

- [ ] **Step 3: Add the pre-paint script to `src/index.html`** — insert directly after the `<link rel="manifest" href="site.webmanifest">` line, before `</head>`:

```html
  <script>
    // Stamp the theme before first paint so a stored light preference never
    // flashes dark. SettingsService adopts this same storage value on boot.
    (function () {
      try {
        var s = JSON.parse(localStorage.getItem('brw.settings.v1') || 'null') || {};
        var t = s.theme === 'system'
          ? (window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark')
          : s.theme;
        if (t === 'light') document.documentElement.setAttribute('data-theme', 'light');
      } catch (e) { /* default dark */ }
    })();
  </script>
```

- [ ] **Step 4: Verify**

Run: `npm test` → Expected: PASS (no behavior change).
Run: `npm run build` → Expected: success; no new budget warnings (global styles have no per-component budget). In a browser via the running dev server, the dark site must look pixel-identical; in DevTools, `document.documentElement.setAttribute('data-theme','light')` should visibly flip the page/popups (partial light coverage is expected until Tasks 6–7).

- [ ] **Step 5: Commit**

```bash
git add src/styles.scss src/index.html
git commit -m "feat(theme): design token layer and pre-paint theme script (dark unchanged)"
```

---

### Task 6: Tokenize `peak-detail.scss` + budget bump

**Files:**
- Modify: `src/app/pages/peak-detail/peak-detail.scss`
- Modify: `angular.json:53-56`

- [ ] **Step 1: Apply the replacement map to `peak-detail.scss`.** Every occurrence, mechanical:

| Old | New |
|---|---|
| `#e8eef3` | `var(--text)` |
| `#8aa0b4` | `var(--muted)` |
| `#cfd8dc` | `var(--text-soft)` |
| `#6f7a8e`, `#6b7e90`, `#41566b` | `var(--muted-deep)` |
| `#92a4b5` | `var(--muted)` |
| `#ef9a9a` | `var(--danger-text)` |
| `#6d4c00` | `var(--warn-bg)` |
| `#ffd54f` (in `.stale-chip`, `.ws-partial`) | `var(--warn-text)` |
| `#1a2632` | `var(--panel-raised)` |
| `#11202c` | `var(--panel)` |
| `#2c3e50` | `var(--border)` |
| `#243240` | `var(--border-soft)` |
| `#b6c2cf`, `#b0bec5` | `var(--text-soft)` |
| `#5fa8d8` | `var(--accent)` |
| `#cfe5f5` | `var(--accent-hover)` |
| `.grade-a { color: #81c784 }` … `.grade-f { color: #ef9a9a }` (hero grade words, lines 57–58) | `var(--grade-text-a)` … `var(--grade-text-f)` |
| `.pill-positive` trio `#1b3a1b`/`#a5d6a7`/`#2e7d32` | `var(--pill-pos-bg)`/`var(--pill-pos-text)`/`var(--pill-pos-border)` |
| `.pill-neutral` trio `#2a3340`/`#b0bec5`/`#455a64` | `var(--pill-neu-bg)`/`var(--pill-neu-text)`/`var(--pill-neu-border)` |
| `.pill-negative` trio `#4a1f1f`/`#ef9a9a`/`#c62828` | `var(--pill-neg-bg)`/`var(--pill-neg-text)`/`var(--pill-neg-border)` |
| AQI block `#81c784`/`#ffd54f`/`#ffb74d`/`#e57373`/`#ba68c8`/`#c75b6d` | `var(--aqi-good)`/`var(--aqi-moderate)`/`var(--aqi-usg)`/`var(--aqi-unhealthy)`/`var(--aqi-very-unhealthy)`/`var(--aqi-hazardous)` |
| glacier warning `#c62828`/`#ef5350`/`#2a1414`/`#ffd9d9`/`#ff8a80`/`strong { color: #fff }` | `var(--gw-border)`/`var(--gw-edge)`/`var(--gw-bg)`/`var(--gw-text)`/`var(--gw-title)`/`var(--gw-strong)` |

Context disambiguation: the hero `.grade-a`…`.grade-f` rules and `.ws-partial`/`.stale-chip` both use `#ffd54f` — the grade-word one becomes `var(--grade-text-c)`, the chip ones `var(--warn-text)`. `#2e7d32` appears only inside `.pill-positive` in this file.

- [ ] **Step 2: Bump the component-style budget.** In `angular.json`, the `anyComponentStyle` budget object:

```json
                {
                  "type": "anyComponentStyle",
                  "maximumWarning": "8kB",
                  "maximumError": "9kB"
                }
```

(Established policy: bump before churning existing rules. `var(--…)` names are longer than hex literals; peak-detail sits at ~6.6kB and lands ~7.0–7.2kB.)

- [ ] **Step 3: Verify**

Run: `npm run build`
Expected: success, no `anyComponentStyle` warnings. Note the reported peak-detail style size in the output.
Run: `npm test` → PASS.
Browser check: dark peak page pixel-identical; with `data-theme="light"` set manually, the peak page renders light (cards white, text dark, pills/AQI/glacier-banner recolored).

- [ ] **Step 4: Commit**

```bash
git add src/app/pages/peak-detail/peak-detail.scss angular.json
git commit -m "refactor(theme): tokenize peak-detail styles; raise component style budget"
```

---

### Task 7: Tokenize remaining component styles

**Files:**
- Modify: `src/app/components/route-card/route-card.scss`, `src/app/pages/map-home/map-home.scss`, `src/app/app.scss`, `src/app/components/route-grid/route-grid.scss`, `src/app/pages/about/about.scss`, `src/app/pages/diagnostics/diagnostics.scss`

- [ ] **Step 1: `route-card.scss`** — same pill trios as Task 6, plus:

| Old | New |
|---|---|
| `#1a2632` (card bg) | `var(--panel-raised)` |
| `#2c3e50` | `var(--border)` |
| `#e8eef3` | `var(--text)` |
| `#4a6075` (hover border) | `var(--muted-deep)` |
| `#92a4b5` | `var(--muted)` |
| `#6b7e90` | `var(--muted-deep)` |
| `#6d4c00` / `#ffd54f` (stale + aqi chips) | `var(--warn-bg)` / `var(--warn-text)` |
| `rgba(95, 168, 216, 0.18)` / `#cfe5f5` (range chip) | `var(--chip-range-bg)` / `var(--chip-range-text)` |
| `rgba(120, 170, 200, 0.16)` / `#cfe2ee` (glacier chip) | `var(--chip-glacier-bg)` / `var(--chip-glacier-text)` |

- [ ] **Step 2: `map-home.scss`**

| Old | New |
|---|---|
| `#0a1525` (map container bg) | `var(--surface)` |
| `rgba(10, 21, 37, 0.85)` (chips, search overlay) | `var(--overlay-bg)` |
| `rgba(10, 21, 37, 0.78)` (error dim) | `var(--overlay-dim)` |
| `#cfd9e8` (all occurrences) | `var(--text-soft)` |
| `#5fa8d8` (loading pulse) | `var(--accent)` |
| `rgba(95, 168, 216, 0.4)` (search border) | `var(--chip-range-bg)` is wrong here — use `var(--accent)` at 40%: replace with `color-mix(in srgb, var(--accent) 40%, transparent)` |
| `rgba(95, 168, 216, 0.15)` (result hover) | `color-mix(in srgb, var(--accent) 15%, transparent)` |
| `#9fb5d5` (result-range) | `var(--muted)` |
| `#1a2942` (error card) | `var(--err-card-bg)` |
| `#c44` / `#d55` (error button) | `var(--danger-strong)` / `var(--danger-strong-hover)` |
| `#ff8a8a` (error h2) | `var(--danger-text)` |
| `#fff` (error card text) | `var(--text)` |
| `rgba(0, 0, 0, 0.4)` (card shadow) | `var(--panel-shadow)` |

`color-mix` is baseline-available in all evergreen browsers this app supports; it keeps the accent-derived translucents theme-correct.

- [ ] **Step 3: `app.scss`**

| Old | New |
|---|---|
| `#eef3f7` (brand) | `var(--text)` |
| `#5fa8d8` (accent) | `var(--accent)` |
| `#9fb2c4` (tagline) | `var(--muted)` |
| `#8aa0b4` / `#cfd8dc` (nav link + hover) | `var(--muted)` / `var(--text-soft)` |
| `#c5e1a5` (active link) | `var(--nav-active)` |

- [ ] **Step 4: `route-grid.scss`**

| Old | New |
|---|---|
| `#e8eef3`, `#e6edf3` | `var(--text)` |
| `#1a2532` (search input bg) | `var(--panel-raised)` |
| `#2c3a4a` | `var(--border)` |
| `#6b7c8e` (placeholder) | `var(--muted-deep)` |
| `#4a90e2` (focus border) | `var(--focus)` |
| `#8aa0b4`, `#92a4b5`, `#9fb5d5` | `var(--muted)` |
| `#ef9a9a` | `var(--danger-text)` |
| `#2e7d32` (retry button bg) | *leave literal — action-green button, brand-adjacent, reads fine on both themes* |

- [ ] **Step 5: `about.scss`**

| Old | New |
|---|---|
| `#e8eef3` | `var(--text)` |
| `#8aa0b4` | `var(--muted)` |
| `#cfd8dc` | `var(--text-soft)` |
| `#b0bec5` | `var(--text-soft)` |
| `#11202c` / `#2c3e50` (block) | `var(--panel)` / `var(--border)` |
| `#1a2632` / `#2c3a4a` (pill-list li) | `var(--panel-raised)` / `var(--border)` |
| `#80cbc4` (email) | `var(--teal)` |

- [ ] **Step 6: `diagnostics.scss`**

| Old | New |
|---|---|
| `#e8eef3` | `var(--text)` |
| `#8aa0b4` | `var(--muted)` |
| `#cfd8dc` | `var(--text-soft)` |
| `#b0bec5` | `var(--text-soft)` |
| `#ef9a9a` | `var(--danger-text)` |
| `#1a2632` (refresh bg AND td border) | `var(--panel-raised)` (bg), `var(--border-soft)` (the `th, td` border-bottom) |
| `#2c3e50` | `var(--border)` |
| `#22313f` (refresh hover) | `var(--panel-hover)` |
| `#11202c` (`.calls` bg AND `.today-tag` text) | `var(--panel)` (bg), `var(--ink-on-grade)` (tag text on teal) |
| `#80cbc4` (today row + tag bg) | `var(--teal)` |

- [ ] **Step 7: Verify no strays remain**

Run (from `frontend/`):
```bash
grep -rn --include=*.scss -E '#[0-9a-fA-F]{3,8}\b|rgba?\(' src/app | grep -v grade-badge.scss | grep -v consensus-badge.scss
```
Expected remaining matches ONLY: grade/driver dot fills in nothing (they live in `styles.scss`), the `#2e7d32` retry button in `route-grid.scss`, `rgba(...)` inside `color-mix(...)` lines in `map-home.scss` — anything else is a miss; fix it.

Run: `npm test` → PASS. `npm run build` → success, no budget warnings.
Browser check: flip `data-theme` in DevTools on `/`, `/all`, a peak page, `/about`, `/diagnostics` — everything recolors; dark remains pixel-identical without the attribute.

- [ ] **Step 8: Commit**

```bash
git add src/app/components/route-card/route-card.scss src/app/pages/map-home/map-home.scss src/app/app.scss src/app/components/route-grid/route-grid.scss src/app/pages/about/about.scss src/app/pages/diagnostics/diagnostics.scss
git commit -m "refactor(theme): tokenize remaining component styles"
```

---

### Task 8: SettingsMenu component + header wiring

**Files:**
- Create: `src/app/components/settings-menu/settings-menu.ts`, `.html`, `.scss`
- Test: `src/app/components/settings-menu/settings-menu.spec.ts`
- Modify: `src/app/app.ts`, `src/app/app.html`

- [ ] **Step 1: Write the failing test**

```ts
// src/app/components/settings-menu/settings-menu.spec.ts
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SettingsMenu } from './settings-menu';
import { SettingsService } from '../../services/settings';
import { STORAGE_KEY } from '../../services/settings-defaults';

describe('SettingsMenu', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [SettingsMenu],
      providers: [provideRouter([])],
    });
  });

  function open() {
    const fixture = TestBed.createComponent(SettingsMenu);
    fixture.detectChanges();
    gear(fixture).click();
    fixture.detectChanges();
    return fixture;
  }
  const gear = (f: any) => (f.nativeElement as HTMLElement).querySelector('button.gear') as HTMLButtonElement;
  const panel = (f: any) => (f.nativeElement as HTMLElement).querySelector('.settings-panel');

  it('renders a closed gear button with correct aria wiring', () => {
    const fixture = TestBed.createComponent(SettingsMenu);
    fixture.detectChanges();
    const btn = gear(fixture);
    expect(btn).toBeTruthy();
    expect(btn.getAttribute('aria-expanded')).toBe('false');
    expect(btn.getAttribute('aria-controls')).toBe('settings-panel');
    expect(panel(fixture)).toBeNull();
  });

  it('opens the panel with preset buttons, six unit rows, and the theme row', () => {
    const fixture = open();
    expect(gear(fixture).getAttribute('aria-expanded')).toBe('true');
    const p = panel(fixture)!;
    expect(p.querySelectorAll('.preset').length).toBe(2);
    expect(p.querySelectorAll('.setting-row').length).toBe(6);
    expect(p.querySelectorAll('.theme-row .seg').length).toBe(3);
  });

  it('Metric preset flips every unit signal and persists', () => {
    const fixture = open();
    const metric = Array.from(panel(fixture)!.querySelectorAll('.preset'))
      .find(b => b.textContent!.includes('Metric')) as HTMLButtonElement;
    metric.click();
    fixture.detectChanges();

    const svc = TestBed.inject(SettingsService);
    expect(svc.unitPreset()).toBe('metric');
    expect(svc.temperature()).toBe('C');
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!).windSpeed).toBe('kmh');
    expect(metric.classList).toContain('active');
  });

  it('a segmented control sets its own signal only', () => {
    const fixture = open();
    const cButton = panel(fixture)!.querySelector('.setting-row .seg[data-value="C"]') as HTMLButtonElement;
    cButton.click();
    fixture.detectChanges();
    const svc = TestBed.inject(SettingsService);
    expect(svc.temperature()).toBe('C');
    expect(svc.windSpeed()).toBe('mph');
    expect(svc.unitPreset()).toBe('mixed');
  });

  it('theme buttons set the theme signal', () => {
    const fixture = open();
    (panel(fixture)!.querySelector('.theme-row .seg[data-value="light"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(TestBed.inject(SettingsService).theme()).toBe('light');
  });

  it('closes on Escape and on outside click', () => {
    let fixture = open();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(panel(fixture)).toBeNull();

    fixture = open();
    document.body.click();
    fixture.detectChanges();
    expect(panel(fixture)).toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot resolve `./settings-menu`.

- [ ] **Step 3: Write the component**

```ts
// src/app/components/settings-menu/settings-menu.ts
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { filter } from 'rxjs';
import { SettingsService } from '../../services/settings';
import { SiteSettings } from '../../services/settings-defaults';

interface MenuRow {
  key: Exclude<keyof SiteSettings, 'theme'>;
  label: string;
  options: { v: string; label: string }[];
}

@Component({
  selector: 'app-settings-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings-menu.html',
  styleUrl: './settings-menu.scss',
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'onEscape()',
  },
})
export class SettingsMenu {
  readonly u = inject(SettingsService);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly gearBtn = viewChild<ElementRef<HTMLButtonElement>>('gearBtn');

  open = signal(false);

  readonly rows: MenuRow[] = [
    { key: 'temperature', label: 'Temperature', options: [{ v: 'F', label: '°F' }, { v: 'C', label: '°C' }] },
    { key: 'windSpeed', label: 'Wind', options: [{ v: 'mph', label: 'mph' }, { v: 'kmh', label: 'km/h' }, { v: 'ms', label: 'm/s' }] },
    { key: 'elevation', label: 'Elevation', options: [{ v: 'ft', label: 'ft' }, { v: 'm', label: 'm' }] },
    { key: 'snowDepth', label: 'Snow', options: [{ v: 'in', label: 'in' }, { v: 'cm', label: 'cm' }] },
    { key: 'visibility', label: 'Visibility', options: [{ v: 'mi', label: 'mi' }, { v: 'km', label: 'km' }] },
    { key: 'timeFormat', label: 'Time', options: [{ v: '12h', label: '12h' }, { v: '24h', label: '24h' }] },
  ];
  readonly themeOptions = [
    { v: 'dark', label: 'Dark' }, { v: 'light', label: 'Light' }, { v: 'system', label: 'System' },
  ];

  constructor() {
    inject(Router).events.pipe(
      filter(e => e instanceof NavigationStart),
      takeUntilDestroyed(inject(DestroyRef)),
    ).subscribe(() => this.open.set(false));
  }

  toggle() { this.open.update(v => !v); }

  onDocumentClick(event: Event) {
    if (!this.open()) return;
    if (!this.host.nativeElement.contains(event.target as Node)) this.open.set(false);
  }

  onEscape() {
    if (!this.open()) return;
    this.open.set(false);
    this.gearBtn()?.nativeElement.focus();
  }
}
```

```html
<!-- src/app/components/settings-menu/settings-menu.html -->
<button #gearBtn type="button" class="gear" aria-label="Settings"
        [attr.aria-expanded]="open()" aria-controls="settings-panel"
        (click)="toggle()">⚙</button>

@if (open()) {
  <div class="settings-panel" id="settings-panel" role="group" aria-label="Site settings">
    <div class="preset-row">
      <button type="button" class="preset" [class.active]="u.unitPreset() === 'imperial'"
              (click)="u.applyPreset('imperial')">Imperial</button>
      <button type="button" class="preset" [class.active]="u.unitPreset() === 'metric'"
              (click)="u.applyPreset('metric')">Metric</button>
    </div>

    @for (row of rows; track row.key) {
      <div class="setting-row" role="group" [attr.aria-label]="row.label">
        <span class="row-label">{{ row.label }}</span>
        <span class="seg-group">
          @for (opt of row.options; track opt.v) {
            <button type="button" class="seg" [attr.data-value]="opt.v"
                    [class.active]="u.valueFor(row.key) === opt.v"
                    (click)="u.setFromMenu(row.key, opt.v)">{{ opt.label }}</button>
          }
        </span>
      </div>
    }

    <div class="theme-row" role="group" aria-label="Theme">
      <span class="row-label">Theme</span>
      <span class="seg-group">
        @for (opt of themeOptions; track opt.v) {
          <button type="button" class="seg" [attr.data-value]="opt.v"
                  [class.active]="u.theme() === opt.v"
                  (click)="u.setFromMenu('theme', opt.v)">{{ opt.label }}</button>
        }
      </span>
    </div>
  </div>
}
```

```scss
// src/app/components/settings-menu/settings-menu.scss
:host {
  position: relative;
  display: inline-flex;
}

.gear {
  background: none;
  border: 1px solid transparent;
  border-radius: 0.4rem;
  padding: 0.1rem 0.4rem;
  font-size: 1rem;
  line-height: 1.4;
  cursor: pointer;
  color: var(--muted);

  &:hover { color: var(--text-soft); }
  &[aria-expanded='true'] { color: var(--text); border-color: var(--border); }
}

.settings-panel {
  position: absolute;
  top: calc(100% + 0.5rem);
  right: 0;
  z-index: 1200; // above Leaflet overlays (map chips sit at 1000-1100)
  width: 15rem;
  background: var(--panel);
  border: 1px solid var(--border);
  border-radius: 0.6rem;
  padding: 0.7rem;
  box-shadow: 0 8px 24px var(--panel-shadow);
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
  font-size: 0.8rem;
}

.preset-row {
  display: flex;
  gap: 0.35rem;
  padding-bottom: 0.45rem;
  border-bottom: 1px solid var(--border-soft);
}

.preset {
  flex: 1;
  padding: 0.3rem 0;
  border-radius: 0.4rem;
  border: 1px solid var(--border);
  background: var(--panel-raised);
  color: var(--muted);
  font: inherit;
  cursor: pointer;

  &.active { background: var(--accent-strong); border-color: var(--accent-strong); color: #fff; }
}

.setting-row, .theme-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.theme-row {
  padding-top: 0.45rem;
  border-top: 1px solid var(--border-soft);
}

.row-label { color: var(--text-soft); }

.seg-group { display: inline-flex; gap: 0.2rem; }

.seg {
  padding: 0.2rem 0.5rem;
  border-radius: 0.35rem;
  border: 1px solid transparent;
  background: none;
  color: var(--muted);
  font: inherit;
  font-size: 0.75rem;
  cursor: pointer;

  &:hover { color: var(--text-soft); }
  &.active { background: var(--panel-raised); border-color: var(--border); color: var(--text); }
}

// Below the breakpoint the same DOM becomes a full-width sheet pinned to the
// top of the viewport (the header isn't fixed, so anchoring under it while
// scrolled isn't meaningful).
@media (max-width: 640px) {
  .settings-panel {
    position: fixed;
    inset: 0 0 auto 0;
    width: auto;
    max-height: 85vh;
    overflow-y: auto;
    border-radius: 0 0 0.6rem 0.6rem;
    padding: 0.9rem 1rem;
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Wire into the app shell.** `src/app/app.ts` becomes:

```ts
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SettingsMenu } from './components/settings-menu/settings-menu';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, RouterOutlet, SettingsMenu],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
```

In `src/app/app.html`, replace the nav block:

```html
    <nav class="hero-nav">
      <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }" class="about-link">Map</a>
      <a routerLink="/all" routerLinkActive="active" class="about-link">All peaks</a>
      <a routerLink="/about" routerLinkActive="active" class="about-link">About</a>
      <app-settings-menu />
    </nav>
```

In `src/app/app.scss`, inside `.hero-nav` add alignment so the gear baselines with the links:

```scss
  .hero-nav {
    padding-top: 0.5rem;
    display: flex;
    gap: 1.25rem;
    align-items: center;
```

- [ ] **Step 6: Verify**

Run: `npm test`
Expected: PASS — including the existing `app.spec.ts` (it only asserts the brand and absence of `h1`; the gear does not affect it).
Browser check: gear appears after About; opens/closes; changing Theme→Light flips the whole site live; narrow the window below 640px — the panel becomes a top sheet.

- [ ] **Step 7: Commit**

```bash
git add src/app/components/settings-menu src/app/app.ts src/app/app.html src/app/app.scss
git commit -m "feat(settings): settings menu in header (popover + mobile sheet)"
```

---

### Task 9: Convert peak-detail displays

**Files:**
- Modify: `src/app/pages/peak-detail/peak-detail.ts`, `src/app/pages/peak-detail/peak-detail.html`
- Test: `src/app/pages/peak-detail/peak-detail.spec.ts` (add a metric describe block)

- [ ] **Step 1: Write the failing test.** Append to the main `describe('PeakDetail')` block in `peak-detail.spec.ts` (before its closing `});`):

```ts
  describe('metric display mode', () => {
    async function renderMetric() {
      const svc = TestBed.inject(SettingsService);
      svc.applyPreset('metric');
      const fixture = TestBed.createComponent(PeakDetail);
      fixture.componentRef.setInput('slug', 'longs-peak');
      fixture.detectChanges();
      httpMock.expectOne('/api/routes/longs-peak').flush(detail());
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('converts the hero elevation to meters', async () => {
      const el = await renderMetric();
      expect(el.querySelector('.facts b')?.textContent).toContain('4,346');
      expect(el.querySelector('.facts b')?.textContent).toContain('m');
    });

    it('renders the forecast table in °C, km/h, and 24h time', async () => {
      const el = await renderMetric();
      const headers = Array.from(el.querySelectorAll('.forecast thead th')).map(th => th.textContent?.trim());
      expect(headers).toContain('°C');
      const firstRow = el.querySelector('.forecast tbody tr')!;
      expect(firstRow.textContent).toContain('4');            // 40°F → 4°C
      expect(firstRow.textContent).toContain('13 km/h');      // 8 mph → 12.87 → 13
      expect(firstRow.textContent).toMatch(/\d{2}:\d{2}/);    // 24h clock (TZ-agnostic)
      expect(firstRow.textContent).not.toContain('mph');
    });

    it('converts snowpack tiles to centimeters', async () => {
      const el = await renderMetric();
      const tiles = el.querySelector('.snowpack')!.textContent!;
      expect(tiles).toContain('3.0 cm');   // SWE 1.2 in
      expect(tiles).toContain('10.2 cm');  // depth 4.0 in
    });
  });
```

Add the import at the top of the spec file:

```ts
import { SettingsService } from '../../services/settings';
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — hero shows `14,259 ft`, table shows `°F`/`mph`, snowpack shows `"`.

- [ ] **Step 3: Update the component.** In `peak-detail.ts`:

Imports — extend line 2 area and component imports:

```ts
import { UNIT_PIPES } from '../../units/unit-pipes';
import { SettingsService } from '../../services/settings';
```

```ts
  imports: [DatePipe, DecimalPipe, GradeBadge, ConsensusBadge, Sparkline, RouterLink, UNIT_PIPES],
```

Inside the class (next to the other `inject` lines):

```ts
  readonly u = inject(SettingsService);
```

- [ ] **Step 4: Update the template.** Exact edits in `peak-detail.html`:

1. Hero facts (line 20):
```html
          <b>{{ p.summitElevationFt | elev: u.elevation() | number }} {{ u.elevLabel() }}</b><span class="dot">·</span>{{ p.routeName }}<span class="dot">·</span>Class {{ p.classDifficulty }}<span class="dot">·</span>{{ p.rangeName }}
```

2. Feels-like tile (line 171):
```html
          <span class="value">{{ skyNow()?.apparentTempF !== null && skyNow()?.apparentTempF !== undefined ? (skyNow()!.apparentTempF | temp: u.temperature() | number:'1.0-0') + u.tempLabel() : '—' }}</span>
```

3. Visibility tile (line 158):
```html
          <span class="value">{{ skyNow()?.visibilityMiles !== null && skyNow()?.visibilityMiles !== undefined ? (skyNow()!.visibilityMiles | dist: u.visibility() | number:'1.0-1') + ' ' + u.distLabel() : '—' }}</span>
```

4. Daylight tile (line 176):
```html
            <span class="value">{{ day.sunriseUtc | date: u.clockFmt() }}–{{ day.sunsetUtc | date: u.clockFmt() }}</span>
```

5. Snowpack tiles (lines 126, 130, 134):
```html
            <span class="value">{{ snow.snowWaterEquivalentIn | depth: u.snowDepth() | number:'1.1-1' }}{{ u.depthSuffix() }}</span>
```
```html
            <span class="value">{{ snow.snowDepthIn | depth: u.snowDepth() | number:'1.1-1' }}{{ u.depthSuffix() }}</span>
```
```html
            <span class="value">{{ snow.newSnowLast7DaysIn | depth: u.snowDepth() | number:'1.1-1' }}{{ u.depthSuffix() }}</span>
```

6. Hourly table header (line 190):
```html
            <tr><th>Time</th><th>{{ u.tempLabel() }}</th><th>Wind</th><th>Gust</th><th>Precip</th><th>Clouds</th><th>Conditions</th></tr>
```

7. Hourly table cells (lines 195–198):
```html
                <td>{{ h.time | date: u.hourFmt() }}</td>
                <td>{{ h.tempF | temp: u.temperature() | number:'1.0-0' }}</td>
                <td>{{ h.windMph | speed: u.windSpeed() | number:'1.0-0' }} {{ u.windLabel() }}</td>
                <td>@if (h.gustMph !== null) { {{ h.gustMph | speed: u.windSpeed() | number:'1.0-0' }} {{ u.windLabel() }} } @else { — }</td>
```

8. Per-source header (line 220):
```html
            <tr><th>Source</th><th>Max wind</th><th>Max gust</th><th>Min {{ u.tempLabel() }}</th><th>Max precip</th><th>CAPE</th></tr>
```

9. Per-source cells (lines 226–228):
```html
                <td>{{ s.windMph | speed: u.windSpeed() | number:'1.0-1' }} {{ u.windLabel() }}</td>
                <td>@if (s.maxGustMph !== null) { {{ s.maxGustMph | speed: u.windSpeed() | number:'1.0-1' }} {{ u.windLabel() }} } @else { — }</td>
                <td>{{ s.tempF | temp: u.temperature() | number:'1.0-1' }}</td>
```

10. Sources footer (lines 246–247):
```html
        <span>NWS: {{ (s.nws.fetchedAt | date: u.stampFmt()) ?? 'unavailable' }}</span>
        <span>SNOTEL: {{ (s.snotel.fetchedAt | date: u.stampFmt()) ?? 'unavailable' }}</span>
```

`sparklinePoints` stays unconverted (Deviation 1).

- [ ] **Step 5: Run tests to verify they pass**

Run: `npm test`
Expected: PASS — new metric block AND all pre-existing peak-detail specs (they run with default imperial settings; localStorage is not polluted because `applyPreset` is only called in the new block. If a pre-existing test fails on `14,259 ft` vs `14,259 ft` spacing, the hero edit changed `ft` from literal to `{{ u.elevLabel() }}` — output is identical; investigate before touching assertions).

Add to the spec's `beforeEach` (inside the existing one, first line) to isolate settings between tests:

```ts
    localStorage.clear();
```

- [ ] **Step 6: Commit**

```bash
git add src/app/pages/peak-detail/peak-detail.ts src/app/pages/peak-detail/peak-detail.html src/app/pages/peak-detail/peak-detail.spec.ts
git commit -m "feat(units): peak-detail renders in user-selected units and time format"
```

---

### Task 10: Convert route-card elevation

**Files:**
- Modify: `src/app/components/route-card/route-card.ts`, `src/app/components/route-card/route-card.html`
- Test: `src/app/components/route-card/route-card.spec.ts`

- [ ] **Step 1: Write the failing test.** Add to `route-card.spec.ts`, plus imports:

```ts
import { SettingsService } from '../../services/settings';
```

```ts
  it('renders elevation in meters when the elevation setting is metric', () => {
    TestBed.inject(SettingsService).set('elevation', 'm');
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', summary('foo'));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).querySelector('.route-name')?.textContent ?? '';
    expect(text).toContain('4,267 m');   // 14,000 ft
    expect(text).not.toContain(`'`);
  });

  it(`renders elevation with the feet tick by default`, () => {
    const fixture = TestBed.createComponent(RouteCard);
    fixture.componentRef.setInput('route', summary('foo'));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.route-name')?.textContent).toContain(`14,000'`);
  });
```

And add `localStorage.clear();` as the first line of the existing `beforeEach`.

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — card always renders `14,000'`.

- [ ] **Step 3: Implement.** `route-card.ts` — add imports and inject:

```ts
import { ElevPipe } from '../../units/unit-pipes';
import { SettingsService } from '../../services/settings';
```

```ts
  imports: [DecimalPipe, GradeBadge, ConsensusBadge, RouterLink, ElevPipe],
```

```ts
  readonly u = inject(SettingsService);
```

(`inject` is already imported? No — line 1 imports `computed, input`; extend it: `import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';`)

`route-card.html` line 5:

```html
      <p class="route-name">{{ route().routeName }} · Class {{ route().classDifficulty }} · {{ route().summitElevationFt | elev: u.elevation() | number }}{{ u.elevSuffix() }}</p>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS (default test sees `14,000'` — `elevSuffix()` is `'` for imperial, so output is byte-identical to before).

- [ ] **Step 5: Commit**

```bash
git add src/app/components/route-card/route-card.ts src/app/components/route-card/route-card.html src/app/components/route-card/route-card.spec.ts
git commit -m "feat(units): route-card elevation follows the elevation setting"
```

---

### Task 11: Map — themed tiles + unit-aware popups

**Files:**
- Modify: `src/app/pages/map-home/map-home.ts`
- Test: `src/app/pages/map-home/map-home.spec.ts`

- [ ] **Step 1: Write the failing tests.** In `map-home.spec.ts`, change the import line for map-home and add settings:

```ts
import { MapHome, markerIconSpec, popupHtml, tileUrlFor } from './map-home';
```

Append two describe blocks at file end:

```ts
describe('tileUrlFor', () => {
  it('selects the CARTO basemap matching the resolved theme', () => {
    expect(tileUrlFor('dark')).toBe('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png');
    expect(tileUrlFor('light')).toBe('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png');
  });
});
```

And update the existing `popupHtml` describe — the two existing tests change signature, plus one new test:

```ts
  it('adds a glaciated note when the route is glaciated', () => {
    expect(popupHtml(summary({ isGlaciated: true }), '12,000 ft')).toContain('Glaciated');
  });

  it('omits the glaciated note otherwise', () => {
    expect(popupHtml(summary({ isGlaciated: false }), '12,000 ft')).not.toContain('Glaciated');
  });

  it('renders the pre-formatted elevation text verbatim', () => {
    expect(popupHtml(summary(), '3,658 m')).toContain('3,658 m &middot; Class 2');
    expect(popupHtml(summary(), '3,658 m')).not.toContain('12,000');
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — `tileUrlFor` is not exported; `popupHtml` ignores the second argument (verbatim test fails).

- [ ] **Step 3: Implement in `map-home.ts`.**

Add imports:

```ts
import { SettingsService } from '../../services/settings';
import { formatElevationText } from '../../units/conversions';
```

(`effect` and `untracked` must be added to the `@angular/core` import list on line 1.)

Inside the class, next to the other `inject` lines:

```ts
  private settings = inject(SettingsService);
  private tileLayer: any | null = null;
```

In the constructor, after the `afterNextRender(() => this.initMap());` line:

```ts
    // Re-render markers when the elevation unit changes so popup text converts;
    // swap the basemap when the resolved theme changes. Both no-op until the
    // map exists and run only in the browser (initMap is browser-only anyway).
    effect(() => {
      this.settings.elevation();
      untracked(() => { void this.renderMarkers(); });
    });
    effect(() => {
      const theme = this.settings.resolvedTheme();
      untracked(() => this.tileLayer?.setUrl(tileUrlFor(theme)));
    });
```

In `initMap`, replace the `L.tileLayer(...)` statement (lines 221–225) with:

```ts
    this.tileLayer = L.tileLayer(tileUrlFor(this.settings.resolvedTheme()), {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 12,
    }).addTo(this.map);
```

In `renderMarkers`, the popup binding (line 345) becomes:

```ts
      marker.bindPopup(popupHtml(route, formatElevationText(route.summitElevationFt, this.settings.elevation())), { className: 'peak-popup' });
```

At module level, add the exported helper (above `popupHtml`):

```ts
export function tileUrlFor(theme: 'dark' | 'light'): string {
  return `https://{s}.basemaps.cartocdn.com/${theme === 'light' ? 'light_all' : 'dark_all'}/{z}/{x}/{y}{r}.png`;
}
```

Change `popupHtml`'s signature and the `.popup-sub` line:

```ts
export function popupHtml(route: RouteSummary, elevationText: string): string {
```
```ts
    <div class="popup-sub">${elevationText} &middot; Class ${escapeHtml(route.classDifficulty)}</div>
```

(`elevationText` is built by `formatElevationText` from trusted numeric + enum inputs — no escaping needed; keep `escapeHtml` on the API-sourced strings.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test`
Expected: PASS — including all pre-existing MapHome specs (the new effects no-op in jsdom: `initMap` never completes without a real Leaflet map, `tileLayer` stays null, `renderMarkers` returns early on `!this.map`).

- [ ] **Step 5: Commit**

```bash
git add src/app/pages/map-home/map-home.ts src/app/pages/map-home/map-home.spec.ts
git commit -m "feat(map): themed CARTO basemap and unit-aware popup elevation"
```

---

### Task 12: Full verification pass

**Files:** none (verification only)

- [ ] **Step 1: Full suite + production build**

Run (from `frontend/`): `npm test`
Expected: all suites PASS, zero skips.

Run: `npm run build`
Expected: success; no budget warnings; prerender completes for all routes (no `window`/`localStorage` access outside browser guards — a failure here means a guard is missing).

- [ ] **Step 2: Manual browser checklist** (against the running dev server — do NOT start it yourself; it's already running in the user's terminal):

1. Dark default: site looks identical to before this branch (spot-check `/`, a peak page, `/all`, `/about`).
2. Gear → Metric: hero elevation, forecast table (°C, km/h, 24h times), snowpack (cm), visibility (km) all flip live; map popups show meters after reopening a popup.
3. Gear → Theme → Light: whole page recolors including Leaflet popups and basemap tiles; reload → still light with **no dark flash** (pre-paint script).
4. Theme → System: follows OS setting; toggling the OS theme live-switches the site.
5. Narrow window < 640px: panel renders as a top sheet; Escape and outside-tap close it.
6. DevTools → Application → Local Storage: `brw.settings.v1` appears only after changing a setting; corrupt it by hand (`{{{`) and reload → site works with defaults.
7. DevTools Sensors/locale override (or `Object.defineProperty(navigator, 'language', ...)` in console before load is unreliable — instead temporarily clear storage and set the browser UI language to e.g. German): first visit shows metric + dark.
8. Hydration: open a peak page with the console open — no NG05xx hydration warnings.

- [ ] **Step 3: Push and hand off**

```bash
git push -u origin feature/user-options
```

Open a PR `feature/user-options` → `dev` (per branching policy). PR description must mention the two spec deviations (sparkline no-op skip; eager theme load) and the budget bump. Verify SSR-sensitive items (theme flash, hydration) on the dev Pages preview after merge — jsdom cannot see them.

---

## Self-Review (completed at write time)

**Spec coverage:** settings model/resolution → Tasks 2–3; conversion functions/pipes → Tasks 1, 4; tokens + light palette + pre-paint script + budget → Tasks 5–7; menu component/placement/a11y → Task 8; every display touchpoint (peak-detail, route-card, popups) → Tasks 9–11; map tile swap → Task 11; testing plan → embedded per task; error handling (storage, matchMedia, corrupt JSON) → Task 3 + verification 6. Sparkline conversion deliberately dropped (Deviation 1). Diagnostics SCSS tokenized but units untouched (spec: internal tooling stays imperial).
**Placeholder scan:** no TBDs; all code steps carry code; all commands carry expected outcomes.
**Type consistency:** unit types live in `conversions.ts` and are re-exported through `settings-defaults.ts` usage; service API (`set`, `setFromMenu`, `valueFor`, `applyPreset`, `applyStored`, `unitPreset`, labels/formats) matches every call site in Tasks 8–11; `popupHtml(route, elevationText)` matches all updated tests and the Task 11 call site; `tileUrlFor` exported where the spec block imports it.
