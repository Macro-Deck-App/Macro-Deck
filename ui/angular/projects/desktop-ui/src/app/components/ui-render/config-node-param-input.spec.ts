import { TestBed } from '@angular/core/testing';
import { UiNode } from '@macro-deck/runtime';
import { el, renderTree } from './ui-render-test-support';

describe('shared-ui-node string field literal-only gating', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders the variable-capable field for a string without literalOnly', async () => {
    const root: UiNode = { id: 'label', type: 'string', properties: { value: 'Hello' } };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelector('shared-node-param-input')).not.toBeNull();
    expect(host.querySelector('shared-variable-text-input')).not.toBeNull();
    expect(host.querySelector('shared-template-builder')).not.toBeNull();
    // The plain literal-only control never renders alongside it.
    expect(host.querySelector('shared-input')).toBeNull();
  });

  it('renders the plain literal-only input for a string with literalOnly', async () => {
    const root: UiNode = { id: 'label', type: 'string', properties: { value: 'Hello', literalOnly: true } };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.querySelector('shared-node-param-input')).toBeNull();
    expect(host.querySelector('shared-variable-text-input')).toBeNull();
    expect(host.querySelector('input')).not.toBeNull();
  });

  it('still honours placeholder and multiline on the variable-capable field', async () => {
    const root: UiNode = {
      id: 'label',
      type: 'string',
      properties: { value: '', placeholder: 'Enter a label', multiline: true },
    };
    const rendered = await renderTree(root);
    const host = el(rendered);

    const editor = host.querySelector('shared-variable-text-input [role="textbox"]');
    expect(editor).not.toBeNull();
    expect(editor?.getAttribute('data-placeholder')).toBe('Enter a label');
    expect(editor?.classList.contains('vti-multiline')).toBeTrue();
  });

  it('forwards the value entered into the variable-capable field back as the node change event', async () => {
    const root: UiNode = { id: 'label', type: 'string', properties: { value: '', events: ['change'] } };
    const rendered = await renderTree(root);
    const host = el(rendered);

    const editor = host.querySelector('shared-variable-text-input [role="textbox"]') as HTMLElement;
    editor.textContent = 'typed';
    editor.dispatchEvent(new Event('input'));
    await rendered.fixture.whenStable();
    rendered.fixture.detectChanges();

    expect(rendered.events).toEqual([{ nodeId: 'label', name: 'change', data: 'typed' }]);
  });
});
