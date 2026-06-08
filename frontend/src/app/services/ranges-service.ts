import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RangeMeta } from '../models/range';

@Injectable({ providedIn: 'root' })
export class RangesService {
  private http = inject(HttpClient);

  list(): Observable<RangeMeta[]> {
    return this.http.get<RangeMeta[]>('/api/ranges');
  }
}
