export type Grade = 'A' | 'B' | 'C' | 'D' | 'F';
export type DriverSeverity = 'positive' | 'neutral' | 'negative';

export interface Driver {
  label: string;
  severity: DriverSeverity;
}

export interface RouteSummary {
  slug: string;
  mountain: string;
  routeName: string;
  summitElevationFt: number;
  classDifficulty: string;
  grade: Grade | null;
  overallScore: number | null;
  drivers: Driver[];
  updatedAt: string;
  isStale: boolean;
}

export interface FactorScore {
  name: string;
  score: number;
  weight: number;
  detail: string;
}

export interface HourlyForecast {
  time: string;
  tempF: number;
  windMph: number;
  precipitationProbabilityPct: number;
  shortForecast: string;
}

export interface SnowpackSnapshot {
  snowWaterEquivalentIn: number;
  snowDepthIn: number;
  newSnowLast7DaysIn: number;
  percentOfNormalSwe: number;
}

export interface RouteDetail extends RouteSummary {
  factors: FactorScore[];
  rationale: string;
  forecastNext48h: HourlyForecast[] | null;
  snowpack: SnowpackSnapshot | null;
}
