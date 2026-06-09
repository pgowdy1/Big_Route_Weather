import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RoutePosition, RouteSummary, RouteDetail } from '../models/route-conditions';

@Injectable({ providedIn: 'root' })
export class RoutesService {
  private http = inject(HttpClient);

  list(): Observable<RouteSummary[]> {
    return this.http.get<RouteSummary[]>('/api/routes');
  }

  positions(): Observable<RoutePosition[]> {
    return this.http.get<RoutePosition[]>('/api/routes/positions');
  }

  detail(slug: string): Observable<RouteDetail> {
    return this.http.get<RouteDetail>(`/api/routes/${slug}`);
  }
}
