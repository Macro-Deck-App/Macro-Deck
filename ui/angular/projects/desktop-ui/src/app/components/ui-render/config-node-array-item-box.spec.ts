import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree } from './ui-render-test-support';

describe('shared-ui-input array items with nothing to render', () => {
  afterEach(() => TestBed.resetTestingModule());

  function root(): UiNode {
    return {
      id: 'states',
      type: 'array',
      properties: { events: ['change'] },
      children: [
        {
          id: 'states.off',
          type: 'object',
          children: [
            { id: 'states.off.label', type: 'string', properties: { value: 'Off', events: ['change'] } },
          ],
        },
        // Not selected: this state's UiObjectInput has no children at all, exactly as UiWhen with a
        // false condition materializes.
        { id: 'states.on', type: 'object', children: [] },
      ],
    };
  }

  it('draws a box only for the item that actually renders something', async () => {
    const rendered = await renderTree(root());
    const host = el(rendered);

    const boxes = host.querySelectorAll('.config-node-array-item');
    expect(boxes.length).toBe(1);

    const renderedOff = host.querySelector('[data-node-id="states.off"]');
    expect(renderedOff).not.toBeNull();
    expect(renderedOff?.closest('.config-node-array-item')).not.toBeNull();

    // The empty item contributes no box and no other markup either - there is nothing for it to
    // show - rather than an empty bordered rectangle taking up visible space in its place.
    expect(host.querySelector('[data-node-id="states.on"]')).toBeNull();
    expect(host.textContent).not.toContain('states.on');
  });

  it('still reports the array as carrying both items, for whatever reads the tree the host pushed', () => {
    const tree = root();
    expect(tree.children?.map(child => child.id)).toEqual(['states.off', 'states.on']);
  });
});
