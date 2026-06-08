import type { Polygon } from 'geojson';

export interface RangeMeta {
  slug: string;
  name: string;
  color: string;
  description: string | null;
  displayOrder: number;
  perimeterGeoJson: Polygon;
}
