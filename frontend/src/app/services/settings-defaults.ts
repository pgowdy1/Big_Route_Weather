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
