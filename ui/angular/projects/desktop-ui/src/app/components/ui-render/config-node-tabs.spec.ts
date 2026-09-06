import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, tick } from './ui-render-test-support';

async function type(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('shared-ui-chrome tabs', () => {
  afterEach(() => TestBed.resetTestingModule());

  function tabsRoot(): UiNode {
    return {
      id: 'appearance',
      type: 'tabs',
      children: [
        {
          id: 'appearance.label',
          type: 'tab',
          properties: { label: 'Label' },
          children: [
            {
              id: 'appearance.label.text',
              type: 'string',
              properties: { value: 'Hello', events: ['change'], literalOnly: true },
            },
          ],
        },
        {
          id: 'appearance.background',
          type: 'tab',
          properties: { label: 'Background' },
          children: [
            {
              id: 'appearance.background.text',
              type: 'string',
              properties: { value: '#fff', events: ['change'], literalOnly: true },
            },
          ],
        },
      ],
    };
  }

  it('draws the tab labels as a segmented control and shows only the first tab\'s children', async () => {
    const rendered = await renderTree(tabsRoot());
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.querySelector('shared-segmented-control')).not.toBeNull();

    const labels = Array.from(host.querySelectorAll('.seg-option')).map(o => o.textContent?.trim());
    expect(labels).toEqual(['Label', 'Background']);

    expect(host.querySelector('[data-node-id="appearance.label.text"] input')).not.toBeNull();
    expect(host.querySelector('[data-node-id="appearance.background.text"]')).toBeNull();
  });

  it('switches to the clicked tab\'s children and removes the previous tab\'s from the tree', async () => {
    const rendered = await renderTree(tabsRoot());
    const host = el(rendered);

    const backgroundOption = Array.from(host.querySelectorAll<HTMLElement>('.seg-option'))
      .find(option => option.textContent?.trim() === 'Background')!;
    backgroundOption.click();
    await tick(rendered);

    expect(host.querySelector('[data-node-id="appearance.background.text"] input')).not.toBeNull();
    expect(host.querySelector('[data-node-id="appearance.label.text"]')).toBeNull();
  });

  it('never puts which tab is active into an emitted event or a node value', async () => {
    const rendered = await renderTree(tabsRoot());
    const host = el(rendered);

    const backgroundOption = Array.from(host.querySelectorAll<HTMLElement>('.seg-option'))
      .find(option => option.textContent?.trim() === 'Background')!;
    backgroundOption.click();
    await tick(rendered);

    // Switching tabs alone raised nothing on the wire.
    expect(rendered.events).toEqual([]);

    const field = host.querySelector('[data-node-id="appearance.background.text"] input') as HTMLInputElement;
    await type(field, '#123456');
    await tick(rendered);

    // The only event is the field's own change - no trace of which tab produced it or is active.
    expect(rendered.events).toEqual([
      { nodeId: 'appearance.background.text', name: 'change', data: '#123456' },
    ]);
  });

  it('renders a bare tab as a plain ordered container, outside of any tabs parent', async () => {
    const root: UiNode = {
      id: 'solo',
      type: 'tab',
      properties: { label: 'Solo' },
      children: [{ id: 'solo.field', type: 'string', properties: { label: 'Field', literalOnly: true } }],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.querySelector('.config-chrome-tab')).not.toBeNull();
    expect(host.querySelector('[data-node-id="solo.field"] input')).not.toBeNull();
  });
});
