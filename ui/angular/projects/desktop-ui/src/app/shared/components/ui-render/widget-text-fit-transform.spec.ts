import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponentDirections, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, tick } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

const BASIS = 120;

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function reading(id: string): UiNode {
  return {
    id,
    type: Types.Text,
    properties: {
      [Props.Text]: 'Speed 188 km/h',
      [Props.Size]: { basis: 0.24 },
      [Props.MinSize]: { basis: 0.05 },
    },
  };
}

function gauge(transform: Record<string, unknown>, outsideFirst: boolean): UiNode {
  const inside: UiNode = {
    id: 'gauge',
    type: Types.Transform,
    properties: { ...transform, [Props.Fill]: true },
    children: [reading('inside')],
  };
  const outside = reading('outside');
  return {
    id: 'root',
    type: Types.Stack,
    properties: { [Props.Direction]: UiComponentDirections.Vertical },
    children: outsideFirst ? [outside, inside] : [inside, outside],
  };
}

async function fittedSizes(tree: UiNode): Promise<{ inside: number; outside: number }> {
  const rendered: RenderedTree = await renderTree(tree, withBasis(BASIS));
  await tick(rendered);
  await tick(rendered);

  const size = (id: string) =>
    parseFloat(getComputedStyle(el(rendered).querySelector(`[data-node-id="${id}"]`) as HTMLElement).fontSize);
  return { inside: size('inside'), outside: size('outside') };
}

describe('ui.transform inside a ui.layer', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('is drawn across the layer\'s box even when another transform comes before it', async () => {
    const tree: UiNode = {
      id: 'layer',
      type: Types.Layer,
      properties: {},
      children: [
        { id: 'dial', type: Types.Transform, properties: {}, children: [reading('scale')] },
        { id: 'gauge', type: Types.Transform, properties: {}, children: [reading('needle')] },
      ],
    };
    const rendered = await renderTree(tree, withBasis(BASIS));
    await tick(rendered);

    const layer = el(rendered).querySelector('[data-node-id="layer"]') as HTMLElement;
    const gauge = el(rendered).querySelector('[data-node-id="gauge"]') as HTMLElement;
    const layerBox = layer.getBoundingClientRect();
    const gaugeBox = gauge.getBoundingClientRect();
    expect(gaugeBox.top).toBeCloseTo(layerBox.top, 1);
    expect(gaugeBox.left).toBeCloseTo(layerBox.left, 1);
  });
});

describe('widget text fit under a ui.transform', () => {
  afterEach(() => TestBed.resetTestingModule());

  for (const outsideFirst of [false, true]) {
    it(`fits text inside and beside a rotated, zoomed transform as if it were not there (outside first: ${outsideFirst})`,
      async () => {
        const untransformed = await fittedSizes(gauge({}, outsideFirst));
        TestBed.resetTestingModule();
        const transformed = await fittedSizes(
          gauge({ [Props.Rotation]: 35, [Props.Zoom]: 1.6, [Props.OriginY]: 0.9 }, outsideFirst));

        expect(untransformed.inside).toBeLessThan(BASIS * 0.24);
        expect(transformed.inside).toBeCloseTo(untransformed.inside, 1);
        expect(transformed.outside).toBeCloseTo(untransformed.outside, 1);
      });
  }
});
