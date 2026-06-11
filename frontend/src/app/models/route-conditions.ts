export type Grade = 'A' | 'B' | 'C' | 'D' | 'F';
export type DriverSeverity = 'positive' | 'neutral' | 'negative';
export type ConsensusLevel = 'high' | 'medium' | 'low';

export interface Driver {
  label: string;
  severity: DriverSeverity;
}

export interface Consensus {
  level: ConsensusLevel;
  worstFactor: string | null;
  coefficientOfVariationByFactor: Record<string, number>;
  sourcesReporting: number;
  sourcesAttempted: number;
}

export interface PerSourceForecast {
  sourceName: string;
  windMph: number;
  tempF: number;
  precipitationProbabilityPct: number | null;
  maxGustMph: number | null;
  maxCapeJkg: number | null;
  fetchedAt: string;
}

export interface RoutePosition {
  slug: string;
  mountain: string;
  summitLat: number;
  summitLon: number;
  rangeSlug: string;
}

export interface RouteSummary {
  slug: string;
  mountain: string;
  routeName: string;
  summitElevationFt: number;
  classDifficulty: string;
  rangeSlug: string;
  rangeName: string;
  summitLat: number;
  summitLon: number;
  grade: Grade | null;
  overallScore: number | null;
  drivers: Driver[];
  updatedAt: string;
  isStale: boolean;
  consensus: Consensus | null;
  airQualityUsAqi: number | null;
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
  gustMph: number | null;
  capeJkg: number | null;
  precipitationIn: number | null;
  cloudCoverPct: number | null;
  visibilityMiles: number | null;
  apparentTempF: number | null;
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

export interface AirQuality {
  usAqi: number;
  pm25: number;
  fetchedAt: string | null;
}

export interface Daylight {
  sunriseUtc: string;
  sunsetUtc: string;
  daylightHours: number;
}

export interface RouteDetail extends RouteSummary {
  factors: FactorScore[];
  rationale: string;
  forecastNext48h: HourlyForecast[] | null;
  snowpack: SnowpackSnapshot | null;
  windowGrades: WindowGrades | null;
  sources: DetailSources;
  perSourceForecast: PerSourceForecast[] | null;
  airQuality: AirQuality | null;
  daylight: Daylight | null;
}
