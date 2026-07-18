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
