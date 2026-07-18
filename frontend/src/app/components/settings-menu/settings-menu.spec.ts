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
  const gear = (f: { nativeElement: unknown }) =>
    (f.nativeElement as HTMLElement).querySelector('button.gear') as HTMLButtonElement;
  const panel = (f: { nativeElement: unknown }) =>
    (f.nativeElement as HTMLElement).querySelector('.settings-panel');

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
