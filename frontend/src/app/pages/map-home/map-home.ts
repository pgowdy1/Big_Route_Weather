import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-map-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map-home.html',
  styleUrl: './map-home.scss',
})
export class MapHome {}
