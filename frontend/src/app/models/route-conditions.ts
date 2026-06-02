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
  isActive: boolean;
}

export interface HourlyForecast {
  time: string;
  tempF: number;
  windMph: number;
  precipitationProbabilityPct: number;
  shortForecast: string;
}

export interface DailyDepthPoint {
  date: string;
  depthIn: number;
}

export interface SnowpackSnapshot {
  snowWaterEquivalentIn: number;
  snowDepthIn: number;
  newSnowLast7DaysIn: number;
  percentOfNormalSwe: number;
  stationTriplet: string;
  dailyDepthIn: DailyDepthPoint[];
}

export interface WindowGrade {
  grade: Grade | null;
  overallScore: number | null;
  hoursCovered: number;
  factors: FactorScore[];
  drivers: Driver[];
  rationale: string;
}

export interface WindowGrades {
  next12h: WindowGrade;
  next24h: WindowGrade;
  next48h: WindowGrade;
}

export interface SourceFreshness {
  fetchedAt: string | null;
}

export interface DetailSources {
  nws: SourceFreshness;
  snotel: SourceFreshness;
}

export interface RouteDetail extends RouteSummary {
  summitLat: number;
  summitLon: number;
  factors: FactorScore[];
  rationale: string;
  forecastNext48h: HourlyForecast[] | null;
  snowpack: SnowpackSnapshot | null;
  windowGrades: WindowGrades | null;
  sources: DetailSources;
}
