import { setStyle } from './dom-writes';

// A recording style, because reading a style declaration back cannot show which of the two
// spellings was actually written.
function recordingElement(): { element: HTMLElement; written: { [property: string]: string } } {
  const written: { [property: string]: string } = {};
  const style = { setProperty: (name: string, value: string) => { written[name] = value; } };
  return { element: { style } as unknown as HTMLElement, written };
}

describe('setStyle on the compatibility floor', () => {
  for (const [name, prefixed, value] of [
    ['transform', '-webkit-transform', 'scale(2)'],
    ['transform-origin', '-webkit-transform-origin', '0 0'],
    ['filter', '-webkit-filter', 'brightness(1.2)'],
  ]) {
    it(`writes ${name} under both spellings and clears both together`, () => {
      const { element, written } = recordingElement();

      setStyle(element, name, value);
      expect(written[name]).toBe(value);
      expect(written[prefixed]).toBe(value);

      setStyle(element, name, null);
      expect(written[name]).toBe('');
      expect(written[prefixed]).toBe('');
    });
  }

  it('writes an unprefixed property under its own name only', () => {
    const { element, written } = recordingElement();

    setStyle(element, 'opacity', '0.5');

    expect(Object.keys(written)).toEqual(['opacity']);
  });
});
