import {
  insertThresholdBoundary,
  moveThresholdBoundary,
  readThresholds,
  removeThresholdBand,
  thresholdAxis,
  snapThresholdValue,
  thresholdBandAt,
  thresholdStepDecimals,
  recolorThresholdBand,
  type ThresholdsValue,
} from './thresholds.util';

interface FixtureCase {
  name: string;
  value: unknown;
  normalized?: unknown;
}

declare const __loadThresholdFixture: () => unknown;

const fixture = __loadThresholdFixture() as { valid: FixtureCase[]; invalid: FixtureCase[] };

const fourBands = (): ThresholdsValue => ({
  bands: [
    { id: 'green', color: '#34c759' },
    { id: 'yellow', color: '#ffcc00', from: 26 },
    { id: 'orange', color: '#ff9500', from: 63 },
    { id: 'red', color: '#ff3b30', from: 90 },
  ],
});

describe('readThresholds', () => {
  for (const testCase of fixture.valid) {
    it(`accepts and normalises the shared valid case "${testCase.name}"`, () => {
      expect(readThresholds(testCase.value)).toEqual(testCase.normalized as ThresholdsValue);
    });
  }

  for (const testCase of fixture.invalid) {
    it(`rejects the shared invalid case "${testCase.name}"`, () => {
      expect(readThresholds(testCase.value)).toBeNull();
    });
  }

  it('rejects more than 64 bands', () => {
    const bands = Array.from({ length: 65 }, (_, i) => (i === 0 ? { id: 'b0', color: '#000000' } : { id: `b${i}`, color: '#000000', from: i }));
    expect(readThresholds({ bands })).toBeNull();
  });
});

describe('thresholdBandAt', () => {
  it('picks the band whose range holds the value, boundaries belonging to the upper band', () => {
    const value = fourBands();
    expect(thresholdBandAt(value, 0)?.id).toBe('green');
    expect(thresholdBandAt(value, 25.9)?.id).toBe('green');
    expect(thresholdBandAt(value, 26)?.id).toBe('yellow');
    expect(thresholdBandAt(value, 90)?.id).toBe('red');
  });

  it('extends the outer bands beyond the scale and answers nothing for NaN', () => {
    const value = fourBands();
    expect(thresholdBandAt(value, -500)?.id).toBe('green');
    expect(thresholdBandAt(value, 1e9)?.id).toBe('red');
    expect(thresholdBandAt(value, Number.NaN)).toBeNull();
  });
});

describe('moving a boundary', () => {
  it('never lets a handle cross or touch its neighbours', () => {
    const moved = moveThresholdBoundary(fourBands(), 2, 10, 1);
    expect(moved.bands[2].from).toBe(27);
    const up = moveThresholdBoundary(fourBands(), 2, 99, 1);
    expect(up.bands[2].from).toBe(89);
  });

  it('lets the outer boundaries leave the scale, since the axis widens to show them', () => {
    const moved = moveThresholdBoundary(fourBands(), 3, 140, 1);
    expect(moved.bands[3].from).toBe(140);
    expect(thresholdAxis(moved, 0, 100)).toEqual({ start: 0, end: 140 });
  });
});

describe('adding and removing bands', () => {
  it('inserts a boundary where the user clicked, splitting the band it falls in', () => {
    const added = insertThresholdBoundary(fourBands(), 40, 1, { id: 'lime', color: '#0F0' });
    expect(added?.bands.map(band => [band.id, band.from])).toEqual([
      ['green', undefined],
      ['yellow', 26],
      ['lime', 40],
      ['orange', 63],
      ['red', 90],
    ]);
    expect(added?.bands[2].color).toBe('#00ff00');
    expect(insertThresholdBoundary(fourBands(), 10, 1, { id: 'low', color: '#000' })?.bands[1].from).toBe(10);
  });

  it('refuses a boundary closer than a step to its neighbours, or past the cap', () => {
    expect(insertThresholdBoundary(fourBands(), 26.5, 1, { id: 'x', color: '#000' })).toBeNull();
    expect(insertThresholdBoundary(fourBands(), 62.5, 1, { id: 'x', color: '#000' })).toBeNull();
    expect(insertThresholdBoundary(fourBands(), 40, 1, { id: 'x', color: '#000' }, 4)).toBeNull();
    expect(insertThresholdBoundary(fourBands(), 40, 1, { id: 'green', color: '#000' })).toBeNull();
  });

  it('lets the neighbour below absorb a removed band, and the next band start the range when the first goes', () => {
    expect(removeThresholdBand(fourBands(), 2)?.bands.map(band => [band.id, band.from])).toEqual([
      ['green', undefined],
      ['yellow', 26],
      ['red', 90],
    ]);
    const firstRemoved = removeThresholdBand(fourBands(), 0);
    expect(firstRemoved?.bands[0]).toEqual({ id: 'yellow', color: '#ffcc00' });
    expect(readThresholds(firstRemoved)).not.toBeNull();
  });

  it('never removes the last band', () => {
    expect(removeThresholdBand({ bands: [{ id: 'a', color: '#000000' }] }, 0)).toBeNull();
  });

  it('recolours one band and ignores a value that is not a colour', () => {
    expect(recolorThresholdBand(fourBands(), 1, '#ABC').bands[1]).toEqual({ id: 'yellow', color: '#aabbcc', from: 26 });
    expect(recolorThresholdBand(fourBands(), 1, 'red').bands[1].color).toBe('#ffcc00');
  });
});

describe('snapping to a step', () => {
  it('keeps the decimals of a step written in exponent notation', () => {
    expect(thresholdStepDecimals(1e-7)).toBe(7);
    expect(thresholdStepDecimals(2.5e-6)).toBe(7);
    expect(thresholdStepDecimals(0.25)).toBe(2);
    expect(snapThresholdValue(0.00000123, 1e-7, 0)).toBe(0.0000012);
  });
});
