import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponentDirections, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree } from './ui-widget-render-test-support';

const Props = UiComponentProperties;

const BASIS = 240;

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

// The shape an Action Button sends: a hugging label box around a wrapping label in a centred row.
function actionButton(value: string, look: LabelLook): UiNode {
  const hasBox = look.boxColor !== undefined || look.boxBorder !== undefined;
  const modifiers: Record<string, unknown> = {};
  if (look.boxColor !== undefined) modifiers['background'] = look.boxColor;
  if (look.boxBorder !== undefined) {
    modifiers['borderWidth'] = { basis: look.boxBorder };
    modifiers['borderColor'] = '#ffffff';
  }
  const label: UiNode = {
    id: 'label',
    type: UiComponents.Text,
    properties: {
      [Props.Text]: value,
      [Props.Size]: { basis: 0.16 },
      [Props.Wrap]: true,
      ...(look.outline === undefined
        ? {}
        : { [Props.StrokeColor]: '#000000', [Props.StrokeWidth]: { basis: look.outline } }),
    },
  };

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
              [Props.Padding]: { basis: hasBox ? 0.03 + (look.boxBorder ?? 0) : 0 },
              [Props.Modifiers]: modifiers,
              ...(hasBox ? {} : { [Props.Fill]: true }),
            },
            children: [label],
          },
        ],
      },
    ],
  } as UiNode;
}

function lineCount(rendered: RenderedTree): number {
  const label = el(rendered).querySelector('[data-node-id="label"]') as HTMLElement;
  const range = document.createRange();
  range.selectNodeContents(label);
  const tops = new Set<number>();
  for (const box of Array.from(range.getClientRects())) {
    if (box.width > 0) tops.add(Math.round(box.top));
  }
  return tops.size;
}

async function linesOf(value: string, look: LabelLook): Promise<number> {
  const rendered = await renderTree(actionButton(value, look), withBasis(BASIS));
  const lines = lineCount(rendered);
  TestBed.resetTestingModule();
  return lines;
}

describe('an outlined label in a label box', () => {
  afterEach(() => TestBed.resetTestingModule());

  const label = 'Mute';

  it('fits on one line without outline or box', async () => {
    expect(await linesOf(label, {})).toBe(1);
  });

  it('stays on one line with an outline and no box', async () => {
    expect(await linesOf(label, { outline: 0.05 })).toBe(1);
  });

  it('stays on one line with an outline and a box colour', async () => {
    expect(await linesOf(label, { outline: 0.02, boxColor: '#224466' })).toBe(1);
  });

  it('stays on one line with an outline and a box border', async () => {
    expect(await linesOf(label, { outline: 0.02, boxBorder: 0.02 })).toBe(1);
  });

  it('stays on one line with an outline, a box colour and a box border', async () => {
    expect(await linesOf(label, { outline: 0.05, boxColor: '#224466', boxBorder: 0.03 })).toBe(1);
  });

  it('still wraps a label that is wider than the button', async () => {
    expect(await linesOf('Mute the microphone', { outline: 0.02, boxColor: '#224466' })).toBeGreaterThan(1);
  });
});
