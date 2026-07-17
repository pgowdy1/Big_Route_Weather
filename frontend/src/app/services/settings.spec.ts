import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SettingsService } from './settings';
import { STORAGE_KEY } from './settings-defaults';

describe('SettingsService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
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

  it('applyPreset(imperial) round-trips every unit field back from metric', () => {
    const s = create();
    s.applyPreset('metric');
    expect(s.unitPreset()).toBe('metric');
    s.applyPreset('imperial');
    expect(s.temperature()).toBe('F');
    expect(s.windSpeed()).toBe('mph');
    expect(s.elevation()).toBe('ft');
    expect(s.snowDepth()).toBe('in');
    expect(s.visibility()).toBe('mi');
    expect(s.timeFormat()).toBe('12h');
    expect(s.unitPreset()).toBe('imperial');
  });

  it('survives a storage write failure and keeps the change in memory', () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('quota');
    });
    const s = create();
    expect(() => s.set('temperature', 'C')).not.toThrow();
    expect(s.temperature()).toBe('C'); // in-memory settings still apply
    expect(setItem).toHaveBeenCalled(); // persist was attempted, not skipped
    setItem.mockRestore();
  });

  it('setFromMenu ignores values outside the allowed list', () => {
    const s = create();
    s.setFromMenu('windSpeed', 'knots');
    expect(s.windSpeed()).toBe('mph'); // rejected — unchanged
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull(); // nothing persisted
    // sanity: a valid value still flows through the same setter
    s.setFromMenu('windSpeed', 'kmh');
    expect(s.windSpeed()).toBe('kmh');
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!).windSpeed).toBe('kmh');
  });

  describe('system theme (live matchMedia)', () => {
    let capturedChange: ((e: MediaQueryListEvent) => void) | undefined;
    let removeSpy: ReturnType<typeof vi.fn>;

    beforeEach(() => {
      capturedChange = undefined;
      removeSpy = vi.fn();
      const mql = {
        matches: true,
        addEventListener: (_type: string, cb: (e: MediaQueryListEvent) => void) => {
          capturedChange = cb;
        },
        removeEventListener: removeSpy,
      };
      vi.stubGlobal('matchMedia', () => mql);
    });

    it('follows the OS preference for theme "system" and stamps data-theme live', async () => {
      const s = create();
      // matches:true → systemTheme is light, but the dark brand default holds
      // until the user explicitly opts into 'system'.
      expect(s.theme()).toBe('dark');
      expect(s.resolvedTheme()).toBe('dark');

      s.set('theme', 'system');
      expect(s.resolvedTheme()).toBe('light');
      await Promise.resolve(); // let the effect run (zoneless scheduler)
      TestBed.tick();
      expect(document.documentElement.getAttribute('data-theme')).toBe('light');

      // OS flips to dark → the live listener updates resolved theme + attribute.
      capturedChange!({ matches: false } as MediaQueryListEvent);
      expect(s.resolvedTheme()).toBe('dark');
      TestBed.tick();
      expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
    });

    it('removes the matchMedia listener when the injector is destroyed', () => {
      create();
      expect(removeSpy).not.toHaveBeenCalled();
      TestBed.resetTestingModule(); // fires DestroyRef.onDestroy
      expect(removeSpy).toHaveBeenCalledWith('change', expect.any(Function));
    });
  });
});
