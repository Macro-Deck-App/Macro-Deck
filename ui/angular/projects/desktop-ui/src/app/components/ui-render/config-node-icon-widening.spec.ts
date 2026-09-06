import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { UiNode } from '@macro-deck/runtime';
import { WidgetIconControlComponent } from '../widget-appearance/widget-icon-control.component';
import { el, renderTree, tick } from './ui-render-test-support';

describe('shared-ui-input icon widening', () => {
  afterEach(() => TestBed.resetTestingModule());

  function iconControl(rendered: Awaited<ReturnType<typeof renderTree>>): WidgetIconControlComponent {
    return rendered.fixture.debugElement.query(By.directive(WidgetIconControlComponent))
      .componentInstance as WidgetIconControlComponent;
  }

  it('reads a bare icon-pack reference string and emits a bare string back on change', async () => {
    const root: UiNode = { id: 'n', type: 'icon', properties: { value: 'flame', events: ['change'] } };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.textContent).toContain('Change icon');
    expect(iconControl(rendered).icon).toEqual({ type: 'icon-pack', reference: 'flame' });

    iconControl(rendered).valueChange.emit({ type: 'icon-pack', reference: 'bolt' });
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'bolt' }]);
  });

  it('renders an empty icon as configurable and unset, not disabled', async () => {
    const root: UiNode = { id: 'n', type: 'icon', properties: { events: ['change'] } };
    const rendered = await renderTree(root);
    const host = el(rendered);

    expect(host.textContent).toContain('Choose icon');
    expect(iconControl(rendered).disabled).toBeFalse();
  });

  it('shows a provider-owned (non icon-pack) reference as configured, not the empty picker state, and never flattens it', async () => {
    const root: UiNode = {
      id: 'n',
      type: 'icon',
      properties: { value: { type: 'plugin-asset', reference: 'x/y.png' }, events: ['change'] },
    };
    const rendered = await renderTree(root);
    const host = el(rendered);
    const control = iconControl(rendered);

    // "Change icon" (configured), never "Choose icon" (the empty state) - and the reference is kept
    // as the full typed object, never collapsed to the bare `x/y.png` string.
    expect(host.textContent).toContain('Change icon');
    expect(host.textContent).not.toContain('Choose icon');
    expect(control.icon).toEqual({ type: 'plugin-asset', reference: 'x/y.png' });
    expect(control.disabled).toBeTrue();
  });

  it('echoes the typed shape back when the node value already used it', async () => {
    const root: UiNode = {
      id: 'n',
      type: 'icon',
      properties: { value: { type: 'icon-pack', reference: 'flame' }, events: ['change'] },
    };
    const rendered = await renderTree(root);

    iconControl(rendered).valueChange.emit({ type: 'icon-pack', reference: 'bolt' });
    await tick(rendered);

    expect(rendered.events).toEqual([
      { nodeId: 'n', name: 'change', data: { type: 'icon-pack', reference: 'bolt' } },
    ]);
  });

  it('clears a typed icon value to null, not to an empty string', async () => {
    const root: UiNode = {
      id: 'n',
      type: 'icon',
      properties: { value: { type: 'icon-pack', reference: 'flame' }, events: ['change'] },
    };
    const rendered = await renderTree(root);

    iconControl(rendered).valueChange.emit(undefined);
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: null }]);
  });

  it('clears a bare-string icon value to an empty string, not null', async () => {
    const root: UiNode = { id: 'n', type: 'icon', properties: { value: 'flame', events: ['change'] } };
    const rendered = await renderTree(root);

    iconControl(rendered).valueChange.emit(undefined);
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: '' }]);
  });
});
