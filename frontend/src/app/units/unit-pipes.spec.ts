import { TempPipe, SpeedPipe, ElevPipe, DepthPipe, DistPipe } from './unit-pipes';

describe('unit pipes', () => {
  it('convert to the requested unit and pass imperial through', () => {
    expect(new TempPipe().transform(40, 'F')).toBe(40);
    expect(new TempPipe().transform(40, 'C')).toBeCloseTo(4.444, 3);
    expect(new SpeedPipe().transform(8, 'kmh')).toBeCloseTo(12.875, 3);
    expect(new ElevPipe().transform(14259, 'm')).toBeCloseTo(4346.14, 2);
    expect(new DepthPipe().transform(1.2, 'cm')).toBeCloseTo(3.048, 3);
    expect(new DistPipe().transform(10, 'km')).toBeCloseTo(16.093, 3);
  });
});
