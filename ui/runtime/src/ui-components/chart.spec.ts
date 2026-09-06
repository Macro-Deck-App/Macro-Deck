import { chartPaths } from './chart';

describe('chart paths', () => {
  it('draws nothing at all for an empty series', () => {
    expect(chartPaths([], 100, 50, 0)).toBeNull();
  });

  it('draws a single sample flat across the full width, not as a dot', () => {
    const paths = chartPaths([0.5], 100, 50, 0);

    expect(paths!.line).toBe('M0 25 L100 25');
  });

  it('puts the lowest value on the foot of the box and the highest at the top of the band', () => {
    const paths = chartPaths([0, 1], 100, 50, 0);

    expect(paths!.line).toBe('M0 50 L100 0');
  });

  it('keeps the top of the band clear by the reserved fraction', () => {
    // plotTop 0.4 of a 50px box reserves 20px, so a full value lands there rather than at 0.
    const paths = chartPaths([1], 100, 50, 0.4);

    expect(paths!.line).toBe('M0 20 L100 20');
  });

  it('spreads the samples evenly across the width', () => {
    const paths = chartPaths([0, 0, 0], 100, 50, 0);

    expect(paths!.line).toBe('M0 50 L50 50 L100 50');
  });

  it('closes the filled area to the box bottom at both ends', () => {
    const paths = chartPaths([1, 1], 100, 50, 0);

    expect(paths!.area.startsWith('M0 50 ')).toBeTrue();
    expect(paths!.area.endsWith(' L100 50 Z')).toBeTrue();
  });

  it('rounds coordinates so the emitted path stays short', () => {
    const paths = chartPaths([1 / 3, 2 / 3], 100, 50, 0);

    expect(/\d\.\d{3}/.test(paths!.line)).toBeFalse();
  });
});
