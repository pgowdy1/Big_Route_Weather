// Pure pipes: the unit argument comes from a SettingsService signal read in the
// template, so the changed argument busts memoization — the zoneless-safe idiom.
// They convert number → number; Angular's number/date pipes keep formatting.
import { Pipe, PipeTransform } from '@angular/core';
import {
  DepthUnit, DistUnit, ElevUnit, SpeedUnit, TempUnit,
  convertDepth, convertDist, convertElev, convertSpeed, convertTemp,
} from './conversions';

@Pipe({ name: 'temp' })
export class TempPipe implements PipeTransform {
  transform(valueF: number, unit: TempUnit): number { return convertTemp(valueF, unit); }
}

@Pipe({ name: 'speed' })
export class SpeedPipe implements PipeTransform {
  transform(valueMph: number, unit: SpeedUnit): number { return convertSpeed(valueMph, unit); }
}

@Pipe({ name: 'elev' })
export class ElevPipe implements PipeTransform {
  transform(valueFt: number, unit: ElevUnit): number { return convertElev(valueFt, unit); }
}

@Pipe({ name: 'depth' })
export class DepthPipe implements PipeTransform {
  transform(valueIn: number, unit: DepthUnit): number { return convertDepth(valueIn, unit); }
}

@Pipe({ name: 'dist' })
export class DistPipe implements PipeTransform {
  transform(valueMi: number, unit: DistUnit): number { return convertDist(valueMi, unit); }
}

export const UNIT_PIPES = [TempPipe, SpeedPipe, ElevPipe, DepthPipe, DistPipe] as const;
