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

function text(id: string, value: string, extra: Record<string, unknown> = {}): UiNode {
  return { id, type: Types.Text, properties: { [Props.Text]: value, [Props.Size]: { basis: 0.06 }, ...extra } };
}

function row(caption: string, minSize?: number, fill = false): UiNode {
  const captionProps = minSize === undefined ? {} : { [Props.MinSize]: { basis: minSize } };
  const layout = (id: string, direction: string): UiNode => ({
    id,
    type: Types.Stack,
    properties: { [Props.Direction]: direction },
    children: [text(`${id}.name`, 'Mouse'), text(`${id}.caption`, caption, captionProps)],
  });
  return {
    id: 'root',
    type: Types.Stack,
    properties: {},
    children: [{
      id: 'group',
      type: Types.FirstFit,
      properties: fill ? { [Props.Fill]: true } : {},
      children: [layout('inline', UiComponentDirections.Horizontal), layout('stacked', UiComponentDirections.Vertical)],
    }],
  };
}

function shown(rendered: RenderedTree): string[] {
  const group = el(rendered).querySelector('[data-node-id="group"]')!;
  return Array.from(group.children)
    .filter(child => child.hasAttribute('data-node-id') && !child.hasAttribute('data-first-fit-hidden'))
    .map(child => child.getAttribute('data-node-id')!);
}

describe('ui.first-fit', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('draws the first layout when its texts fit the box', async () => {
    const rendered = await renderTree(row('full'), withBasis(BASIS));

    expect(shown(rendered)).toEqual(['inline']);
  });

  it('draws the last layout when the texts of the first would be cut off', async () => {
    const rendered = await renderTree(row('0:35 to full charge, 13% per hour'), withBasis(BASIS));

    expect(shown(rendered)).toEqual(['stacked']);
  });

  it('picks by the box the node is filled to, not only by the room an auto-sized node gets', async () => {
    const wide = await renderTree(row('full', undefined, true), withBasis(BASIS));
    expect(shown(wide)).toEqual(['inline']);

    const long = await renderTree(row('0:35 to full charge, 13% per hour', undefined, true), withBasis(BASIS));
    expect(shown(long)).toEqual(['stacked']);
  });

  it('chooses again when a text changes to one that no longer fits, and back', async () => {
    const rendered = await renderTree(row('full'), withBasis(BASIS));
    expect(shown(rendered)).toEqual(['inline']);

    await updateTree(rendered, { root: row('0:35 to full charge, 13% per hour') });
    expect(shown(rendered)).toEqual(['stacked']);

    await updateTree(rendered, { root: row('full') });
    expect(shown(rendered)).toEqual(['inline']);
  });

  it('counts a text that shrinks to its minimum size as fitting', async () => {
    const rendered = await renderTree(row('0:35 to full charge', 0.02), withBasis(BASIS));

    expect(shown(rendered)).toEqual(['inline']);
  });
});
