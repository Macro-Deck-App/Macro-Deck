import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree, updateTree } from './ui-render-test-support';

describe('shared-ui-node validation and errors', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows no error state when neither invalid nor validationMessage is set', async () => {
    const root: UiNode = {
      id: 'apiKey',
      type: 'string',
      properties: { required: true, value: '', maxLength: 4, literalOnly: true },
    };
    const rendered = await renderTree(root);
    const host = el(rendered);
    const input = host.querySelector('input') as HTMLInputElement;

    expect(input.getAttribute('aria-invalid')).not.toBe('true');
    expect(host.querySelector('.config-node-error')).toBeNull();
    expect(host.textContent).not.toMatch(/required/i);
  });

  it('shows the explicit validation message and wires aria-invalid/aria-describedby', async () => {
    const root: UiNode = {
      id: 'name',
      type: 'string',
      properties: {
        value: 'a perfectly fine value',
        invalid: true,
        validationMessage: 'Name is already taken',
        literalOnly: true,
      },
    };
    const rendered = await renderTree(root);
    const host = el(rendered);
    const input = host.querySelector('input') as HTMLInputElement;

    expect(input.getAttribute('aria-invalid')).toBe('true');
    const errors = host.querySelectorAll('.config-node-error');
    expect(errors.length).toBe(1);
    expect(errors[0].textContent).toBe('Name is already taken');
    expect(input.getAttribute('aria-describedby')).toContain(errors[0].id);
  });

  it('renders required/maxLength/placeholder as DOM affordances only, with no validation logic', async () => {
    const root: UiNode = {
      id: 'apiKey',
      type: 'string',
      properties: { label: 'API key', required: true, maxLength: 32, placeholder: 'sk-…', literalOnly: true },
    };
    const rendered = await renderTree(root);
    const host = el(rendered);
    const input = host.querySelector('input') as HTMLInputElement;

    expect(input.hasAttribute('required')).toBeTrue();
    expect(input.getAttribute('maxlength')).toBe('32');
    expect(input.getAttribute('placeholder')).toBe('sk-…');
    expect(host.querySelector('.config-node-label')?.textContent).toContain('API key');
    expect(host.querySelector('.config-node-required')).not.toBeNull();
    expect(input.getAttribute('aria-invalid')).not.toBe('true');
    expect(host.querySelector('.config-node-error')).toBeNull();
  });

  it('hoists a validation-message onto the input named by "for"', async () => {
    const tree = (includeMessage: boolean): UiNode => ({
      id: 'root',
      type: 'stack',
      children: [
        { id: 'host', type: 'string', properties: { value: 'a', literalOnly: true } },
        { id: 'port', type: 'number', properties: { value: 1024 } },
        ...(includeMessage
          ? [{ id: 'msg1', type: 'validation-message', properties: { text: 'Port must be above 1024', for: 'port' } }]
          : []),
      ],
    });

    const rendered = await renderTree(tree(true));
    const host = el(rendered);
    const portInput = host.querySelector('[data-node-id="port"] input') as HTMLInputElement;
    const hostInput = host.querySelector('[data-node-id="host"] input') as HTMLInputElement;
    // The validation-message node is hoisted into port's own error slot, not rendered at its own
    // tree position - shared-ui-node still wraps it, but with no visible content of its own.
    expect(host.querySelector('[data-node-id="msg1"]')?.textContent?.trim()).toBe('');
    const message = host.querySelector('[data-node-id="port"] .config-node-error');

    expect(message).not.toBeNull();
    expect(message?.id).toBe('msg1');
    expect(message?.textContent).toBe('Port must be above 1024');
    expect(portInput.getAttribute('aria-describedby')).toContain('msg1');
    expect(hostInput.getAttribute('aria-describedby') ?? '').not.toContain('msg1');
    expect(portInput.value).toBe('1024');

    await updateTree(rendered, { root: tree(false) });

    expect(host.querySelector('[data-node-id="port"] .config-node-error')).toBeNull();
    const portAfter = host.querySelector('[data-node-id="port"] input') as HTMLInputElement;
    expect(portAfter.getAttribute('aria-describedby') ?? '').toBe('');
    expect(portAfter.value).toBe('1024');
  });
});
