import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponentDirections, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, updateTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

const BASIS = 240;

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function text(id: string, size: number, value: string): UiNode {
  return { id, type: Types.Text, properties: { [Props.Text]: value, [Props.Size]: { basis: size } } };
}

function node(rendered: RenderedTree, id: string): HTMLElement {
  return el(rendered).querySelector(`[data-node-id="${id}"]`) as HTMLElement;
}

function cardWith(children: UiNode[]): UiNode {
  return {
    id: 'root',
    type: Types.Stack,
    properties: { [Props.Direction]: UiComponentDirections.Vertical },
    children: [
      {
        id: 'row',
        type: Types.Stack,
        properties: { [Props.Direction]: UiComponentDirections.Horizontal },
        children,
      },
    ],
  };
}

describe('ui.text ink', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('paints a reading whole where the row around it is sized by that reading', async () => {
    const rendered = await renderTree(cardWith([text('value', 0.14, '50 %')]), withBasis(BASIS));

    const value = node(rendered, 'value');

    expect(value.scrollHeight).toBeLessThanOrEqual(value.clientHeight);
  });

  it('leaves the row the height the profile gives it, ink or no ink', async () => {
    const rendered = await renderTree(cardWith([text('value', 0.14, '50 %')]), withBasis(BASIS));

    expect(node(rendered, 'row').getBoundingClientRect().height).toBeCloseTo(BASIS * 0.14, 1);
  });

  it('takes only the height it declares, so what follows it sits where the profile says', async () => {
    const rendered = await renderTree(
      {
        id: 'root',
        type: Types.Stack,
        properties: { [Props.Direction]: UiComponentDirections.Vertical },
        children: [
          text('value', 0.14, '50 %'),
          { id: 'below', type: Types.Stack, properties: { [Props.MainSize]: { basis: 0.1 } } },
        ],
      },
      withBasis(BASIS),
    );

    const root = node(rendered, 'root').getBoundingClientRect();
    const below = node(rendered, 'below').getBoundingClientRect();

    expect(below.top - root.top).toBeCloseTo(BASIS * 0.14, 1);
  });

  it('still holds a reading too big for its box to the height that box declares', async () => {
    const rendered = await renderTree(
      {
        id: 'root',
        type: Types.Stack,
        properties: { [Props.Direction]: UiComponentDirections.Vertical },
        children: [
          {
            id: 'row',
            type: Types.Stack,
            properties: { [Props.Direction]: UiComponentDirections.Horizontal, [Props.MainSize]: { basis: 0.1 } },
            children: [text('value', 0.4, '50 %')],
          },
          { id: 'below', type: Types.Stack, properties: { [Props.MainSize]: { basis: 0.1 } } },
        ],
      },
      withBasis(BASIS),
    );

    const root = node(rendered, 'root').getBoundingClientRect();
    const below = node(rendered, 'below').getBoundingClientRect();

    expect(below.top - root.top).toBeCloseTo(BASIS * 0.1, 1);
  });

  describe('wrapping text with blank lines after it', () => {
    const wrapping = (id: string, value: string): UiNode => ({
      id,
      type: Types.Text,
      properties: { [Props.Text]: value, [Props.Size]: { basis: 0.1 }, [Props.Wrap]: true },
    });

    async function heightOf(value: string): Promise<number> {
      const rendered = await renderTree(
        { id: 'root', type: Types.Stack, properties: {}, children: [wrapping('value', value)] },
        withBasis(BASIS),
      );
      const height = node(rendered, 'value').getBoundingClientRect().height;
      TestBed.resetTestingModule();
      return height;
    }

    it('keeps one trailing blank line as a line of its own', async () => {
      expect(await heightOf('Back\n')).toBeCloseTo(await heightOf('a\nb'), 0);
    });

    it('keeps every trailing blank line', async () => {
      expect(await heightOf('Back\n\n')).toBeCloseTo(await heightOf('a\nb\nc'), 0);
    });

    it('adds no line to text without a trailing break', async () => {
      expect(await heightOf('Back')).toBeCloseTo(await heightOf('a'), 0);
    });
  });

  describe('wrapping text clamped to two lines', () => {
    const SIZE = 0.1;
    const LONG = 'Grüße aus Köln gypjq Äpfel Öl Übermut gypjq Élan Ärger Ödland Üppig gypjq Äther Öse';
    const TWO_LINES = 'gypjq\ngypjq';

    const clamped = (value: string): UiNode => ({
      id: 'value',
      type: Types.Text,
      properties: { [Props.Text]: value, [Props.Size]: { basis: SIZE }, [Props.Wrap]: true, [Props.MaxLines]: 2 },
    });

    const column = (child: UiNode): UiNode => ({
      id: 'root',
      type: Types.Stack,
      properties: { [Props.Direction]: UiComponentDirections.Vertical },
      children: [child, { id: 'below', type: Types.Stack, properties: { [Props.MainSize]: { basis: 0.1 } } }],
    });

    interface Glyph {
      line: number;
      color: string;
      shadow: string;
    }

    function glyphs(element: HTMLElement): Glyph[] {
      const style = getComputedStyle(element);
      const pitch = parseFloat(style.lineHeight);
      const top = element.getBoundingClientRect().top + parseFloat(style.paddingTop);
      const found: Glyph[] = [];
      const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
      for (let current = walker.nextNode(); current !== null; current = walker.nextNode()) {
        const textNode = current as Text;
        const owner = getComputedStyle(textNode.parentElement!);
        for (let offset = 0; offset < textNode.length; offset++) {
          if (textNode.data[offset].trim() === '') continue;
          const range = document.createRange();
          range.setStart(textNode, offset);
          range.setEnd(textNode, offset + 1);
          const rect = range.getClientRects()[0];
          if (rect === undefined || rect.height === 0) continue;
          found.push({
            line: Math.floor((rect.top + rect.height / 2 - top) / pitch),
            color: owner.color,
            shadow: owner.textShadow,
          });
        }
      }
      return found;
    }

    const transparent = (color: string) => color === 'transparent' || /rgba\(.*,\s*0\)$/.test(color);

    it('paints nothing of the lines past the clamp', async () => {
      const rendered = await renderTree(column(clamped(LONG)), withBasis(BASIS));

      const hidden = glyphs(node(rendered, 'value')).filter(glyph => glyph.line >= 2);

      expect(hidden.length).toBeGreaterThan(0);
      expect(hidden.every(glyph => transparent(glyph.color) && glyph.shadow === 'none')).toBeTrue();
    });

    it('keeps painting the lines it shows', async () => {
      const rendered = await renderTree(column(clamped(LONG)), withBasis(BASIS));

      const shown = glyphs(node(rendered, 'value')).filter(glyph => glyph.line < 2);

      expect(shown.length).toBeGreaterThan(0);
      expect(shown.some(glyph => transparent(glyph.color))).toBeFalse();
    });

    it('hides the legibility shadow of the clamped lines inside a button too', async () => {
      const rendered = await renderTree(
        { id: 'btn', type: Types.Button, properties: {}, children: [clamped(LONG)] },
        withBasis(BASIS),
      );

      const glyphsOfValue = glyphs(node(rendered, 'value'));
      const hidden = glyphsOfValue.filter(glyph => glyph.line >= 2);

      expect(hidden.length).toBeGreaterThan(0);
      expect(hidden.every(glyph => transparent(glyph.color) && glyph.shadow === 'none')).toBeTrue();
      expect(glyphsOfValue.filter(glyph => glyph.line < 2).every(glyph => glyph.shadow !== 'none')).toBeTrue();
    });

    it('stays two lines tall, so what follows it sits where the profile says', async () => {
      const rendered = await renderTree(column(clamped(LONG)), withBasis(BASIS));

      const root = node(rendered, 'root').getBoundingClientRect();
      const below = node(rendered, 'below').getBoundingClientRect();

      expect(below.top - root.top).toBeCloseTo(2 * BASIS * SIZE, 1);
    });

    it('leaves the last line its descenders when the text is exactly two lines', async () => {
      const rendered = await renderTree(column(clamped(TWO_LINES)), withBasis(BASIS));

      const value = node(rendered, 'value');

      expect(value.scrollHeight).toBeLessThanOrEqual(value.clientHeight);
      expect(glyphs(value).some(glyph => transparent(glyph.color))).toBeFalse();
    });

    it('leaves a clamped text alone while only a sibling text changes', async () => {
      const withSibling = (sibling: string): UiNode => ({
        id: 'root',
        type: Types.Stack,
        properties: { [Props.Direction]: UiComponentDirections.Vertical },
        children: [clamped(LONG), text('sibling', 0.1, sibling)],
      });
      const rendered = await renderTree(withSibling('12:00'), withBasis(BASIS));
      const before = node(rendered, 'value').querySelector('.widget-text-clamp-overflow');

      await updateTree(rendered, { root: withSibling('12:01') });

      expect(node(rendered, 'sibling').textContent).toBe('12:01');
      expect(before).not.toBeNull();
      expect(node(rendered, 'value').querySelector('.widget-text-clamp-overflow')).toBe(before);
    });

    it('shows every line again once the text fits', async () => {
      const rendered = await renderTree(column(clamped(LONG)), withBasis(BASIS));
      await updateTree(rendered, { root: column(clamped(TWO_LINES)) });

      expect(glyphs(node(rendered, 'value')).some(glyph => transparent(glyph.color))).toBeFalse();
      expect(node(rendered, 'value').textContent).toBe(TWO_LINES);
    });
  });
});
