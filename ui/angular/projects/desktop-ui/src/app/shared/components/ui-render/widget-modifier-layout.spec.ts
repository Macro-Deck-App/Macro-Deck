import { TestBed } from '@angular/core/testing';

import { UiNode, UiComponentDirections, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree } from './ui-widget-render-test-support';

const Props = UiComponentProperties;

const BASIS = 240;

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function rect(rendered: RenderedTree, id: string): DOMRect {
  return (el(rendered).querySelector(`[data-node-id="${id}"]`) as HTMLElement).getBoundingClientRect();
}

describe('ui.modifier layout', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function overSubscribed(): Promise<RenderedTree> {
    return renderTree(
      {
        id: 'root',
        type: UiComponents.Stack,
        properties: { [Props.Direction]: UiComponentDirections.Vertical },
        children: [
          {
            id: 'wrapper',
            type: UiComponents.Modifier,
            properties: {
              [Props.Padding]: { basis: 0.05 },
              [Props.Frame]: { height: { basis: 0.3 }, maxWidth: { basis: 0.8 }, minHeight: { basis: 0.1 } },
              [Props.Modifiers]: { background: '#224466' },
            },
            children: [
              { id: 'label', type: UiComponents.Text, properties: { [Props.Text]: 'gjpqy', [Props.Size]: { basis: 0.14 } } },
            ],
          },
          { id: 'filler', type: UiComponents.Stack, properties: { [Props.MainSize]: { basis: 0.9 } } },
        ],
      } as UiNode,
      withBasis(BASIS),
    );
  }

  it('keeps a padded, framed wrapper and its siblings inside an over-subscribed stack', async () => {
    const rendered = await overSubscribed();

    const root = rect(rendered, 'root');
    const wrapper = rect(rendered, 'wrapper');
    const filler = rect(rendered, 'filler');

    expect(wrapper.width).toBeCloseTo(BASIS * 0.8, 1);
    expect(wrapper.top).toBeGreaterThanOrEqual(root.top - 0.5);
    expect(wrapper.bottom).toBeLessThanOrEqual(filler.top + 0.5);
    expect(filler.bottom).toBeLessThanOrEqual(root.bottom + 0.5);
  });

  it('stretches a childless button to the height a filling wrapper takes from its min clamp', async () => {
    const rendered = await renderTree(
      {
        id: 'tile',
        type: UiComponents.Stack,
        properties: { [Props.Justify]: 'center', [Props.Padding]: { basis: 0.1, maxOfCell: 0.1 } },
        children: [
          {
            id: 'row',
            type: UiComponents.Stack,
            properties: { [Props.Align]: 'center', [Props.Direction]: UiComponentDirections.Horizontal, [Props.Justify]: 'center' },
            children: [
              {
                id: 'framed',
                type: UiComponents.Modifier,
                properties: {
                  [Props.Fill]: true,
                  [Props.Frame]: {
                    minWidth: { basis: 0.3 }, maxWidth: { basis: 0.6 }, minHeight: { basis: 0.25 }, maxHeight: { basis: 0.4 },
                  },
                },
                children: [{ id: 'button', type: UiComponents.Button, properties: {}, children: [] }],
              },
            ],
          },
        ],
      } as UiNode,
      withBasis(BASIS),
    );

    const framed = rect(rendered, 'framed');
    const button = rect(rendered, 'button');

    expect(framed.height).toBeCloseTo(BASIS * 0.25, 1);
    expect(button.height).toBeCloseTo(framed.height, 1);
    expect(button.width).toBeCloseTo(framed.width, 1);
  });

  it('insets the child by the padding and keeps its descenders', async () => {
    const rendered = await overSubscribed();

    const wrapper = rect(rendered, 'wrapper');
    const label = rect(rendered, 'label');
    const labelElement = el(rendered).querySelector('[data-node-id="label"]') as HTMLElement;

    expect(label.left).toBeGreaterThanOrEqual(wrapper.left + BASIS * 0.05 - 0.5);
    expect(label.right).toBeLessThanOrEqual(wrapper.right - BASIS * 0.05 + 0.5);
    expect(labelElement.scrollHeight).toBeLessThanOrEqual(labelElement.clientHeight);
  });
});
