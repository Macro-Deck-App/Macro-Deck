import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, tick } from './ui-widget-render-test-support';

const Props = UiComponentProperties;

function ring(index: number): UiNode {
  return {
    id: `ring${index}`,
    type: UiComponents.Layer,
    properties: {},
    children: [
      {
        id: `gauge${index}`,
        type: UiComponents.Gauge,
        properties: { [Props.Level]: 0.8, [Props.StartAngle]: 0, [Props.EndAngle]: 360, [Props.Thickness]: { basis: 0.01, ofParent: 0.1 } },
      },
      { id: `icon${index}`, type: UiComponents.Icon, properties: { [Props.Icon]: 'play', [Props.Size]: { basis: 0.02, ofParent: 0.3 } } },
      { id: `percent${index}`, type: UiComponents.Text, properties: { [Props.Text]: '80 %', [Props.Size]: { basis: 0.03, ofParent: 0.18 } } },
    ],
  } as UiNode;
}

function panel(count: number): UiNode {
  return {
    id: 'rings',
    type: UiComponents.Grid,
    properties: { [Props.Columns]: 1, [Props.MinCellSize]: { basis: 0.25 } },
    children: Array.from({ length: count }, (_, index) => ring(index)),
  } as UiNode;
}

async function renderAt(count: number, width: number, height: number): Promise<RenderedTree> {
  const context = new UiWidgetTreeContext();
  context.setBasis(Math.min(width, height));
  const rendered = await renderTree(panel(count), [{ provide: UiWidgetTreeContext, useValue: context }]);
  rendered.fixture.componentRef.setInput('box', { width, height });
  await tick(rendered);
  return rendered;
}

function rect(rendered: RenderedTree, id: string): DOMRect {
  return (el(rendered).querySelector(`[data-node-id="${id}"]`) as HTMLElement).getBoundingClientRect();
}

function strokeOf(rendered: RenderedTree, id: string): number {
  const track = el(rendered).querySelector(`[data-node-id="${id}"] .widget-gauge-track`) as SVGElement;
  return parseFloat(track.getAttribute('stroke-width') ?? '');
}

describe('a ring panel drawn from one tree', () => {
  afterEach(() => TestBed.resetTestingModule());

  const shapes = [
    { name: '1x1', width: 120, height: 120, columns: 4, rows: 4 },
    { name: '2x1', width: 240, height: 120, columns: 5, rows: 3 },
    { name: '1x2', width: 120, height: 240, columns: 3, rows: 5 },
    { name: '3x2', width: 360, height: 240, columns: 5, rows: 3 },
  ];

  for (const shape of shapes) {
    it(`arranges fifteen rings as ${shape.columns} by ${shape.rows} in a ${shape.name} widget and scales each ring's parts with its cell`, async () => {
      const rendered = await renderAt(15, shape.width, shape.height);

      const lefts = new Set<number>();
      const tops = new Set<number>();
      for (let index = 0; index < 15; index++) {
        const cell = rect(rendered, `ring${index}`);
        lefts.add(Math.round(cell.left));
        tops.add(Math.round(cell.top));
      }
      expect(lefts.size).withContext('columns').toBe(shape.columns);
      expect(tops.size).withContext('rows').toBe(shape.rows);

      const cell = rect(rendered, 'ring0');
      const edge = Math.min(cell.width, cell.height);
      expect(strokeOf(rendered, 'gauge0')).withContext('stroke').toBeCloseTo(0.1 * edge, 1);
      expect(parseFloat((el(rendered).querySelector('[data-node-id="icon0"] .widget-icon-glyph') as HTMLElement).style.width))
        .withContext('icon').toBeCloseTo(0.3 * edge, 1);
      expect(parseFloat((el(rendered).querySelector('[data-node-id="percent0"]') as HTMLElement).style.fontSize))
        .withContext('percent').toBeCloseTo(0.18 * edge, 1);
    });
  }

  it('draws a ring in a small widget with a thinner stroke than the same ring in a large one', async () => {
    const small = await renderAt(2, 120, 120);
    const smallStroke = strokeOf(small, 'gauge0');
    const large = await renderAt(2, 480, 480);

    expect(strokeOf(large, 'gauge0')).toBeCloseTo(smallStroke * 4, 1);
  });
});
