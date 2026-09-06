import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { EMPTY, Observable } from 'rxjs';
import { UiNode } from '@macro-deck/runtime';
import { ApiService, ConnectionState, VariableService } from '@shared';
import { SelectComponent } from '../forms/select/select.component';
import { el, renderTree, tick } from './ui-render-test-support';

function fakeApi(): jasmine.SpyObj<ApiService> {
  const api = jasmine.createSpyObj<ApiService>('ApiService', ['getVariables', 'getIntegrations', 'onNotification']);
  api.getVariables.and.resolveTo({ variables: [] });
  api.getIntegrations.and.resolveTo({ integrations: [] });
  api.onNotification.and.callFake(<T>(): Observable<T> => EMPTY);
  (api as unknown as { connectionStateSignal: () => ConnectionState }).connectionStateSignal =
    () => 'disconnected';
  return api;
}

function mappingNode(properties: Record<string, unknown> = {}): UiNode {
  return {
    id: 'stateMapping',
    type: 'state-mapping-editor',
    properties: {
      value: { rules: [], fallbackStateId: 'off' },
      states: [{ value: 'off', label: 'Off' }, { value: 'on', label: 'On' }],
      events: ['change'],
      ...properties,
    },
  };
}

async function render(root: UiNode) {
  const rendered = await renderTree(root, null, [
    { provide: ApiService, useValue: fakeApi() },
    { provide: VariableService, useValue: { visibleForActionButtonEditor: () => [] } },
  ]);
  rendered.fixture.detectChanges();
  await rendered.fixture.whenStable();
  return rendered;
}

describe('shared-ui-node state-mapping-editor', () => {
  // shared-modal defers its close/save callback behind a 150ms close animation - see
  // recovery-key-modal.component.spec.ts for the same pattern.
  beforeEach(() => {
    // Matched by its English label, the modal's Save button is only found while no other suite's
    // cached catalog is left in localStorage for LocalizationService to restore.
    localStorage.clear();
    jasmine.clock().install();
  });
  afterEach(() => {
    jasmine.clock().uninstall();
    TestBed.resetTestingModule();
  });

  it('summarises an unset mapping as None and never opens the dialog unasked', async () => {
    const rendered = await render(mappingNode());
    const host = el(rendered);

    expect(host.textContent).toContain('None');
    expect(host.querySelector('app-state-mapping-modal')).toBeNull();
  });

  it('summarises a mapping with rules by count, not None', async () => {
    const rendered = await render(mappingNode({
      value: {
        rules: [{ id: 'r1', stateId: 'on', when: { kind: 'compare', id: 'c1', left: '', operator: '==', right: '' } }],
        fallbackStateId: 'off',
      },
    }));
    const host = el(rendered);

    expect(host.textContent).not.toContain('None');
    expect(host.querySelector('.state-mapping-value')?.textContent?.trim().length).toBeGreaterThan(0);
  });

  it('opens the state mapping dialog on click, and closes it on cancel', async () => {
    const rendered = await render(mappingNode());
    const host = el(rendered);

    host.querySelector<HTMLButtonElement>('.state-mapping-row')!.click();
    await tick(rendered);

    expect(host.querySelector('app-state-mapping-modal')).not.toBeNull();

    const cancel = Array.from(host.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Cancel');
    cancel?.click();
    jasmine.clock().tick(150);
    await tick(rendered);

    expect(host.querySelector('app-state-mapping-modal')).toBeNull();
  });

  it('offers the node\'s own states to the dialog, not a hard-coded list', async () => {
    const rendered = await render(mappingNode({
      value: { rules: [], fallbackStateId: 'muted' },
      states: [{ value: 'muted', label: 'Muted' }, { value: 'unmuted', label: 'Unmuted' }],
    }));
    const host = el(rendered);

    host.querySelector<HTMLButtonElement>('.state-mapping-row')!.click();
    await tick(rendered);

    // With no rules yet, the fallback row's select is the only one in the tree.
    const [fallbackSelect] = rendered.fixture.debugElement
      .queryAll(By.directive(SelectComponent))
      .map(debugEl => debugEl.componentInstance as SelectComponent);

    expect(fallbackSelect?.options.map(o => o.label)).toEqual(['Muted', 'Unmuted']);
  });

  // A button gains its mapping key with an empty fallback, and the modal's Save is disabled until the
  // fallback names a real state - with nothing on screen saying which precondition is missing.
  it('preselects the first state when the stored fallback names none', async () => {
    const rendered = await render(mappingNode({ value: { rules: [], fallbackStateId: '' } }));
    const host = el(rendered);

    host.querySelector<HTMLButtonElement>('.state-mapping-row')!.click();
    await tick(rendered);

    const [fallbackSelect] = rendered.fixture.debugElement
      .queryAll(By.directive(SelectComponent))
      .map(debugEl => debugEl.componentInstance as SelectComponent);

    expect(fallbackSelect?.value).toBe('off');
  });

  it('preselects the first state when the stored fallback names a state that is gone', async () => {
    const rendered = await render(mappingNode({ value: { rules: [], fallbackStateId: 'deleted' } }));
    const host = el(rendered);

    host.querySelector<HTMLButtonElement>('.state-mapping-row')!.click();
    await tick(rendered);

    const [fallbackSelect] = rendered.fixture.debugElement
      .queryAll(By.directive(SelectComponent))
      .map(debugEl => debugEl.componentInstance as SelectComponent);

    expect(fallbackSelect?.value).toBe('off');
  });

  it('leaves a fallback that names a real state alone', async () => {
    const rendered = await render(mappingNode({ value: { rules: [], fallbackStateId: 'on' } }));
    const host = el(rendered);

    host.querySelector<HTMLButtonElement>('.state-mapping-row')!.click();
    await tick(rendered);

    const [fallbackSelect] = rendered.fixture.debugElement
      .queryAll(By.directive(SelectComponent))
      .map(debugEl => debugEl.componentInstance as SelectComponent);

    expect(fallbackSelect?.value).toBe('on');
  });

  it('commits the edited mapping back onto the node as a change event on save', async () => {
    const rendered = await render(mappingNode());

    const host = el(rendered);
    host.querySelector<HTMLButtonElement>('.state-mapping-row')!.click();
    await tick(rendered);

    const save = Array.from(host.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Save');
    expect(save).withContext('the fallback is already a live state, so an empty mapping is savable').not.toBeUndefined();
    save?.click();
    jasmine.clock().tick(150);
    await tick(rendered);

    const change = rendered.events.find(e => e.name === 'change');
    expect(change).withContext('saving an unmodified mapping still commits it').not.toBeUndefined();
    expect((change?.data as { fallbackStateId: string } | undefined)?.fallbackStateId).toBe('off');
  });
});
