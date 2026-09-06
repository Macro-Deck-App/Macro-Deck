import { iconDisplayStyle, resolveIconDisplay } from './icon-display.util';

describe('icon-display.util', () => {
  describe('resolveIconDisplay', () => {
    it('defaults an absent framing to a contained icon at full size and opacity', () => {
      expect(resolveIconDisplay(undefined)).toEqual({
        fit: 'contain', zoom: 100, offsetX: 0, offsetY: 0, opacity: 100,
      });
    });

    it('keeps cover and clamps every value into its range', () => {
      expect(resolveIconDisplay({ fit: 'cover', zoom: 9000, offsetX: -500, offsetY: 500, opacity: -20 })).toEqual({
        fit: 'cover', zoom: 400, offsetX: -100, offsetY: 100, opacity: 0,
      });
    });

    it('falls back for values that are not finite numbers', () => {
      const display = resolveIconDisplay({ zoom: Number.NaN, opacity: undefined });
      expect(display.zoom).toBe(100);
      expect(display.opacity).toBe(100);
    });

    it('treats an unknown fit as contain', () => {
      expect(resolveIconDisplay({ fit: 'stretch' as never }).fit).toBe('contain');
    });
  });

  describe('iconDisplayStyle', () => {
    it('renders the default framing', () => {
      expect(iconDisplayStyle(undefined)).toEqual({
        'object-fit': 'contain',
        'transform': 'translate(0%, 0%) scale(1)',
        'opacity': '1',
      });
    });

    it('translates outside the scale so an offset stays independent of the zoom', () => {
      const style = iconDisplayStyle({ fit: 'cover', zoom: 250, offsetX: -20, offsetY: 15, opacity: 40 });
      expect(style['object-fit']).toBe('cover');
      expect(style['transform']).toBe('translate(-20%, 15%) scale(2.5)');
      expect(style['opacity']).toBe('0.4');
    });
  });
});
