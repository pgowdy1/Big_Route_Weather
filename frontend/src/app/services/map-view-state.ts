import { Injectable } from '@angular/core';

export interface MapView {
  center: [number, number];
  zoom: number;
}

/**
 * In-memory store for the map's last zoom/center. Root singleton, so it survives
 * client-side navigation (map -> peak -> back) but resets on a hard reload —
 * mid-session you return to where you were; a fresh visit gets the default view.
 */
@Injectable({ providedIn: 'root' })
export class MapViewState {
  private view: MapView | null = null;

  save(view: MapView): void {
    this.view = view;
  }

  load(): MapView | null {
    return this.view;
  }
}
