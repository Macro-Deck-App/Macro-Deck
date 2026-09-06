import { hexToHsv, hsvToHex, normalizeHex } from './color-conversion';

describe('color-conversion', () => {
  describe('normalizeHex', () => {
    it('expands a three digit shorthand', () => {
      expect(normalizeHex('#0af')).toBe('#00aaff');
    });

    it('accepts a value without the leading hash and lowercases it', () => {
      expect(normalizeHex('EF4444')).toBe('#ef4444');
    });

    it('rejects anything that is not a hex colour', () => {
      expect(normalizeHex('')).toBeNull();
      expect(normalizeHex('#12')).toBeNull();
      expect(normalizeHex('#12345')).toBeNull();
      expect(normalizeHex('rebeccapurple')).toBeNull();
      expect(normalizeHex('$reset')).toBeNull();
    });
  });

  it('round-trips a colour through HSV and back', () => {
    for (const hex of ['#ef4444', '#22c55e', '#3b82f6', '#000000', '#ffffff', '#7f7f7f']) {
      const hsv = hexToHsv(hex);
      expect(hsv).not.toBeNull();
      expect(hsvToHex(hsv!)).toBe(hex);
    }
  });

  it('reads the primaries as their expected hue', () => {
    expect(hexToHsv('#ff0000')).toEqual({ h: 0, s: 1, v: 1 });
    expect(hexToHsv('#00ff00')).toEqual({ h: 120, s: 1, v: 1 });
    expect(hexToHsv('#0000ff')).toEqual({ h: 240, s: 1, v: 1 });
  });

  it('wraps a hue outside the circle back onto it', () => {
    expect(hsvToHex({ h: 360, s: 1, v: 1 })).toBe('#ff0000');
    expect(hsvToHex({ h: -120, s: 1, v: 1 })).toBe('#0000ff');
  });
});
