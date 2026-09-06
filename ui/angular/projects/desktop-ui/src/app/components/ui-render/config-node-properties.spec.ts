import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, updateTree } from './ui-render-test-support';

describe('shared-ui-node property typing and visibility', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('ignores wrongly-typed properties instead of coercing them', async () => {
    const root: UiNode = {
      id: 'n',
      type: 'number',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      properties: { label: 'Port', required: 'true', maxLength: '5', min: '1', disabled: 'false', showSlider: 1 } as any,
    };
    const rendered = await renderTree(root);
    const host = el(rendered);
    const input = host.querySelector('input[type="number"]') as HTMLInputElement;

    expect(input).not.toBeNull();
    expect(input.hasAttribute('required')).toBeFalse();
    expect(input.hasAttribute('maxlength')).toBeFalse();
    expect(input.hasAttribute('min')).toBeFalse();
    expect(input.disabled).toBeFalse();
    expect(host.querySelector('input[type="range"]')).toBeNull();
    expect(host.querySelector('.config-node-label')?.textContent).toContain('Port');
  });

  it('resolves visibleWhen case-insensitively and keeps a hidden value', async () => {
    const tree = (mode: string): UiNode => ({
      id: 'root',
      type: 'stack',
      children: [
        { id: 'mode', type: 'choice', properties: { value: mode } },
        {
          id: 'timeout',
          type: 'number',
          properties: { value: 30, visibleWhen: { parameterName: 'mode', values: ['advanced', 'expert'] } },
        },
        {
          id: 'orphan',
          type: 'string',
          properties: { visibleWhen: { parameterName: 'nosuchfield', values: ['x'] }, literalOnly: true },
        },
      ],
    });

    const rendered = await renderTree(tree('ADVANCED'));
    const host = el(rendered);

    expect(host.querySelector('[data-node-id="timeout"] input')).not.toBeNull();
    expect(host.querySelector('[data-node-id="orphan"] input')).not.toBeNull();

    await updateTree(rendered, { root: tree('basic') });
    expect(host.querySelector('[data-node-id="timeout"] input')).toBeNull();
    expect(host.querySelector('[data-node-id="orphan"] input')).not.toBeNull();

    await updateTree(rendered, { root: tree('ADVANCED') });
    const timeoutInput = host.querySelector('[data-node-id="timeout"] input') as HTMLInputElement;
    expect(timeoutInput).not.toBeNull();
    expect(timeoutInput.value).toBe('30');
  });

  it('never composes ids: children already arrive fully scoped', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        {
          id: 'server',
          type: 'object',
          children: [{ id: 'server.name', type: 'string', properties: { label: 'Name', literalOnly: true } }],
        },
        { id: 'name', type: 'string', properties: { label: 'Name', literalOnly: true } },
      ],
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    const nested = host.querySelector('[data-node-id="server.name"] input') as HTMLInputElement;
    const top = host.querySelector('#name') as HTMLInputElement;
    expect(nested.id).toBe('server.name');
    expect(nested.getAttribute('name')).toBe('server.name');
    expect(top).not.toBeNull();
    expect(top.getAttribute('name')).toBe('name');
    expect(host.querySelectorAll('#name').length).toBe(1);
  });

  it('resolves a bare visibleWhen parameter name inside the object scope that owns it', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        { id: 'mode', type: 'choice', properties: { value: 'off' } },
        {
          id: 'server',
          type: 'object',
          children: [
            { id: 'server.mode', type: 'choice', properties: { value: 'manual' } },
            {
              id: 'server.host',
              type: 'string',
              properties: {
                label: 'Host',
                visibleWhen: { parameterName: 'mode', values: ['manual'] },
                literalOnly: true,
              },
            },
          ],
        },
      ],
    };
    const rendered = await renderTree(root);

    expect(el(rendered).querySelector('[data-node-id="server.host"] input')).not.toBeNull();
  });
});
