import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RangesService } from './ranges-service';
import { RangeMeta } from '../models/range';

describe('RangesService', () => {
  let service: RangesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RangesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('GETs /api/ranges and returns the catalog', () => {
    const expected: RangeMeta[] = [{
      slug: 'cascades',
      name: 'Cascade Range',
      color: '#5fa8d8',
      description: 'PNW volcanoes',
      displayOrder: 1,
      perimeterGeoJson: { type: 'Polygon', coordinates: [[[0, 0], [1, 0], [1, 1], [0, 0]]] },
    }];

    let received: RangeMeta[] | null = null;
    service.list().subscribe(r => received = r);

    const req = httpMock.expectOne('/api/ranges');
    expect(req.request.method).toBe('GET');
    req.flush(expected);

    expect(received).toEqual(expected);
  });
});
