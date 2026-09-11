import { setCustomPropertySupportForTesting } from './custom-properties';
import { renderWidgetBorder } from './widget-border';

describe('widget border', () => {
  let overlay: HTMLElement;

  beforeEach(() => {
    overlay = document.createElement('div');
    document.body.appendChild(overlay);
  });

  afterEach(() => overlay.remove());

  const ring = (): HTMLElement | null => overlay.querySelector('.ring');
  const phase = (): string => ring()?.style.getPropertyValue('--wb-phase') ?? '';

  it('is the same width on screen whether it is mounted inside the scaled content or beside it', () => {
    const beside = document.createElement('div');
    const inside = document.createElement('div');
    document.body.appendChild(beside);
    document.body.appendChild(inside);

    renderWidgetBorder(beside, () => 0);
    renderWidgetBorder(inside, () => 0, { insideScaledContent: true });

    const authored = (declared: string, deckScale: number): number => {
      const divided = /^calc\(([\d.]+)px \/ var\(--deck-scale, 1\)\)$/.exec(declared);
      if (divided !== null) return Number(divided[1]) / deckScale;
      const plain = /^([\d.]+)px$/.exec(declared);
      expect(plain).not.toBeNull();
      return Number(plain?.[1]);
    };

    for (const deckScale of [0.5, 0.845, 1, 2.4]) {
      // The tile's own ring is already in screen pixels; the one in the tile content is drawn
      // through the transform, so what it declares is multiplied by the deck scale.
      const tile = authored(beside.style.getPropertyValue('--wb-width'), deckScale);
      const inTree = authored(inside.style.getPropertyValue('--wb-width'), deckScale) * deckScale;

      expect(inTree).toBeCloseTo(tile, 6);
    }

    beside.remove();
    inside.remove();
  });

  it('paints a ring for a configured border and nothing for none', () => {
    const handle = renderWidgetBorder(overlay, () => 0);

    handle.update({ style: 'breathing', color: '#ff0000' });
    expect(ring()).not.toBeNull();

    handle.update(undefined);
    expect(ring()).toBeNull();
  });

  it('leaves the ring alone while the border and the clock both hold still', () => {
    const handle = renderWidgetBorder(overlay, () => Date.now());
    handle.update({ style: 'breathing', color: '#ff0000' });
    const first = ring();

    handle.update({ style: 'breathing', color: '#ff0000' });

    // Replacing it would restart the animation, which is what the phase lock exists to avoid.
    expect(ring()).toBe(first);
  });

  it('re-anchors the phase when the clock is corrected under a ring already on screen', () => {
    let offset = 0;
    const handle = renderWidgetBorder(overlay, () => Date.now() + offset);
    handle.update({ style: 'breathing', color: '#ff0000' });
    const before = ring();
    const phaseBefore = phase();

    // The first server sync lands. A ring that kept the device clock's phase would stay visibly out
    // of step with every other client for as long as it was on screen.
    offset = 4000;
    handle.update({ style: 'breathing', color: '#ff0000' });

    expect(ring()).not.toBe(before);
    expect(phase()).not.toBe(phaseBefore);
  });

  it('phase-locks the ring on an engine that only knows the prefixed animation properties', () => {
    setCustomPropertySupportForTesting(false);
    const writes = spyOn(CSSStyleDeclaration.prototype, 'setProperty').and.callThrough();
    try {
      renderWidgetBorder(overlay, () => 1500).update({ style: 'breathing', color: '#ff0000' });

      expect(writes).toHaveBeenCalledWith('-webkit-animation-delay', phase());
    } finally {
      setCustomPropertySupportForTesting(null);
    }
  });

  it('ignores a clock reading that only differs by the time the two reads took', () => {
    let extra = 0;
    const handle = renderWidgetBorder(overlay, () => Date.now() + extra);
    handle.update({ style: 'breathing', color: '#ff0000' });
    const before = ring();

    // Milliseconds of jitter between two reads of the same clock are not a correction.
    extra = 5;
    handle.update({ style: 'breathing', color: '#ff0000' });

    expect(ring()).toBe(before);
  });
});
