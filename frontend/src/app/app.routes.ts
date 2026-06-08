import { Routes } from '@angular/router';
import { MapHome } from './pages/map-home/map-home';
import { RouteGrid } from './components/route-grid/route-grid';
import { PeakDetail } from './pages/peak-detail/peak-detail';
import { About } from './pages/about/about';
import { Diagnostics } from './pages/diagnostics/diagnostics';

export const routes: Routes = [
  { path: '', component: MapHome },
  { path: 'all', component: RouteGrid },
  { path: 'peak/:slug', component: PeakDetail },
  { path: 'about', component: About },
  { path: 'diagnostics', component: Diagnostics },
  { path: '**', redirectTo: '' },
];
