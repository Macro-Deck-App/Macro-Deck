import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, tick, updateTree } from './ui-render-test-support';

async function type(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('shared-ui-tree value overlay', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('falls back to defaultValue when neither a map, an overlay nor a value is present', async () => {
    const root: UiNode = {
      id: 'name',
      type: 'string',
      properties: { label: 'Name', defaultValue: 'from default', literalOnly: true },
    };
    const rendered = await renderTree(root);

    expect((el(rendered).querySelector('input') as HTMLInputElement).value).toBe('from default');
  });

  it('writes the overlay and emits change when no external map is supplied', async () => {
    const root: UiNode = {
      id: 'name',
      type: 'string',
      properties: { label: 'Name', value: 'from tree', events: ['change'], literalOnly: true },
    };
    const rendered = await renderTree(root);
    const input = el(rendered).querySelector('input') as HTMLInputElement;
    expect(input.value).toBe('from tree');

    await type(input, 'edited');
    await tick(rendered);

    expect(input.value).toBe('edited');
    expect(rendered.events).toEqual([{ nodeId: 'name', name: 'change', data: 'edited' }]);
  });

  it('lets a fresh external map win over any pending local edit', async () => {
    const root: UiNode = {
      id: 'name',
      type: 'string',
      properties: { label: 'Name', value: 'from tree', events: ['change'], literalOnly: true },
    };
    const rendered = await renderTree(root, { name: 'from mount' });
    const input = el(rendered).querySelector('input') as HTMLInputElement;
    expect(input.value).toBe('from mount');

    await type(input, 'typed');
    await tick(rendered);

    await updateTree(rendered, { values: { name: 'from mount 2' } });

    expect(input.value).toBe('from mount 2');
  });

  it('falls back to a node\'s own value, not empty, when its key is absent from the map', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        { id: 'a', type: 'string', properties: { value: 'A', literalOnly: true } },
        { id: 'b', type: 'string', properties: { value: 'B', literalOnly: true } },
      ],
    };
    const rendered = await renderTree(root, { a: 'A from mount' });
    const host = el(rendered);

    expect((host.querySelector('[data-node-id="a"] input') as HTMLInputElement).value).toBe('A from mount');
    expect((host.querySelector('[data-node-id="b"] input') as HTMLInputElement).value).toBe('B');
  });
});
