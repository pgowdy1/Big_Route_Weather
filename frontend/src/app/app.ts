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
