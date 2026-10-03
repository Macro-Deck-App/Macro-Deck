import { naturalTextWidth, textFit } from './text-fit';

describe('textFit', () => {
  let original: typeof Element.prototype.getBoundingClientRect;
  let label: HTMLElement | undefined;

  beforeEach(() => {
    original = Element.prototype.getBoundingClientRect;
    // Whole-pixel advances: a width that is not proportional to the size, as a hinted face's is.
    Element.prototype.getBoundingClientRect = function (this: Element): DOMRect {
      const isMeasurer = this.getAttribute('aria-hidden') === 'true' && this.parentElement === document.body;
      const width = isMeasurer ? 10 * Math.ceil(parseFloat((this as HTMLElement).style.fontSize) * 0.9) : 0;
      return { width, height: 0, top: 0, left: 0, right: width, bottom: 0, x: 0, y: 0, toJSON: () => ({}) };
    };
  });

  afterEach(() => {
    Element.prototype.getBoundingClientRect = original;
    label?.remove();
  });

  it('leaves a shrinkable text no wider than its room even when widths are not proportional to size', () => {
    label = document.createElement('div');
    label.textContent = 'Mouse';
    document.body.appendChild(label);

    for (const available of [97, 103.5, 118, 131.2]) {
      const size = textFit(label!, 20, 4).apply(available)!;
      label!.style.fontSize = `${size}px`;
      expect(naturalTextWidth(label!)!).toBeLessThanOrEqual(available, `room ${available}`);
    }
  });

  it('fits with the same styled-copy measurement that first-fit uses', () => {
    label = document.createElement('div');
    label.textContent = 'Mouse';
    document.body.appendChild(label);
    const view = document.defaultView!;
    const realStyle = view.getComputedStyle.bind(view);
    spyOn(view, 'getComputedStyle').and.callFake((element: Element) => {
      const style = realStyle(element);
      if ((element as HTMLElement).style.width !== 'max-content') return style;
      // The element's own styles give it wider advances than the isolated span above.
      const width = 12 * Math.ceil(parseFloat((element as HTMLElement).style.fontSize));
      return new Proxy(style, {
        get(target, key) { return key === 'width' ? `${width}px` : Reflect.get(target, key); },
      });
    });

    const size = textFit(label, 20, 4).apply(100)!;

    expect(size).toBeLessThan(10);
    expect(naturalTextWidth(label)!).toBeLessThanOrEqual(100);
  });
});
