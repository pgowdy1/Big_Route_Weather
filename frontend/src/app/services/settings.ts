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

  // Note: a set() before afterNextRender's applyStored() would persist current
  // state and shadow yet-unread stored prefs. Unreachable today (no consumer
  // can write pre-first-paint); revisit if a pre-paint caller ever appears.
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
