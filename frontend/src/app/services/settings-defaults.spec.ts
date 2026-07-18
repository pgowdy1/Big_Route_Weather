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
