import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, updateTree } from './ui-widget-render-test-support';

const field = (properties: Record<string, unknown>): UiNode =>
  ({ id: 'f', type: 'ui.text-field', properties }) as UiNode;

const list = (count: number, properties: Record<string, unknown>): UiNode => {
  const children: UiNode[] = [];
  for (let index = 0; index < count; index++) {
    children.push({ id: `row-${index}`, type: 'ui.text', properties: { text: `row ${index}` } } as UiNode);
  }
  return { id: 'l', type: 'ui.list', properties, children } as UiNode;
};

describe('ui.text-field', () => {
  const input = (rendered: { fixture: unknown }) =>
    el(rendered as never).querySelector('.widget-text-field') as HTMLInputElement;

  it('draws the value the producer put in it', async () => {
    const rendered = await renderTree(field({ text: 'panzer' }));

    expect(input(rendered).value).toBe('panzer');
  });

  it('accepts no typing at all from a node that declared no events', async () => {
    const rendered = await renderTree(field({ text: 'x' }));

    // The same rule a slider and a button follow: interaction is offered only where declared.
    expect(input(rendered).readOnly).toBeTrue();
  });

  it('reports what the user typed while they are still typing', async () => {
    const rendered = await renderTree(field({ text: '', events: ['adjust', 'change'] }));

    input(rendered).value = 'pan';
    input(rendered).dispatchEvent(new Event('input'));

    expect(rendered.events).toEqual([{ nodeId: 'f', name: 'adjust', data: 'pan' }]);
  });

  it('reports the value they settled on when the field is left', async () => {
    const rendered = await renderTree(field({ text: '', events: ['change'] }));

    input(rendered).value = 'panzer';
    input(rendered).dispatchEvent(new Event('blur'));

    expect(rendered.events).toEqual([{ nodeId: 'f', name: 'change', data: 'panzer' }]);
  });

  it('does not overwrite what the user is typing when the producer echoes it back', async () => {
    const rendered = await renderTree(field({ text: '', events: ['adjust'] }));
    input(rendered).dispatchEvent(new Event('focus'));
    input(rendered).value = 'panz';

    // A filtering producer echoes the query on every keystroke; applying it would move the caret.
    await updateTree(rendered, { root: field({ text: 'pan', events: ['adjust'] }) });

    expect(input(rendered).value).toBe('panz');
  });

  it('takes the producer value again once the user has left', async () => {
    const rendered = await renderTree(field({ text: '', events: ['adjust'] }));
    input(rendered).dispatchEvent(new Event('focus'));
    input(rendered).dispatchEvent(new Event('blur'));

    await updateTree(rendered, { root: field({ text: 'cleared', events: ['adjust'] }) });

    expect(input(rendered).value).toBe('cleared');
  });
});

describe('ui.list', () => {
  const surface = (rendered: { fixture: unknown }) =>
    el(rendered as never).querySelector('.widget-list') as HTMLElement;

  it('scrolls rather than dividing its box between its children', async () => {
    const rendered = await renderTree(list(3, {}));

    expect(getComputedStyle(surface(rendered)).overflowY).toBe('auto');
    expect(el(rendered).querySelectorAll('.widget-text').length).toBe(3);
  });

  it('asks for more once it has been drawn, without waiting to be scrolled', async () => {
    // A list whose content does not fill its box has been read to the end the moment it is drawn, and
    // nothing would ever scroll to say so. How many rows the fixture's own box shows is incidental -
    // what matters is that it reported one index, once. The runtime specs pin the arithmetic.
    const rendered = await renderTree(list(20, { events: ['reveal'] }));

    expect(rendered.events.length).toBe(1);
    expect(rendered.events[0].nodeId).toBe('l');
    expect(rendered.events[0].name).toBe('reveal');
    expect(rendered.events[0].data as number).toBeGreaterThanOrEqual(0);
  });

  it('says nothing at all for a list that declared no reveal', async () => {
    const rendered = await renderTree(list(20, {}));

    surface(rendered).dispatchEvent(new Event('scroll'));

    expect(rendered.events).toEqual([]);
  });

  it('does not ask again for a position it has already asked about', async () => {
    const rendered = await renderTree(list(20, { events: ['reveal'] }));
    const asked = rendered.events.length;

    // Scrolling back up is not new ground. The index guard and the throttle both hold here; the
    // runtime specs pin the two apart on a clock.
    surface(rendered).dispatchEvent(new Event('scroll'));

    expect(rendered.events.length).toBe(asked);
  });
});
