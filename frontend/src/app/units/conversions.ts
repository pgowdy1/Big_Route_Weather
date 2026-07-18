// All API values are imperial-canonical; converters take the imperial value and
// the user's target unit. Rounding/formatting stays with Angular's number pipe.
export type TempUnit = 'F' | 'C';
export type SpeedUnit = 'mph' | 'kmh' | 'ms';
export type ElevUnit = 'ft' | 'm';
export type DepthUnit = 'in' | 'cm';
export type DistUnit = 'mi' | 'km';

export function convertTemp(f: number, unit: TempUnit): number {
  return unit === 'C' ? (f - 32) * 5 / 9 : f;
}

export function convertSpeed(mph: number, unit: SpeedUnit): number {
  if (unit === 'kmh') return mph * 1.609344;
  if (unit === 'ms') return mph * 0.44704;
  return mph;
}

export function convertElev(ft: number, unit: ElevUnit): number {
  return unit === 'm' ? ft * 0.3048 : ft;
}

export function convertDepth(inches: number, unit: DepthUnit): number {
  return unit === 'cm' ? inches * 2.54 : inches;
}

export function convertDist(mi: number, unit: DistUnit): number {
  return unit === 'km' ? mi * 1.609344 : mi;
}

// For map popups, which build HTML strings outside Angular's pipe pipeline.
// en-US pinned to match route-meta.ts and keep output deterministic in tests.
export function formatElevationText(ft: number, unit: ElevUnit): string {
  return `${Math.round(convertElev(ft, unit)).toLocaleString('en-US')} ${unit}`;
}
