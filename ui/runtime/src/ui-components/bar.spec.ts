import { UiNode } from '../ui-framework/ui-node.interface';
import {
  barEnd,
  barFillStyle,
  barHasMarker,
  barMarkerDiameterPx,
  barMarkerLeftPx,
  barStart,
  progressFillStyle,
  sliderIsVertical,
  sliderLevel,
  sliderLevelColor,
  sliderLevelFromPointer,
  sliderThumbOffsetPx,
} from './bar';

let nextId = 0;
const node = (properties: Record<string, unknown> = {}): UiNode =>
  ({ id: `n${++nextId}`, type: 'ui.range-bar', properties }) as UiNode;

describe('bars', () => {
  describe('range bar', () => {
    it('fills the whole track when the producer sent no span', () => {
      expect(barStart(node())).toBe(0);
      expect(barEnd(node())).toBe(1);
    });

    it('clamps a span outside the unit range', () => {
      expect(barStart(node({ start: -0.5 }))).toBe(0);
      expect(barEnd(node({ end: 4 }))).toBe(1);
    });

    it('paints no gradient at all unless both colours were sent', () => {
      // The colour is the value on a range bar, so a substituted one would show a different reading.
      expect(barFillStyle(node())).toBeNull();
      expect(barFillStyle(node({ startColor: '#001122' }))).toBeNull();
      expect(barFillStyle(node({ startColor: '#001122', endColor: '#334455' })))
        .toBe('linear-gradient(to right, #001122, #334455)');
    });

    it('falls a progress bar back to the accent, because there the extent is the value', () => {
      expect(progressFillStyle(node())).toBe('linear-gradient(to right, var(--color-accent), var(--color-accent))');
    });

    it('shows a marker only when it has both a position and an end colour', () => {
      expect(barHasMarker(node({ marker: 0.5 }))).toBeFalse();
      expect(barHasMarker(node({ endColor: '#334455' }))).toBeFalse();
      expect(barHasMarker(node({ marker: 0.5, endColor: '#334455' }))).toBeTrue();
    });

    it('keeps the marker fully inside the bar at either extreme', () => {
      const thickness = 10;
      const width = 200;
      const radius = barMarkerDiameterPx(thickness) / 2;

      const atStart = barMarkerLeftPx(node({ marker: 0 }), width, thickness);
      const atEnd = barMarkerLeftPx(node({ marker: 1 }), width, thickness);

      expect(atStart).toBeGreaterThanOrEqual(radius);
      expect(atEnd).toBeLessThanOrEqual(width - radius);
    });

    it('centres the marker on a bar too narrow to hold it', () => {
      expect(barMarkerLeftPx(node({ marker: 1 }), 4, 40)).toBe(2);
    });
  });

  describe('slider', () => {
    const slider = (properties: Record<string, unknown> = {}): UiNode =>
      ({ id: `s${++nextId}`, type: 'ui.slider', properties }) as UiNode;

    it('runs across the widget unless told otherwise, unlike a stack', () => {
      expect(sliderIsVertical(slider())).toBeFalse();
      expect(sliderIsVertical(slider({ direction: 'vertical' }))).toBeTrue();
    });

    it('sits at zero and in the accent when the producer sent neither', () => {
      expect(sliderLevel(slider())).toBe(0);
      expect(sliderLevelColor(slider())).toBe('var(--color-accent)');
    });

    it('reads a pointer along the track as a level', () => {
      expect(sliderLevelFromPointer(50, 200, false)).toBe(0.25);
      expect(sliderLevelFromPointer(-20, 200, false)).toBe(0);
      expect(sliderLevelFromPointer(400, 200, false)).toBe(1);
    });

    it('reads a vertical track from the bottom up, the way it is drawn', () => {
      expect(sliderLevelFromPointer(0, 200, true)).toBe(1);
      expect(sliderLevelFromPointer(200, 200, true)).toBe(0);
    });

    it('snaps to the step the producer declared', () => {
      expect(sliderLevelFromPointer(52, 200, false, 0.25)).toBe(0.25);
      expect(sliderLevelFromPointer(70, 200, false, 0.25)).toBe(0.25);
      expect(sliderLevelFromPointer(90, 200, false, 0.25)).toBe(0.5);
    });

    it('ignores a step that is zero or negative rather than dividing by it', () => {
      expect(sliderLevelFromPointer(50, 200, false, 0)).toBe(0.25);
      expect(sliderLevelFromPointer(50, 200, false, -1)).toBe(0.25);
    });

    it('answers zero for a track with no extent instead of dividing by zero', () => {
      expect(sliderLevelFromPointer(10, 0, false)).toBe(0);
    });

    it('keeps the thumb inside the track at either end', () => {
      const thickness = 8;
      const extent = 200;

      const low = sliderThumbOffsetPx(0, extent, thickness);
      const high = sliderThumbOffsetPx(1, extent, thickness);

      expect(low).toBeGreaterThanOrEqual(0);
      expect(high).toBeLessThanOrEqual(extent);
      expect(high).toBeGreaterThan(low);
    });
  });
});
