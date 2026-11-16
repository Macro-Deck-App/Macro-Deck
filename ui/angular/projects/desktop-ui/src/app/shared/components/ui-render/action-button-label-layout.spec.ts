import { TestBed } from '@angular/core/testing';

import { UiComponentDirections, UiComponentProperties, UiComponents, UiNode } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, tick } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

const SIZE = 240;
const INNER = SIZE * 0.8;
const SAFE_AREA = { basis: 0.1 };

type Preset = { justify: string; align: string; x: number; y: number };

const AXIS = [{ name: 'start', value: 0 }, { name: 'center', value: 0.5 }, { name: 'end', value: 1 }];
const PRESETS: Preset[] = AXIS.flatMap(vertical =>
  AXIS.map(horizontal => ({ justify: vertical.name, align: horizontal.name, x: horizontal.value, y: vertical.value })));

function text(id: string, value: string, align: string): UiNode {
  return {
    id,
    type: Types.Text,
    properties: { [Props.Text]: value, [Props.Size]: { basis: 0.14 }, [Props.Align]: align, [Props.Wrap]: true },
  };
}

function singleLabelButton(preset: Preset, value: string): UiNode {
  return {
    id: 'actionButton',
    type: Types.Button,
    properties: { [Props.Padding]: SAFE_AREA, [Props.Justify]: preset.justify },
    children: [
      {
        id: 'labelRow',
        type: Types.Stack,
        properties: { [Props.Direction]: UiComponentDirections.Horizontal },
        children: [
          {
            id: 'labelBox',
            type: Types.Modifier,
            properties: { [Props.Fill]: true, [Props.Padding]: { basis: 0 } },
            children: [text('label', value, preset.align)],
          },
        ],
      },
    ],
  };
}

function alignOf(x: number): string {
  return x < 1 / 3 ? 'start' : x > 2 / 3 ? 'end' : 'center';
}

function slot(suffix: string, value: string, x: number, y: number): UiNode {
  const offset = (dx: number, dy: number) => ({
    ...(dx ? { [Props.OffsetX]: dx } : {}),
    ...(dy ? { [Props.OffsetY]: dy } : {}),
  });
  return {
    id: `labelSlot${suffix}`,
    type: Types.Transform,
    properties: offset(x, y),
    children: [
      {
        id: `labelAnchor${suffix}`,
        type: Types.Stack,
        properties: { [Props.Justify]: 'start' },
        children: [
          {
            id: `labelRow${suffix}`,
            type: Types.Stack,
            properties: { [Props.Direction]: UiComponentDirections.Horizontal, [Props.Justify]: 'start' },
            children: [
              {
                id: `labelPin${suffix}`,
                type: Types.Transform,
                properties: offset(-x, -y),
                children: [
                  {
                    id: `labelBox${suffix}`,
                    type: Types.Modifier,
                    properties: { [Props.Padding]: { basis: 0 } },
                    children: [text(`label${suffix}`, value, alignOf(x))],
                  },
                ],
              },
            ],
          },
        ],
      },
    ],
  };
}

function layeredButton(slots: UiNode[]): UiNode {
  return {
    id: 'actionButton',
    type: Types.Button,
    properties: { [Props.Padding]: SAFE_AREA },
    children: [{ id: 'labels', type: Types.Layer, properties: { [Props.Fill]: true }, children: slots }],
  };
}

async function render(tree: UiNode): Promise<RenderedTree> {
  const context = new UiWidgetTreeContext();
  context.setBasis(SIZE);
  const rendered = await renderTree(tree, [{ provide: UiWidgetTreeContext, useValue: context }]);
  const host = el(rendered);
  host.style.display = 'block';
  host.style.width = `${SIZE}px`;
  host.style.height = `${SIZE}px`;
  document.body.appendChild(host);
  await tick(rendered);
  await tick(rendered);
  return rendered;
}

type Box = { left: number; top: number; right: number; bottom: number };

function inkOf(rendered: RenderedTree, id: string): Box {
  const host = el(rendered).getBoundingClientRect();
  const range = document.createRange();
  range.selectNodeContents(el(rendered).querySelector(`[data-node-id="${id}"]`) as HTMLElement);
  const rect = range.getBoundingClientRect();
  return {
    left: rect.left - host.left, top: rect.top - host.top, right: rect.right - host.left, bottom: rect.bottom - host.top,
  };
}

describe('Action Button labels', () => {
  afterEach(() => {
    document.querySelectorAll('shared-ui-widget-tree').forEach(node => node.remove());
    TestBed.resetTestingModule();
  });

  for (const preset of PRESETS) {
    for (const value of ['Main', 'A much longer label that has to wrap']) {
      it(`draws a label on a preset position where the single-label button drew it (${preset.justify}/${preset.align}, "${value}")`, async () => {
        const before = inkOf(await render(singleLabelButton(preset, value)), 'label');
        const after = inkOf(
          await render(layeredButton([slot('Primary', value, preset.x, preset.y), slot('0', 'Extra', 0.5, 1)])),
          'labelPrimary',
        );

        expect(before.bottom - before.top).toBeGreaterThan(0);
        expect(after.top).toBeCloseTo(before.top, 0);
        expect(after.bottom).toBeCloseTo(before.bottom, 0);
        expect(after.left).toBeCloseTo(before.left, 0);
        expect(after.right).toBeCloseTo(before.right, 0);
      });
    }
  }

  it('places a label by a fraction of the free space, so it moves evenly and never leaves the safe area', async () => {
    const left = inkOf(await render(layeredButton([slot('Primary', 'Main', 0, 0)])), 'labelPrimary');
    const quarter = inkOf(await render(layeredButton([slot('Primary', 'Main', 0.25, 0.75)])), 'labelPrimary');
    const end = inkOf(await render(layeredButton([slot('Primary', 'Main', 1, 1)])), 'labelPrimary');

    const width = left.right - left.left;
    const height = left.bottom - left.top;
    expect(left.left).toBeCloseTo(SIZE * 0.1, 0);
    expect(end.right).toBeCloseTo(SIZE * 0.9, 0);
    expect(quarter.left - left.left).toBeCloseTo((INNER - width) * 0.25, 0);
    expect(quarter.top - left.top).toBeCloseTo((end.top - left.top) * 0.75, 0);
    expect(end.bottom - end.top).toBeCloseTo(height, 0);
  });

  it('positions every label on its own, so one at the top and one at the bottom do not push each other', async () => {
    const rendered = await render(layeredButton([
      slot('Primary', 'Main', 0.5, 0.5), slot('0', 'Top', 0.5, 0), slot('1', 'Low', 0.5, 1),
    ]));

    const main = inkOf(rendered, 'labelPrimary');
    const top = inkOf(rendered, 'label0');
    const low = inkOf(rendered, 'label1');

    expect(Math.abs((main.top + main.bottom) / 2 - SIZE / 2)).toBeLessThan(2);
    expect(top.bottom).toBeLessThan(main.top);
    expect(low.top).toBeGreaterThan(main.bottom);
    expect(Math.abs(top.top - (SIZE - low.bottom))).toBeLessThan(2);
  });
});
