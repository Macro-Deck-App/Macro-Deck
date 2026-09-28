import { TestBed } from '@angular/core/testing';

import {
  UiComponentDirections,
  UiComponentOverflows,
  UiComponentProperties,
  UiComponents,
  UiNode,
} from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

const BASIS = 240;
const TEXT_SIZE = 0.1;
const LINE_PX = BASIS * TEXT_SIZE;
const INK_PX = 0.2 * LINE_PX;
const PIXEL = 0.5;

const SQUARE_SVG = 'data:image/svg+xml;charset=utf-8,' +
  encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8"/></svg>');

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function node(rendered: RenderedTree, id: string): HTMLElement {
  return el(rendered).querySelector(`[data-node-id="${id}"]`) as HTMLElement;
}

function message(index: number): UiNode {
  return {
    id: `message-${index}`,
    type: Types.Text,
    properties: { [Props.Text]: `g\u{1F600} message ${index}`, [Props.Size]: { basis: TEXT_SIZE } },
  };
}

function inkOf(element: HTMLElement): DOMRect {
  const range = document.createRange();
  range.selectNodeContents(element);
  return range.getBoundingClientRect();
}

function feed(count: number): UiNode {
  return {
    id: 'root',
    type: Types.Stack,
    properties: { [Props.Direction]: UiComponentDirections.Vertical },
    children: [
      {
        id: 'feed',
        type: Types.Stack,
        requiredComponentVersion: 2,
        properties: {
          [Props.Overflow]: UiComponentOverflows.ClipStart,
          [Props.MainSize]: { basis: 0.5 },
          [Props.Padding]: { basis: 0.2 * TEXT_SIZE },
        },
        children: Array.from({ length: count }, (_, index) => message(index)),
      },
    ],
  };
}

describe('ui.stack clip-start', () => {
  afterEach(() => TestBed.resetTestingModule());

  const COUNT = 10;

  it('keeps the last child whole, down to the ink below its baseline', async () => {
    const rendered = await renderTree(feed(COUNT), withBasis(BASIS));

    const box = node(rendered, 'feed').getBoundingClientRect();
    const last = node(rendered, `message-${COUNT - 1}`);
    const ink = inkOf(last);

    expect(ink.height).toBeGreaterThan(0);
    expect(ink.bottom).toBeLessThanOrEqual(box.bottom + PIXEL);
    expect(ink.top).toBeGreaterThanOrEqual(box.top - PIXEL);
    expect(last.getBoundingClientRect().bottom).toBeLessThanOrEqual(box.bottom + PIXEL);
    expect(last.scrollHeight).toBeLessThanOrEqual(last.clientHeight);
  });

  it('sits the last line on the padding at the end of the stack', async () => {
    const rendered = await renderTree(feed(COUNT), withBasis(BASIS));

    const box = node(rendered, 'feed').getBoundingClientRect();
    const last = node(rendered, `message-${COUNT - 1}`).getBoundingClientRect();

    expect(box.bottom - (last.bottom - INK_PX)).toBeCloseTo(INK_PX, 0);
  });

  it('cuts the first children off at the top', async () => {
    const rendered = await renderTree(feed(COUNT), withBasis(BASIS));

    const box = node(rendered, 'feed').getBoundingClientRect();
    const first = node(rendered, 'message-0').getBoundingClientRect();

    expect(box.height).toBeCloseTo(BASIS * 0.5, 1);
    expect(first.bottom).toBeLessThan(box.top);
    expect(node(rendered, 'feed').scrollTop).toBe(0);
  });

  it('keeps every child at its own height instead of squashing them into the box', async () => {
    const rendered = await renderTree(feed(COUNT), withBasis(BASIS));

    for (let index = 0; index < COUNT; index++) {
      const height = node(rendered, `message-${index}`).getBoundingClientRect().height;
      expect(height).withContext(`message-${index}`).toBeCloseTo(LINE_PX + 2 * INK_PX, 0);
    }
  });

  it('draws everything, anchored at the end, when the children fit', async () => {
    const rendered = await renderTree(feed(2), withBasis(BASIS));

    const box = node(rendered, 'feed').getBoundingClientRect();
    const first = node(rendered, 'message-0').getBoundingClientRect();

    expect(first.top + INK_PX).toBeGreaterThan(box.top + BASIS * 0.25);
  });
});

describe('ui.text spans', () => {
  afterEach(() => TestBed.resetTestingModule());

  function spanText(id: string, spans: unknown[], extra: Record<string, unknown> = {}): UiNode {
    return {
      id,
      type: Types.Text,
      properties: { [Props.Text]: 'g', [Props.Size]: { basis: TEXT_SIZE }, [Props.Spans]: spans, ...extra },
    };
  }

  async function withLoadedImages(root: UiNode): Promise<RenderedTree> {
    const rendered = await renderTree(root, withBasis(BASIS));
    const images = Array.from(el(rendered).querySelectorAll('img.widget-text-span-image')) as HTMLImageElement[];
    expect(images.length).withContext('image spans drawn').toBeGreaterThan(0);
    await Promise.all(images.map(image => new Promise<void>(resolve => {
      image.addEventListener('load', () => resolve(), { once: true });
      image.setAttribute('src', SQUARE_SVG);
    })));
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    return rendered;
  }

  const emote = { image: { resourceId: 'emote' }, alt: 'Kappa' };

  it('keeps a line holding an image exactly one line high', async () => {
    const rendered = await withLoadedImages({
      id: 'root',
      type: Types.Stack,
      properties: { [Props.Direction]: UiComponentDirections.Vertical },
      children: [
        spanText('plain', [{ text: 'g' }]),
        spanText('mixed', [{ text: 'g ' }, emote, { text: ' g' }]),
      ],
    });

    const plain = node(rendered, 'plain').getBoundingClientRect();
    const mixed = node(rendered, 'mixed');
    const image = mixed.querySelector('img') as HTMLImageElement;

    expect(image.getBoundingClientRect().height).toBeCloseTo(LINE_PX, 0);
    expect(image.getBoundingClientRect().width).toBeCloseTo(LINE_PX, 0);
    expect(mixed.getBoundingClientRect().height).toBeCloseTo(plain.height, 0);
  });

  it('keeps wrapped lines holding images one line high each', async () => {
    const runs = Array.from({ length: 30 }, () => emote);
    const rendered = await withLoadedImages({
      id: 'root',
      type: Types.Stack,
      properties: { [Props.Direction]: UiComponentDirections.Vertical },
      children: [spanText('wrapped', runs, { [Props.Wrap]: true, [Props.MaxLines]: 2 })],
    });

    const wrapped = node(rendered, 'wrapped').getBoundingClientRect();

    expect(wrapped.height).toBeCloseTo(2 * LINE_PX + 2 * INK_PX, 0);
  });
});
