import { MapViewState } from './map-view-state';

describe('MapViewState', () => {
  it('returns null before any view is saved', () => {
    expect(new MapViewState().load()).toBeNull();
  });

  it('round-trips the saved view', () => {
    const svc = new MapViewState();
    svc.save({ center: [40, -105], zoom: 9 });
    expect(svc.load()).toEqual({ center: [40, -105], zoom: 9 });
  });

  it('overwrites the previous view on re-save', () => {
    const svc = new MapViewState();
    svc.save({ center: [40, -105], zoom: 9 });
    svc.save({ center: [45, -110], zoom: 5 });
    expect(svc.load()).toEqual({ center: [45, -110], zoom: 5 });
  });
});
