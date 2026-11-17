import { TestBed } from '@angular/core/testing';

import { UiComponentDirections, UiComponentProperties, UiComponents, UiNode } from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { el, renderTree } from './ui-widget-render-test-support';

const BASIS = 240;

function withBasis(basis: number) {
  const context = new UiWidgetTreeContext();
  context.setBasis(basis);
  return [{ provide: UiWidgetTreeContext, useValue: context }];
}

function choices(count: number): UiNode {
  return {
    id: 'root',
    type: UiComponents.Stack,
    properties: { [UiComponentProperties.MainSize]: { basis: 1 } },
    children: [
      {
        id: 'list',
        type: UiComponents.List,
        properties: { [UiComponentProperties.Fill]: true },
        children: [
          {
            id: 'choice',
            type: UiComponents.Segmented,
            requiredComponentVersion: 2,
            properties: {
              [UiComponentProperties.Direction]: UiComponentDirections.Vertical,
              [UiComponentProperties.Selected]: 0,
              [UiComponentProperties.Events]: ['change'],
            },
            children: Array.from({ length: count }, (_, index) => ({
              id: `choice.${index}`,
              type: UiComponents.Text,
              properties: { [UiComponentProperties.Text]: `Scene ${index + 1}` },
            })),
          },
        ],
      },
    ],
  };
}

describe('a vertical ui.segmented in a ui.list', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('is as tall as its choices, so a long list scrolls instead of squeezing them', async () => {
    const rendered = await renderTree(choices(20), withBasis(BASIS));
    const host = el(rendered);
    const list = host.querySelector('[data-node-id="list"]') as HTMLElement;
    const choice = host.querySelector('[data-node-id="choice"]') as HTMLElement;

    expect(choice.getBoundingClientRect().height).toBeCloseTo(20 * 0.14 * BASIS, 0);
    expect(list.scrollHeight).toBeGreaterThan(list.clientHeight);
  });
});
