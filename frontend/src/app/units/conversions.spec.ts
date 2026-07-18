import { convertTemp, convertSpeed, convertElev, convertDepth, convertDist, formatElevationText } from './conversions';

describe('conversions', () => {
  it('temperature: F passthrough, F→C', () => {
    expect(convertTemp(40, 'F')).toBe(40);
    expect(convertTemp(32, 'C')).toBe(0);
    expect(convertTemp(40, 'C')).toBeCloseTo(4.444, 3);
    expect(convertTemp(-40, 'C')).toBe(-40);
  });

  it('speed: mph passthrough, mph→km/h, mph→m/s', () => {
    expect(convertSpeed(8, 'mph')).toBe(8);
    expect(convertSpeed(8, 'kmh')).toBeCloseTo(12.875, 3);
    expect(convertSpeed(8, 'ms')).toBeCloseTo(3.576, 3);
  });

  it('elevation: ft passthrough, ft→m', () => {
    expect(convertElev(14259, 'ft')).toBe(14259);
    expect(convertElev(14259, 'm')).toBeCloseTo(4346.14, 2);
  });

  it('depth: in passthrough, in→cm', () => {
    expect(convertDepth(1.2, 'in')).toBe(1.2);
    expect(convertDepth(1.2, 'cm')).toBeCloseTo(3.048, 3);
    expect(convertDepth(4.0, 'cm')).toBeCloseTo(10.16, 2);
  });

  it('distance: mi passthrough, mi→km', () => {
    expect(convertDist(10, 'mi')).toBe(10);
    expect(convertDist(10, 'km')).toBeCloseTo(16.093, 3);
  });

  it('formatElevationText renders a localized rounded value with its unit', () => {
    expect(formatElevationText(14259, 'ft')).toBe('14,259 ft');
    expect(formatElevationText(14259, 'm')).toBe('4,346 m');
  });
});
