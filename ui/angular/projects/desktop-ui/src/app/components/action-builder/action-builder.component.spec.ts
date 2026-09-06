import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { Subject } from 'rxjs';

import { ActionBlock, ActionBlockDefinition, ActionFlow } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { ActionBuilderComponent } from './action-builder.component';
import { ActionFlowStore } from './services/action-flow.store';

function fakeApiService(extra: Record<string, unknown> = {}): Partial<ApiService> {
  return {
    onNotification: () => new Subject(),
    connectionStateSignal: signal('disconnected') as never,
    ...extra,
  };
}

describe('ActionBuilderComponent aux tab', () => {
  let fixture: ComponentFixture<ActionBuilderComponent>;
  let component: ActionBuilderComponent;
  let store: ActionFlowStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: fakeApiService() }],
    })
      .overrideComponent(ActionBuilderComponent, { set: { template: '', imports: [] } })
      .compileComponents();

    fixture = TestBed.createComponent(ActionBuilderComponent);
    component = fixture.componentInstance;
    store = fixture.debugElement.injector.get(ActionFlowStore);
    fixture.detectChanges();
  });

  // Issue #741: a surface only gets the state affordances if it says it can consume states, so the
  // default has to be "no" and only an explicit binding may turn it on.
  it('assumes the surface cannot consume action states until it says otherwise', () => {
    expect(store.consumerSupportsStateProvider()).toBeFalse();

    fixture.componentRef.setInput('supportsStateProvider', true);
    fixture.detectChanges();

    expect(store.consumerSupportsStateProvider()).toBeTrue();
  });

  // The icon-provider sibling of the #741 guard above (issue #425), wired through its own input.
  it('assumes the surface cannot consume an action icon until it says otherwise', () => {
    expect(store.consumerSupportsIconProvider()).toBeFalse();

    fixture.componentRef.setInput('supportsIconProvider', true);
    fixture.detectChanges();

    expect(store.consumerSupportsIconProvider()).toBeTrue();
  });

  it('mirrors iconProviderBlockId down to the store, independently of stateProviderBlockId', () => {
    fixture.componentRef.setInput('iconProviderBlockId', 'blk-icon');
    fixture.componentRef.setInput('stateProviderBlockId', 'blk-state');
    fixture.detectChanges();

    expect(store.iconProviderBlockId()).toBe('blk-icon');
    expect(store.stateProviderBlockId()).toBe('blk-state');
  });

  it('re-emits an icon-provider toggle request from the store as iconProviderChange', () => {
    const requests: unknown[] = [];
    component.iconProviderChange.subscribe((r: unknown) => requests.push(r));

    store.requestIconProviderToggle({
      blockId: 'blk-1', integrationId: 'i', actionId: 'a', actionLabel: 'A', checked: true,
    });

    expect(requests).toEqual([{ blockId: 'blk-1', integrationId: 'i', actionId: 'a', actionLabel: 'A', checked: true }]);
  });

  it('exposes the aux tab as its own item when configured', () => {
    fixture.componentRef.setInput('auxTabId', 'stateBinding');
    fixture.componentRef.setInput('auxTabLabel', 'State Binding');
    fixture.componentRef.setInput('auxTabDot', true);
    fixture.detectChanges();

    const aux = component.auxTabItem();
    expect(aux).toBeDefined();
    expect(aux!.id).toBe('stateBinding');
    expect(aux!.label).toBe('State Binding');
    expect(aux!.dot).toBe(true);
  });

  it('exposes the aux tab as a single-item list for its own tab bar', () => {
    expect(component.auxTabItems()).toEqual([]);

    fixture.componentRef.setInput('auxTabId', 'stateBinding');
    fixture.componentRef.setInput('auxTabLabel', 'State Binding');
    fixture.detectChanges();

    const items = component.auxTabItems();
    expect(items.length).toBe(1);
    expect(items[0].id).toBe('stateBinding');
  });

  it('never folds the aux tab into triggerTabItems, even when configured (#42)', () => {
    fixture.componentRef.setInput('auxTabId', 'stateBinding');
    fixture.componentRef.setInput('auxTabLabel', 'State Binding');
    fixture.detectChanges();

    expect(component.triggerTabItems().some(t => t.id === 'stateBinding')).toBeFalse();
  });

  it('has no aux tab and is never aux-selected without an aux id', () => {
    expect(component.auxTabItem()).toBeUndefined();
    store.selectedTriggerId.set('stateBinding');
    fixture.detectChanges();
    expect(component.isAuxSelected()).toBeFalse();
  });

  it('reports the aux tab as selected only when it is the active trigger', () => {
    fixture.componentRef.setInput('auxTabId', 'stateBinding');
    fixture.detectChanges();
    expect(component.isAuxSelected()).toBeFalse();

    store.selectedTriggerId.set('stateBinding');
    fixture.detectChanges();
    expect(component.isAuxSelected()).toBeTrue();
  });

  it('does not reset the selection away from the aux tab while it is active', () => {
    fixture.componentRef.setInput('auxTabId', 'stateBinding');
    fixture.detectChanges();

    store.selectedTriggerId.set('stateBinding');
    fixture.detectChanges();

    expect(store.selectedTriggerId()).toBe('stateBinding');
  });

  it('hides the run button while the aux tab is selected', () => {
    fixture.componentRef.setInput('auxTabId', 'stateBinding');
    fixture.detectChanges();
    expect(component.showRun()).toBeTrue();

    store.selectedTriggerId.set('stateBinding');
    fixture.detectChanges();

    expect(component.showRun()).toBeFalse();
  });

  it('hides the run button where the caller opted out', () => {
    fixture.componentRef.setInput('allowRun', false);
    fixture.detectChanges();

    expect(component.showRun()).toBeFalse();
  });

  it('resets the selection to the first tab when the active tab disappears', async () => {
    fixture.componentRef.setInput('auxTabId', 'stateBinding');
    fixture.detectChanges();
    store.selectedTriggerId.set('stateBinding');
    fixture.detectChanges();
    expect(component.isAuxSelected()).toBeTrue();

    // Removing the aux tab (e.g. leaving toggle mode) must not strand the selection on it.
    fixture.componentRef.setInput('auxTabId', undefined);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(store.selectedTriggerId()).toBe('onShortPress');
    expect(component.isAuxSelected()).toBeFalse();
  });
});

describe('ActionBuilderComponent runFlow', () => {
  let fixture: ComponentFixture<ActionBuilderComponent>;
  let component: ActionBuilderComponent;
  let store: ActionFlowStore;
  let runActionFlow: jasmine.Spy;
  let toasts: ToastService;

  function makeBlock(id: string): ActionBlock {
    return { id, type: 'action', blockType: 'system.run', label: 'Run', color: '#000' };
  }

  beforeEach(async () => {
    // LocalizationService caches the active culture to localStorage, which - unlike TestBed - is not
    // reset between spec files sharing this Chrome instance. A test elsewhere that switches culture
    // would otherwise leak into these translated-text assertions.
    localStorage.clear();
    runActionFlow = jasmine.createSpy('runActionFlow');

    await TestBed.configureTestingModule({
      imports: [ActionBuilderComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: fakeApiService({ runActionFlow, clientId: 'client-1' }) },
      ],
    })
      .overrideComponent(ActionBuilderComponent, { set: { template: '', imports: [] } })
      .compileComponents();

    fixture = TestBed.createComponent(ActionBuilderComponent);
    component = fixture.componentInstance;
    store = fixture.debugElement.injector.get(ActionFlowStore);
    toasts = TestBed.inject(ToastService);
    fixture.detectChanges();

    store.selectedTriggerId.set('onShortPress');
    store.flows.set([{ triggerId: 'flow-1', triggerType: 'onShortPress', children: [makeBlock('block-1')] }]);
  });

  it('emits runCompleted and shows a success toast mentioning the duration', async () => {
    runActionFlow.and.resolveTo({ success: true, status: 'Succeeded', durationMs: 1234, executionId: 'e1', actions: [] });
    const emitted: void[] = [];
    component.runCompleted.subscribe(() => emitted.push(undefined));

    await component.runFlow();

    expect(emitted.length).toBe(1);
    expect(toasts.toasts()).toEqual([
      jasmine.objectContaining({ variant: 'success', message: jasmine.stringMatching(/1\.2s/) }),
    ]);
  });

  it('does not emit runCompleted when the run could not be started', async () => {
    runActionFlow.and.resolveTo({ success: false, status: 'Failed', durationMs: 0, error: { code: 'X', message: 'nope' } });
    const emitted: void[] = [];
    component.runCompleted.subscribe(() => emitted.push(undefined));

    await component.runFlow();

    expect(emitted.length).toBe(0);
    expect(toasts.toasts()).toEqual([jasmine.objectContaining({ variant: 'error', message: 'nope' })]);
  });

  it('shows an error toast with the cancellation message when the run is cancelled', async () => {
    runActionFlow.and.resolveTo({ success: false, status: 'Cancelled', durationMs: 10, error: { code: 'X', message: 'cut short' } });

    await component.runFlow();

    expect(toasts.toasts()).toEqual([jasmine.objectContaining({ variant: 'error', message: 'cut short' })]);
  });

  it('reports how many of how many steps failed on a partial failure, with the first failure as detail', async () => {
    runActionFlow.and.resolveTo({
      success: false,
      status: 'PartiallyFailed',
      durationMs: 20,
      actions: [
        { blockId: 'b1', status: 'Succeeded', durationMs: 5 },
        { blockId: 'b2', status: 'Failed', durationMs: 5, errorMessage: 'first failure' },
        { blockId: 'b3', status: 'Failed', durationMs: 5, errorMessage: 'second failure' },
      ],
    });
    const emitted: void[] = [];
    component.runCompleted.subscribe(() => emitted.push(undefined));

    await component.runFlow();

    expect(emitted.length).toBe(0);
    expect(toasts.toasts()).toEqual([
      jasmine.objectContaining({ variant: 'error', message: '2 actions failed out of 3', detail: 'first failure' }),
    ]);
  });

  // Issue #718: `ActionOutcome.errorMessage` is a LocalizedText on the wire, which can be an object
  // (`{ $localized: { scope, key } }`); the toast's `detail` must resolve it, not interpolate it raw.
  it('resolves a localized-object errorMessage for the partial-failure detail', async () => {
    runActionFlow.and.resolveTo({
      success: false,
      status: 'PartiallyFailed',
      durationMs: 20,
      actions: [
        { blockId: 'b1', status: 'Failed', durationMs: 5, errorMessage: { $localized: { scope: 'macrodeck', key: 'Common.Cancel' } } },
      ],
    });

    await component.runFlow();

    expect(toasts.toasts()).toEqual([
      jasmine.objectContaining({ variant: 'error', detail: 'Cancel' }),
    ]);
  });

  // Issue #718: same LocalizedText handling for a plain run-failure error.
  it('resolves a localized-object error for a run failure', async () => {
    runActionFlow.and.resolveTo({
      success: false, status: 'Failed', durationMs: 0,
      error: { code: 'X', message: { $localized: { scope: 'macrodeck', key: 'Common.Cancel' } } },
    });

    await component.runFlow();

    expect(toasts.toasts()).toEqual([jasmine.objectContaining({ variant: 'error', message: 'Cancel' })]);
  });

  it('does nothing when the run never started', async () => {
    store.selectedTriggerId.set('onLongPress');

    await component.runFlow();

    expect(runActionFlow).not.toHaveBeenCalled();
    expect(toasts.toasts()).toEqual([]);
  });
});

describe('ActionBuilderComponent run with unsaved changes', () => {
  let fixture: ComponentFixture<ActionBuilderComponent>;
  let component: ActionBuilderComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: fakeApiService() }],
    })
      .overrideComponent(ActionBuilderComponent, { set: { template: '', imports: [] } })
      .compileComponents();

    fixture = TestBed.createComponent(ActionBuilderComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('flows', [{
      triggerId: 'onShortPress', triggerType: 'onShortPress', triggerLabel: 'Short Press',
      children: [{ id: 'a', type: 'action', blockType: 'x', label: 'x', color: '' } as ActionBlock],
    }]);
    fixture.detectChanges();
  });

  it('allows a run while the draft matches what was saved', () => {
    fixture.componentRef.setInput('unsavedChanges', false);
    fixture.detectChanges();

    expect(component.canRun()).toBeTrue();
  });

  it('refuses a run while there are unsaved changes', () => {
    fixture.componentRef.setInput('unsavedChanges', true);
    fixture.detectChanges();

    expect(component.canRun()).toBeFalse();
  });

  it('says that saving comes first', () => {
    fixture.componentRef.setInput('unsavedChanges', true);
    fixture.detectChanges();

    expect(component.runTitle()).toContain('Save first');
  });
});

describe('ActionBuilderComponent widget variables', () => {
  let fixture: ComponentFixture<ActionBuilderComponent>;
  let component: ActionBuilderComponent;
  let store: ActionFlowStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: fakeApiService() }],
    })
      .overrideComponent(ActionBuilderComponent, { set: { template: '', imports: [] } })
      .compileComponents();

    fixture = TestBed.createComponent(ActionBuilderComponent);
    component = fixture.componentInstance;
    store = fixture.debugElement.injector.get(ActionFlowStore);
    fixture.detectChanges();
  });

  it('offers the variables of the widget whose flow is being edited', () => {
    store.previewScope.set('widget');
    store.previewScopeRefId.set('w1');

    expect(component.showVariables()).toBeTrue();
  });

  it('offers none for a flow that belongs to no widget', () => {
    store.previewScope.set('global');
    store.previewScopeRefId.set(undefined);

    expect(component.showVariables()).toBeFalse();
  });

  it('offers none for a widget scope with nothing to scope to', () => {
    // A script and an automation reach the builder this way; without a ref id there is no widget
    // whose variables these would be.
    store.previewScope.set('widget');
    store.previewScopeRefId.set(undefined);

    expect(component.showVariables()).toBeFalse();
  });
});

describe('ActionBuilderComponent parameter hydration', () => {
  let fixture: ComponentFixture<ActionBuilderComponent>;
  let store: ActionFlowStore;

  const definition: ActionBlockDefinition = {
    blockType: 'obs.set-scene',
    type: 'action',
    label: 'Set Scene',
    color: '#3b82f6',
    category: 'OBS',
    integrationId: 'obs',
    actionId: 'set-scene',
    parameters: [
      { name: 'sceneName', type: 'string', label: 'Scene Name', defaultValue: 'Default Scene' },
      { name: 'fadeMs', type: 'number', label: 'Fade Duration', defaultValue: 500 },
    ],
  };

  function migratedFlows(): ActionFlow[] {
    return [{
      triggerId: 'onShortPress',
      triggerType: 'onShortPress',
      children: [{
        id: 'b1',
        type: 'action',
        blockType: 'obs.set-scene',
        label: 'undefined',
        color: '#000',
        integrationId: 'obs',
        actionId: 'set-scene',
        parameters: [{ name: 'sceneName', type: 'string', label: '', value: 'Intro' } as never],
      }],
    }];
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: fakeApiService() }],
    })
      .overrideComponent(ActionBuilderComponent, { set: { template: '', imports: [] } })
      .compileComponents();

    fixture = TestBed.createComponent(ActionBuilderComponent);
    store = fixture.debugElement.injector.get(ActionFlowStore);
    fixture.detectChanges();
  });

  it('hydrates a migrated block once its definition is already available', () => {
    fixture.componentRef.setInput('availableBlocks', [definition]);
    fixture.componentRef.setInput('flows', migratedFlows());
    fixture.detectChanges();

    const params = store.flows()[0].children[0].parameters!;
    expect(params.map(p => p.name)).toEqual(['sceneName', 'fadeMs']);
    expect(params.find(p => p.name === 'sceneName')).toEqual(
      jasmine.objectContaining({ type: 'string', label: 'Scene Name', value: 'Intro' }),
    );
    expect(params.find(p => p.name === 'fadeMs')).toEqual(
      jasmine.objectContaining({ type: 'number', label: 'Fade Duration', value: 500 }),
    );
  });

  it('keeps a migrated block untouched while its definition is still unknown, then hydrates it once the definition arrives', () => {
    fixture.componentRef.setInput('flows', migratedFlows());
    fixture.detectChanges();

    const beforeParams = store.flows()[0].children[0].parameters!;
    expect(beforeParams).toEqual([{ name: 'sceneName', type: 'string', label: '', value: 'Intro' } as never]);

    fixture.componentRef.setInput('availableBlocks', [definition]);
    fixture.detectChanges();

    const afterParams = store.flows()[0].children[0].parameters!;
    expect(afterParams.map(p => p.name)).toEqual(['sceneName', 'fadeMs']);
    expect(afterParams.find(p => p.name === 'sceneName')?.label).toBe('Scene Name');
  });
});
