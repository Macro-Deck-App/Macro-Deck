import { TestBed } from '@angular/core/testing';

import {
  UiNode,
  UiComponentDirections,
  UiComponentOverflows,
  UiComponents,
  UiComponentProperties,
} from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree } from './ui-widget-render-test-support';

const Props = UiComponentProperties;

const BASIS = 240;
const BOX_PADDING = 0.03;

interface LabelLook {
  outline?: number;
  boxColor?: string;
  boxBorder?: number;
}

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function hasBox(look: LabelLook): boolean {
  return look.boxColor !== undefined || look.boxBorder !== undefined;
}

function boxPaddingOf(look: LabelLook): number {
  return hasBox(look) ? BOX_PADDING + (look.boxBorder ?? 0) : 0;
}

function outlinedText(id: string, value: string, size: number, outline: number | undefined, wrap: boolean): UiNode {
  return {
    id,
    type: UiComponents.Text,
    properties: {
      [Props.Text]: value,
      [Props.Size]: { basis: size },
      ...(wrap ? { [Props.Wrap]: true } : {}),
      ...(outline === undefined ? {} : { [Props.StrokeColor]: '#000000', [Props.StrokeWidth]: { basis: outline } }),
    },
  } as UiNode;
}

function actionButton(value: string, look: LabelLook): UiNode {
  const modifiers: Record<string, unknown> = {};
  if (look.boxColor !== undefined) modifiers['background'] = look.boxColor;
  if (look.boxBorder !== undefined) {
    modifiers['borderWidth'] = { basis: look.boxBorder };
    modifiers['borderColor'] = '#ffffff';
  }

  return {
    id: 'button',
    type: UiComponents.Button,
    properties: {},
    children: [
      {
        id: 'labelRow',
        type: UiComponents.Stack,
        properties: { [Props.Direction]: UiComponentDirections.Horizontal, [Props.Justify]: 'center' },
        children: [
          {
            id: 'labelBox',
            type: UiComponents.Modifier,
            properties: {
              [Props.Padding]: { basis: boxPaddingOf(look) },
              [Props.Modifiers]: modifiers,
              ...(hasBox(look) ? {} : { [Props.Fill]: true }),
            },
            children: [outlinedText('label', value, 0.16, look.outline, true)],
          },
        ],
      },
    ],
  } as UiNode;
}

function element(rendered: RenderedTree, id: string): HTMLElement {
  return el(rendered).querySelector(`[data-node-id="${id}"]`) as HTMLElement;
}

function lineCount(rendered: RenderedTree): number {
  const range = document.createRange();
  range.selectNodeContents(element(rendered, 'label'));
  const tops = new Set<number>();
  for (const box of Array.from(range.getClientRects())) {
    if (box.width > 0) tops.add(Math.round(box.top));
  }
  return tops.size;
}

interface LabelLayout {
  lines: number;
  labelLeft: number;
  labelRight: number;
  roomLeft: number;
  roomRight: number;
}

async function layoutOf(value: string, look: LabelLook): Promise<LabelLayout> {
  const rendered = await renderTree(actionButton(value, look), withBasis(BASIS));
  const outlinePx = (look.outline ?? 0) * BASIS;
  const paddingPx = boxPaddingOf(look) * BASIS;
  const label = element(rendered, 'label').getBoundingClientRect();
  const box = element(rendered, 'labelBox').getBoundingClientRect();
  const layout = {
    lines: lineCount(rendered),
    labelLeft: label.left + outlinePx,
    labelRight: label.right - outlinePx,
    roomLeft: box.left + paddingPx,
    roomRight: box.right - paddingPx,
  };
  TestBed.resetTestingModule();
  return layout;
}

function expectInsideTheBox(layout: LabelLayout): void {
  expect(layout.labelLeft).toBeGreaterThanOrEqual(layout.roomLeft - 0.5);
  expect(layout.labelRight).toBeLessThanOrEqual(layout.roomRight + 0.5);
}

describe('an outlined label in a label box', () => {
  afterEach(() => TestBed.resetTestingModule());

  const label = 'Mute';

  it('fits on one line without outline or box', async () => {
    expect((await layoutOf(label, {})).lines).toBe(1);
  });

  it('fits on one line with a box colour and no outline', async () => {
    expect((await layoutOf(label, { boxColor: '#224466' })).lines).toBe(1);
  });

  it('fits on one line with a box border and no outline', async () => {
    expect((await layoutOf(label, { boxBorder: 0.02 })).lines).toBe(1);
  });

  it('stays on one line with an outline and no box', async () => {
    expect((await layoutOf(label, { outline: 0.05 })).lines).toBe(1);
  });

  it('stays on one line inside the box with an outline and a box colour', async () => {
    const layout = await layoutOf(label, { outline: 0.02, boxColor: '#224466' });

    expect(layout.lines).toBe(1);
    expectInsideTheBox(layout);
  });

  it('stays on one line inside the box with an outline and a box border', async () => {
    const layout = await layoutOf(label, { outline: 0.02, boxBorder: 0.02 });

    expect(layout.lines).toBe(1);
    expectInsideTheBox(layout);
  });

  it('stays on one line inside the box with an outline, a box colour and a box border', async () => {
    const layout = await layoutOf(label, { outline: 0.05, boxColor: '#224466', boxBorder: 0.03 });

    expect(layout.lines).toBe(1);
    expectInsideTheBox(layout);
  });

  it('still wraps a label wider than the button and keeps it inside the box', async () => {
    const layout = await layoutOf('Mute the microphone', { outline: 0.02, boxColor: '#224466' });

    expect(layout.lines).toBeGreaterThan(1);
    expectInsideTheBox(layout);
  });
});

describe('an outlined text in a clip-start stack', () => {
  afterEach(() => TestBed.resetTestingModule());

  const outline = 0.02;
  const outlinePx = outline * BASIS;

  function clipStart(direction: string, child: UiNode): UiNode {
    return {
      id: 'root',
      type: UiComponents.Stack,
      properties: { [Props.Direction]: direction },
      children: [
        {
          id: 'feed',
          type: UiComponents.Stack,
          properties: {
            [Props.Direction]: direction,
            [Props.Overflow]: UiComponentOverflows.ClipStart,
            [Props.MainSize]: { basis: 0.3 },
          },
          children: [child],
        },
      ],
    } as UiNode;
  }

  it('keeps its natural width and clips at the start when wider than a row', async () => {
    const value = 'A line far wider than its row';
    const rendered = await renderTree(
      clipStart(UiComponentDirections.Horizontal, outlinedText('line', value, 0.1, outline, false)),
      withBasis(BASIS),
    );
    const feed = element(rendered, 'feed').getBoundingClientRect();
    const line = element(rendered, 'line');
    const range = document.createRange();
    range.selectNodeContents(line);
    const ink = range.getBoundingClientRect();
    const box = line.getBoundingClientRect();

    expect(box.width - 2 * outlinePx).toBeGreaterThan(feed.width);
    expect(ink.width).toBeLessThanOrEqual(box.width - 2 * outlinePx + 0.5);
    expect(box.right - outlinePx).toBeCloseTo(feed.right, 0);
  });

  it('keeps its natural height and clips at the start when taller than a column', async () => {
    const value = 'One two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen';
    const rendered = await renderTree(
      clipStart(UiComponentDirections.Vertical, outlinedText('line', value, 0.1, outline, true)),
      withBasis(BASIS),
    );
    const feed = element(rendered, 'feed').getBoundingClientRect();
    const line = element(rendered, 'line');
    const range = document.createRange();
    range.selectNodeContents(line);
    const ink = range.getBoundingClientRect();
    const box = line.getBoundingClientRect();

    expect(ink.height).toBeGreaterThan(feed.height);
    expect(line.scrollHeight).toBeLessThanOrEqual(line.clientHeight + 1);
    expect(box.bottom).toBeGreaterThanOrEqual(feed.bottom - 0.5);
  });
});
