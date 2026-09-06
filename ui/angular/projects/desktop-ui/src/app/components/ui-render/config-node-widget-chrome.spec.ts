import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree } from './ui-render-test-support';

describe('shared-ui-chrome widget editor regions', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders widget-configuration, widget-properties and widget-editor as ordered containers', async () => {
    const root: UiNode = {
      id: 'editor',
      type: 'widget-editor',
      children: [
        {
          id: 'config',
          type: 'widget-configuration',
          children: [{ id: 'a', type: 'string', properties: { label: 'A', literalOnly: true } }],
        },
        {
          id: 'props',
          type: 'widget-properties',
          children: [{ id: 'b', type: 'boolean', properties: { label: 'B' } }],
        },
      ],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.querySelector('.config-chrome-widget-editor')).not.toBeNull();
    expect(host.querySelector('.config-chrome-widget-configuration')).not.toBeNull();
    expect(host.querySelector('.config-chrome-widget-properties')).not.toBeNull();

    // Both children actually rendered as inputs - proof the three types were routed to
    // `shared-ui-chrome`, not `shared-ui-input` (which has no `@case` for them and would fall
    // through to the "unsupported" placeholder, already ruled out above).
    expect(host.querySelector('[data-node-id="a"] input')).not.toBeNull();
    expect(host.querySelector('[data-node-id="b"] shared-toggle-switch')).not.toBeNull();

    const ids = Array.from(host.querySelectorAll('[data-node-id]')).map(node => node.getAttribute('data-node-id'));
    expect(ids.indexOf('config')).toBeLessThan(ids.indexOf('props'));
    expect(ids.indexOf('a')).toBeLessThan(ids.indexOf('b'));
  });

  it('renders each region\'s own type as a plain container - not just inside widget-editor', async () => {
    const root: UiNode = {
      id: 'props',
      type: 'widget-properties',
      children: [{ id: 'only-child', type: 'string', properties: { label: 'Only', literalOnly: true } }],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();
    expect(host.querySelector('.config-chrome-widget-properties')).not.toBeNull();
    expect(host.querySelector('[data-node-id="only-child"] input')).not.toBeNull();
  });
});
