import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RouteSummary, RouteDetail } from '../models/route-conditions';

@Injectable({ providedIn: 'root' })
export class RoutesService {
  private http = inject(HttpClient);

  list(): Observable<RouteSummary[]> {
    return this.http.get<RouteSummary[]>('/api/routes');
  }

  listRefresh(): Observable<RouteSummary[]> {
    return this.http.get<RouteSummary[]>('/api/routes/refresh');
  }

  detail(slug: string): Observable<RouteDetail> {
    return this.http.get<RouteDetail>(`/api/routes/${slug}`);
  }

  detailRefresh(slug: string): Observable<RouteDetail> {
    return this.http.get<RouteDetail>(`/api/routes/${slug}/refresh`);
  }
}
