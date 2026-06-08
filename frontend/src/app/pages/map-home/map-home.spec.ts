import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MapHome } from './map-home';

describe('MapHome', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MapHome],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests routes and ranges on init', () => {
    TestBed.createComponent(MapHome).detectChanges();
    httpMock.expectOne('/api/routes').flush([]);
    httpMock.expectOne('/api/ranges').flush([]);
  });

  it('exposes an error signal when either request fails', () => {
    const fixture = TestBed.createComponent(MapHome);
    fixture.detectChanges();
    // Flush the success first; forkJoin will cancel the still-pending ranges
    // subscription as soon as routes errors, so erroring routes first makes
    // the ranges TestRequest impossible to flush.
    httpMock.expectOne('/api/ranges').flush([]);
    httpMock.expectOne('/api/routes').error(new ProgressEvent('boom'));
    expect(fixture.componentInstance.error()).toBeTruthy();
  });
});
