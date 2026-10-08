import { asUiLength, boxExtent, lengthReference, resolveLength, UiLength } from './length';

const scope = (basis: number, crossExtent: number | null, container: number | null) => ({ basis, crossExtent, container });

// The parser as released in 3.0.0-beta.15, before ofParent existed.
function previousAsUiLength(raw: unknown): UiLength | undefined {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return undefined;
  const candidate = raw as { basis?: unknown; maxOfCross?: unknown; maxOfCell?: unknown };
  if (typeof candidate.basis !== 'number' || !Number.isFinite(candidate.basis)) return undefined;
  const length: UiLength = { basis: candidate.basis };
  if (typeof candidate.maxOfCross === 'number' && Number.isFinite(candidate.maxOfCross)) length.maxOfCross = candidate.maxOfCross;
  if (typeof candidate.maxOfCell === 'number' && Number.isFinite(candidate.maxOfCell)) length.maxOfCell = candidate.maxOfCell;
  return length;
}

describe('lengths relative to the containing box', () => {
  it('resolves against the smaller side of a definite containing box', () => {
    expect(resolveLength({ basis: 0.01, ofParent: 0.1 }, scope(240, null, 54))).toBeCloseTo(5.4, 6);
  });

  it('ignores ofParent and uses the basis when the containing box is not definite', () => {
    expect(resolveLength({ basis: 0.05, ofParent: 0.5 }, scope(240, null, null))).toBeCloseTo(12, 6);
  });

  it('is zero for a zero fraction rather than falling back', () => {
    expect(resolveLength({ basis: 0.05, ofParent: 0 }, scope(240, null, 100))).toBe(0);
  });

  it('still clamps against the cross extent and the cell', () => {
    expect(resolveLength({ basis: 0.01, ofParent: 0.5, maxOfCross: 0.1 }, scope(240, 100, 80))).toBeCloseTo(10, 6);
    expect(resolveLength({ basis: 0.01, ofParent: 0.5, maxOfCell: 0.1 }, scope(240, null, 80))).toBeCloseTo(12, 6);
  });

  it('leaves a length without ofParent exactly as it was', () => {
    expect(resolveLength({ basis: 0.25 }, scope(200, null, 30))).toBe(50);
  });

  it('measures a cap against the box the length used', () => {
    expect(lengthReference({ basis: 0.1, ofParent: 0.2 }, scope(240, null, 54))).toBe(54);
    expect(lengthReference({ basis: 0.1, ofParent: 0.2 }, scope(240, null, null))).toBe(240);
    expect(lengthReference({ basis: 0.1 }, scope(240, null, 54))).toBe(240);
  });

  it('takes the smaller side of a box and none unless both are definite', () => {
    expect(boxExtent(80, 54)).toBe(54);
    expect(boxExtent(80, null)).toBeNull();
    expect(boxExtent(null, 54)).toBeNull();
  });

  describe('parsing', () => {
    it('reads ofParent beside the basis', () => {
      expect(asUiLength({ basis: 0.02, ofParent: 0.4 })).toEqual({ basis: 0.02, ofParent: 0.4 });
    });

    it('drops an ofParent that is negative, not a number or not finite and keeps the length', () => {
      expect(asUiLength({ basis: 0.02, ofParent: -1 })).toEqual({ basis: 0.02 });
      expect(asUiLength({ basis: 0.02, ofParent: '0.4' })).toEqual({ basis: 0.02 });
      expect(asUiLength({ basis: 0.02, ofParent: null })).toEqual({ basis: 0.02 });
    });

    it('is still read by the previous parser as its basis, never as an absent length', () => {
      const wire = { basis: 0.02, ofParent: 0.4 };

      expect(previousAsUiLength(wire)).toEqual({ basis: 0.02 });
      expect(resolveLength(previousAsUiLength(wire), scope(240, null, 54))).toBeCloseTo(4.8, 6);
    });
  });
});
