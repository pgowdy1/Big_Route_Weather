import { Routes } from '@angular/router';
import { RouteGrid } from './components/route-grid/route-grid';
import { PeakDetail } from './pages/peak-detail/peak-detail';

export const routes: Routes = [
  { path: '', component: RouteGrid },
  { path: 'peak/:slug', component: PeakDetail },
  { path: '**', redirectTo: '' },
];
