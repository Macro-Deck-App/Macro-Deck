import { UiNode } from '../ui-framework/ui-node.interface';
import {
  buttonArtworkTransform,
  buttonBackground,
  buttonBorder,
  buttonFit,
  buttonOpacity,
  chartPlotTop,
  chartPoints,
  nodeAlign,
  nodeGapPx,
  nodeIsHorizontal,
  nodeJustify,
  nodePaddingPx,
  stackBackground,
  textDigits,
  textFillColor,
  textFontWeight,
  textRoleColor,
  textWraps,
  widgetTileFilled,
  widgetTileTransparent,
} from './style';

let nextId = 0;
const node = (properties: Record<string, unknown> = {}): UiNode =>
  ({ id: `n${++nextId}`, type: 'ui.stack', properties }) as UiNode;

describe('widget node presentation', () => {
  describe('axis and alignment', () => {
    it('runs down the page unless the producer asks for a row', () => {
      expect(nodeIsHorizontal(node())).toBeFalse();
      expect(nodeIsHorizontal(node({ direction: 'vertical' }))).toBeFalse();
      expect(nodeIsHorizontal(node({ direction: 'horizontal' }))).toBeTrue();
    });

    it('treats a direction it does not know as the default, not as a row', () => {
      expect(nodeIsHorizontal(node({ direction: 'diagonal' }))).toBeFalse();
    });

    it('packs to the start unless told otherwise', () => {
      expect(nodeJustify(node())).toBe('flex-start');
      expect(nodeJustify(node({ justify: 'center' }))).toBe('center');
      expect(nodeJustify(node({ justify: 'end' }))).toBe('flex-end');
      expect(nodeJustify(node({ justify: 'space-between' }))).toBe('space-between');
    });

    it('stretches children across the cross axis unless told otherwise', () => {
      expect(nodeAlign(node())).toBe('stretch');
      expect(nodeAlign(node({ align: 'start' }))).toBe('flex-start');
      expect(nodeAlign(node({ align: 'center' }))).toBe('center');
      expect(nodeAlign(node({ align: 'end' }))).toBe('flex-end');
      expect(nodeAlign(node({ align: 'baseline' }))).toBe('baseline');
    });

    it('falls back to the default for a distribution it does not know', () => {
      expect(nodeJustify(node({ justify: 'space-around' }))).toBe('flex-start');
      expect(nodeAlign(node({ align: 'last-baseline' }))).toBe('stretch');
    });
  });

  describe('lengths', () => {
    const scope = (basis: number) => ({ basis, crossExtent: null, container: null });

    it('treats an absent padding or gap as none', () => {
      expect(nodePaddingPx(node(), scope(100))).toBe(0);
      expect(nodeGapPx(node(), scope(100))).toBe(0);
    });

    it('resolves padding and gap as fractions of the basis', () => {
      expect(nodePaddingPx(node({ padding: { basis: 0.1 } }), scope(200))).toBe(20);
      expect(nodeGapPx(node({ gap: { basis: 0.05 } }), scope(200))).toBe(10);
    });
  });

  describe('backgrounds', () => {
    it('paints nothing behind a plain stack the producer left bare', () => {
      expect(stackBackground(node())).toBeUndefined();
    });

    it('falls a bare button back to the reader accent rather than to nothing', () => {
      expect(buttonBackground(node())).toBe('var(--color-accent)');
    });

    it('uses a literal colour when the producer sent one', () => {
      expect(stackBackground(node({ background: '#ff8800' }))).toBe('#ff8800');
      expect(buttonBackground(node({ background: '#ff8800' }))).toBe('#ff8800');
    });

    it('ignores a colour that is not a six-digit hex', () => {
      expect(stackBackground(node({ background: 'red' }))).toBeUndefined();
      expect(stackBackground(node({ background: '#fff' }))).toBeUndefined();
      expect(stackBackground(node({ background: 'rgba(0, 0, 0, 0)' }))).toBeUndefined();
      expect(buttonBackground(node({ background: 'Transparent' }))).toBe('var(--color-accent)');
    });

    it('paints nothing for a stack or a button asked for transparent, rather than the button accent', () => {
      expect(stackBackground(node({ background: 'transparent' }))).toBe('transparent');
      expect(buttonBackground(node({ background: 'transparent' }))).toBe('transparent');
    });
  });

  describe('filled tile face', () => {
    const typed = (type: string, properties: Record<string, unknown>): UiNode =>
      ({ id: `f${++nextId}`, type, properties, children: [] }) as UiNode;

    it('is filled when the root paints an opaque colour, including a button with no colour of its own', () => {
      expect(widgetTileFilled(typed('ui.stack', { background: '#ff0000' }))).toBeTrue();
      expect(widgetTileFilled(typed('ui.button', { background: '#ff0000' }))).toBeTrue();
      expect(widgetTileFilled(typed('ui.button', {}))).toBeTrue();
    });

    it('is not filled when the root paints nothing opaque of its own', () => {
      expect(widgetTileFilled(null)).toBeFalse();
      expect(widgetTileFilled(typed('ui.stack', {}))).toBeFalse();
      expect(widgetTileFilled(typed('ui.stack', { background: 'transparent' }))).toBeFalse();
      expect(widgetTileFilled(typed('ui.button', { background: 'transparent' }))).toBeFalse();
      expect(widgetTileFilled(typed('ui.text', { background: '#ff0000' }))).toBeFalse();
    });

    it('fills with an opaque eight-digit colour but lets a translucent one paint over the tile face', () => {
      expect(widgetTileFilled(typed('ui.stack', { background: '#ff0000ff' }))).toBeTrue();
      expect(widgetTileFilled(typed('ui.stack', { background: '#ff000080' }))).toBeFalse();
      expect(widgetTileFilled(typed('ui.button', { background: '#ff000080' }))).toBeFalse();
      expect(widgetTileFilled(typed('ui.button', { background: '#ff000000' }))).toBeFalse();
      expect(widgetTileTransparent(typed('ui.button', { background: '#ff000000' }))).toBeFalse();
    });
  });

  describe('transparent tile face', () => {
    const typed = (type: string, properties: Record<string, unknown>, children: UiNode[] = []): UiNode =>
      ({ id: `n${++nextId}`, type, properties, children }) as UiNode;

    it('drops the face when the root stack or button is transparent', () => {
      expect(widgetTileTransparent(typed('ui.stack', { background: 'transparent' }))).toBeTrue();
      expect(widgetTileTransparent(typed('ui.button', { background: 'transparent' }))).toBeTrue();
    });

    it('keeps the face for no tree, a coloured or bare root, or a transparent node below the root', () => {
      expect(widgetTileTransparent(null)).toBeFalse();
      expect(widgetTileTransparent(undefined)).toBeFalse();
      expect(widgetTileTransparent(typed('ui.stack', { background: '#101010' }))).toBeFalse();
      expect(widgetTileTransparent(typed('ui.button', {}))).toBeFalse();
      expect(widgetTileTransparent(typed('ui.layer', {}, [typed('ui.stack', { background: 'transparent' })])))
        .toBeFalse();
    });

    it('looks through a responsive root to the variant that is drawn', () => {
      const responsive = typed('ui.responsive', { variants: [{ minWidth: 2 }] }, [
        typed('ui.stack', { background: '#101010' }),
        typed('ui.stack', { background: 'transparent' }),
      ]);

      expect(widgetTileTransparent(responsive, { width: 240, height: 120 })).toBeTrue();
      expect(widgetTileTransparent(responsive, { width: 120, height: 120 })).toBeFalse();
    });
  });

  describe('button artwork framing', () => {
    it('contains the artwork unless the producer asked it to cover', () => {
      expect(buttonFit(node())).toBe('contain');
      expect(buttonFit(node({ fit: 'cover' }))).toBe('cover');
    });

    it('is fully opaque when no opacity was sent, not transparent', () => {
      expect(buttonOpacity(node())).toBe(1);
    });

    it('clamps an opacity outside the unit range instead of passing it through', () => {
      expect(buttonOpacity(node({ opacity: 1.4 }))).toBe(1);
      expect(buttonOpacity(node({ opacity: -0.2 }))).toBe(0);
      expect(buttonOpacity(node({ opacity: 0.35 }))).toBe(0.35);
    });

    it('frames unzoomed and uncentred when nothing was sent', () => {
      expect(buttonArtworkTransform(node())).toBe('translate(0%, 0%) scale(1)');
    });

    it('reads the offsets as fractions of the element, which is what CSS percent already means', () => {
      expect(buttonArtworkTransform(node({ offsetX: 0.25, offsetY: -0.5, zoom: 2 })))
        .toBe('translate(25%, -50%) scale(2)');
    });

    it('ignores a zoom of zero or less rather than collapsing the artwork', () => {
      expect(buttonArtworkTransform(node({ zoom: 0 }))).toContain('scale(1)');
      expect(buttonArtworkTransform(node({ zoom: -3 }))).toContain('scale(1)');
    });
  });

  describe('button ring', () => {
    it('draws no ring when the producer named no style', () => {
      expect(buttonBorder(node())).toBeUndefined();
    });

    it('draws no ring for a style this reader does not know', () => {
      // A transparent ring would still change stacking and hit-testing, so absence has to mean absence.
      expect(buttonBorder(node({ borderStyle: 'dashed-neon' }))).toBeUndefined();
    });

    it('carries the colour through for a style it knows', () => {
      const border = buttonBorder(node({ borderStyle: 'heartbeat', borderColor: '#123456' }));

      expect(border).toEqual({ style: 'heartbeat', color: '#123456' });
    });
  });

  describe('text', () => {
    it('stays on one line unless the producer asked for wrapping', () => {
      expect(textWraps(node())).toBeFalse();
      expect(textWraps(node({ wrap: true }))).toBeTrue();
    });

    it('renders at the regular weight when none was sent', () => {
      expect(textFontWeight(node())).toBe(400);
      expect(textFontWeight(node({ weight: 'medium' }))).toBe(500);
      expect(textFontWeight(node({ weight: 'semibold' }))).toBe(600);
      expect(textFontWeight(node({ weight: 'bold' }))).toBe(700);
    });

    it('paints in the primary role when none was sent', () => {
      expect(textRoleColor(node())).toBe('var(--color-text-primary)');
      expect(textRoleColor(node({ role: 'secondary' }))).toBe('var(--color-text-secondary)');
      expect(textRoleColor(node({ role: 'muted' }))).toBe('var(--color-text-muted)');
    });

    it('paints a literal colour, translucent when it carries alpha, over the role', () => {
      expect(textFillColor(node({ role: 'secondary', color: '#ffffff' }))).toBe('#ffffff');
      expect(textFillColor(node({ role: 'secondary', color: '#ffffffcc' }))).toBe('#ffffffcc');
    });

    it('falls back to the role for a colour that is neither six nor eight hex digits', () => {
      expect(textFillColor(node({ role: 'muted', color: '#fff' }))).toBe('var(--color-text-muted)');
      expect(textFillColor(node({ role: 'muted', color: '#ffffffc' }))).toBe('var(--color-text-muted)');
      expect(textFillColor(node({ role: 'muted', color: 'white' }))).toBe('var(--color-text-muted)');
    });

    it('reserves no digit width unless a positive count was sent', () => {
      expect(textDigits(node())).toBeNull();
      expect(textDigits(node({ digits: 0 }))).toBeNull();
      expect(textDigits(node({ digits: -2 }))).toBeNull();
      expect(textDigits(node({ digits: 4 }))).toBe(4);
    });
  });

  describe('chart series', () => {
    it('reads no series at all when the property is missing or not a list', () => {
      expect(chartPoints(node())).toEqual([]);
      expect(chartPoints(node({ points: 'nope' }))).toEqual([]);
    });

    it('drops samples that are not finite numbers instead of rendering NaN', () => {
      expect(chartPoints(node({ points: [0.2, 'x', null, Infinity, 0.8] }))).toEqual([0.2, 0.8]);
    });

    it('clamps samples into the unit range the profile defines', () => {
      expect(chartPoints(node({ points: [-1, 0.5, 2] }))).toEqual([0, 0.5, 1]);
    });

    it('reserves nothing above the band unless asked, and clamps what is asked', () => {
      expect(chartPlotTop(node())).toBe(0);
      expect(chartPlotTop(node({ plotTop: 0.4 }))).toBe(0.4);
      expect(chartPlotTop(node({ plotTop: 3 }))).toBe(1);
    });
  });
});
