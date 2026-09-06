import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, tick, updateTree } from './ui-render-test-support';

describe('shared-ui-node dispatch, support and degradation', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders exactly one placeholder for an unknown type with no fallback', async () => {
    const root: UiNode = { id: 'f1', type: 'quantum-slider', properties: { label: 'Speed' } };
    const rendered = await renderTree(root);
    const host = el(rendered);

    const placeholders = host.querySelectorAll('.config-node-unsupported');
    expect(placeholders.length).toBe(1);
    expect(placeholders[0].textContent?.trim()).toBe("This field isn't supported by this version of Macro Deck");
    expect(placeholders[0].getAttribute('data-unsupported-type')).toBe('quantum-slider');
    expect(placeholders[0].getAttribute('title')).toBe('quantum-slider');
    expect(placeholders[0].textContent).not.toContain('quantum-slider');
    expect(host.querySelector('input, select, textarea')).toBeNull();
  });

  it('recurses into a fallback chain and negotiates it in turn', async () => {
    const root: UiNode = {
      id: 'chart',
      type: 'chart',
      requiredComponentVersion: 2,
      children: [{ id: 'inner', type: 'string', properties: { label: 'Inner', literalOnly: true } }],
      fallback: {
        id: 'chart-fb',
        type: 'also-unknown',
        fallback: { id: 'chart-fb2', type: 'prose', properties: { text: 'Charts need a newer app' } },
      },
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    const resolved = host.querySelector('[data-node-id="chart-fb2"]');
    expect(resolved).not.toBeNull();
    expect(resolved?.textContent).toContain('Charts need a newer app');
    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.querySelector('[data-node-id="inner"]')).toBeNull();
  });

  it('keeps rendering the rest of the tree when one sibling degrades', async () => {
    spyOn(console, 'error');
    const root: UiNode = {
      id: 'stack',
      type: 'stack',
      children: [
        { id: 'a', type: 'string', properties: { label: 'A', literalOnly: true } },
        { id: 'boom', type: 'quantum-slider', properties: {} },
        { id: 'b', type: 'boolean', properties: { label: 'B' } },
      ],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelector('[data-node-id="a"] input')).not.toBeNull();
    expect(host.querySelector('[data-node-id="b"] shared-toggle-switch')).not.toBeNull();
    expect(host.querySelectorAll('.config-node-unsupported').length).toBe(1);

    const ids = Array.from(host.querySelectorAll('[data-node-id]')).map(node => node.getAttribute('data-node-id'));
    expect(ids.indexOf('a')).toBeLessThan(ids.indexOf('boom'));
    expect(ids.indexOf('boom')).toBeLessThan(ids.indexOf('b'));
    expect(console.error).not.toHaveBeenCalled();
  });

  it('negotiates the required component version without clamping', async () => {
    const root: UiNode = {
      id: 'stack',
      type: 'stack',
      children: [
        { id: 'unversioned', type: 'string', properties: { label: 'A', literalOnly: true } },
        { id: 'v1', type: 'string', requiredComponentVersion: 1, properties: { label: 'B', literalOnly: true } },
        { id: 'v2', type: 'string', requiredComponentVersion: 2, properties: { label: 'C', literalOnly: true } },
      ],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelector('[data-node-id="unversioned"] input')).not.toBeNull();
    expect(host.querySelector('[data-node-id="v1"] input')).not.toBeNull();
    expect(host.querySelector('[data-node-id="v2"] input')).toBeNull();
    expect(host.querySelector('[data-node-id="v2"] .config-node-unsupported')).not.toBeNull();
    expect(host.querySelector('[data-node-id="v2"] .config-node-unsupported')?.getAttribute('data-unsupported-type'))
      .toBe('string');
  });

  it('tracks a node by id across a re-render', async () => {
    const stack = (ids: string[]): UiNode => ({
      id: 'stack',
      type: 'stack',
      children: ids.map(id => ({ id, type: 'string', properties: { label: id, literalOnly: true } })),
    });

    const rendered = await renderTree(stack(['a', 'b']));
    const before = el(rendered).querySelector('[data-node-id="b"] input');
    expect(before).not.toBeNull();

    await updateTree(rendered, { root: stack(['z', 'a', 'b']) });

    const after = el(rendered).querySelector('[data-node-id="b"] input');
    expect(after).toBe(before);
  });

  it('renders a fallback that resolves to an editable input as fully interactive, not just static text', async () => {
    const root: UiNode = {
      id: 'n',
      type: 'quantum-slider',
      fallback: { id: 'n-fb', type: 'string', properties: { label: 'Fallback', events: ['change'], literalOnly: true } },
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelectorAll('.config-node-unsupported').length).toBe(0);
    expect(host.querySelectorAll('input').length).toBe(1);

    const input = host.querySelector('input') as HTMLInputElement;
    input.value = 'typed';
    input.dispatchEvent(new Event('input'));
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n-fb', name: 'change', data: 'typed' }]);
  });

  it('drops an unsupported node and every one of its children with no fallback, while the rest of the tree still renders', async () => {
    const root: UiNode = {
      id: 'stack',
      type: 'stack',
      children: [
        { id: 'before', type: 'string', properties: { label: 'Before', literalOnly: true } },
        {
          id: 'boom',
          type: 'quantum-slider',
          children: [
            { id: 'orphan-1', type: 'string', properties: { label: 'Orphan 1', literalOnly: true } },
            { id: 'orphan-2', type: 'boolean', properties: { label: 'Orphan 2' } },
          ],
        },
        { id: 'after', type: 'boolean', properties: { label: 'After' } },
      ],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelectorAll('.config-node-unsupported').length).toBe(1);
    expect(host.querySelector('[data-node-id="orphan-1"]')).toBeNull();
    expect(host.querySelector('[data-node-id="orphan-2"]')).toBeNull();
    expect(host.querySelectorAll('[data-node-id]').length).toBe(4);
    expect(host.querySelector('[data-node-id="before"] input')).not.toBeNull();
    expect(host.querySelector('[data-node-id="after"] shared-toggle-switch')).not.toBeNull();
  });
});
