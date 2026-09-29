import { TestBed } from '@angular/core/testing';

import {
  UiComponentDirections,
  UiComponentListAnchors,
  UiComponentProperties,
  UiComponents,
  UiNode,
} from '@macro-deck/runtime';
import { UiWidgetTreeContext } from './ui-widget-tree-context';
import { RenderedTree, el, renderTree, tick, updateTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

const BASIS = 240;
const TEXT_SIZE = 0.1;
const PIXEL = 1;

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
    properties: { [Props.Text]: `message ${index}`, [Props.Size]: { basis: TEXT_SIZE } },
  };
}

function feed(from: number, to: number): UiNode {
  const children: UiNode[] = [];
  for (let index = from; index < to; index++) children.push(message(index));
  return {
    id: 'root',
    type: Types.Stack,
    properties: { [Props.Direction]: UiComponentDirections.Vertical },
    children: [
      {
        id: 'feed',
        type: Types.List,
        requiredComponentVersion: 3,
        properties: {
          [Props.Anchor]: UiComponentListAnchors.End,
          [Props.MainSize]: { basis: 0.5 },
          [Props.Padding]: { basis: 0.02 },
        },
        children,
      },
    ],
  };
}

function distanceToEnd(list: HTMLElement): number {
  return list.scrollHeight - list.scrollTop - list.clientHeight;
}

function jump(rendered: RenderedTree): HTMLElement | null {
  return el(rendered).querySelector('.widget-list-jump') as HTMLElement | null;
}

function shown(element: HTMLElement | null): boolean {
  return element !== null && getComputedStyle(element).display !== 'none';
}

// Rows finish growing in the frame after a paint, as their text fits; the list catches up there.
function frame(): Promise<void> {
  return new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

async function scrollBy(rendered: RenderedTree, list: HTMLElement, top: number): Promise<void> {
  await frame();
  list.scrollTop = top;
  await frame();
  await tick(rendered);
}

describe('ui.list anchored at its end', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('opens at the end, with the newest row whole inside the box', async () => {
    const rendered = await renderTree(feed(0, 20), withBasis(BASIS));

    const list = node(rendered, 'feed');
    await frame();
    const box = list.getBoundingClientRect();
    const last = node(rendered, 'message-19').getBoundingClientRect();

    expect(list.scrollHeight).withContext('the rows overflow the box').toBeGreaterThan(list.clientHeight);
    expect(distanceToEnd(list)).toBeLessThanOrEqual(PIXEL);
    expect(last.bottom).toBeLessThanOrEqual(box.bottom + PIXEL);
    expect(shown(jump(rendered))).toBeFalse();
  });

  it('follows rows appended while the view is at the end', async () => {
    const rendered = await renderTree(feed(0, 20), withBasis(BASIS));

    await updateTree(rendered, { root: feed(1, 21) });
    await updateTree(rendered, { root: feed(2, 22) });
    await frame();

    const list = node(rendered, 'feed');
    expect(distanceToEnd(list)).toBeLessThanOrEqual(PIXEL);
    expect(node(rendered, 'message-21').getBoundingClientRect().bottom)
      .toBeLessThanOrEqual(list.getBoundingClientRect().bottom + PIXEL);
  });

  it('keeps a view the user scrolled up to still while rows are evicted and appended', async () => {
    const rendered = await renderTree(feed(0, 20), withBasis(BASIS));
    const list = node(rendered, 'feed');
    await scrollBy(rendered, list, 60);
    const watched = node(rendered, 'message-8');
    const before = watched.getBoundingClientRect().top;

    await updateTree(rendered, { root: feed(1, 21) });
    await updateTree(rendered, { root: feed(2, 22) });
    await frame();

    expect(watched.getBoundingClientRect().top).toBeCloseTo(before, 0);
    expect(distanceToEnd(list)).toBeGreaterThan(PIXEL);

    const pill = jump(rendered)!;
    expect(shown(pill)).withContext('new rows arrived while away').toBeTrue();
    const box = list.getBoundingClientRect();
    const pillBox = pill.getBoundingClientRect();
    expect(pillBox.bottom).toBeLessThanOrEqual(box.bottom + PIXEL);
    expect(pillBox.top).toBeGreaterThanOrEqual(box.top);
  });

  it('goes back to the end and follows again once the way back is pressed', async () => {
    const rendered = await renderTree(feed(0, 20), withBasis(BASIS));
    const list = node(rendered, 'feed');
    await scrollBy(rendered, list, 60);
    await updateTree(rendered, { root: feed(1, 21) });

    jump(rendered)!.click();
    await tick(rendered);
    await frame();

    expect(shown(jump(rendered))).toBeFalse();
    expect(distanceToEnd(list)).toBeLessThanOrEqual(PIXEL);

    await updateTree(rendered, { root: feed(2, 22) });
    await frame();
    expect(distanceToEnd(list)).toBeLessThanOrEqual(PIXEL);
  });
});
