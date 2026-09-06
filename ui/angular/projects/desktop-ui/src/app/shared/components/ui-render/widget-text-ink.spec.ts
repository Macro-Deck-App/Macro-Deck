import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponentDirections, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree } from './ui-widget-render-test-support';

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
});
