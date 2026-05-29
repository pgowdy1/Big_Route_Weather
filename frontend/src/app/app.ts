import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouteGrid } from './components/route-grid/route-grid';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouteGrid],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
