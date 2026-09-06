import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import { ActionBuilderComponent } from './action-builder.component';
import { ActionFlowStore } from './services/action-flow.store';

function fakeApiService(): ApiService {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
  apiSpy.onNotification.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
  return apiSpy;
}

describe('ActionBuilderComponent configured trigger tabs', () => {
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

  it('shows only Short Press for a widget with no flows', () => {
    fixture.componentRef.setInput('flows', []);
    fixture.detectChanges();

    expect(component.triggerTabItems().map(t => t.id)).toEqual(['onShortPress']);
  });

  it('drops the stand-in Short Press tab once another trigger is configured', () => {
    fixture.componentRef.setInput('flows', [
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [] },
    ]);
    fixture.detectChanges();

    expect(component.triggerTabItems().map(t => t.id)).toEqual(['onLongPress']);
    expect(component.addableTriggerTabs().map(t => t.triggerType)).toContain('onShortPress');
  });

  it('shows configured triggers in canonical order, not in the order they were added', () => {
    fixture.componentRef.setInput('flows', [
      { triggerId: 'onTouchEnd', triggerType: 'onTouchEnd', children: [] },
      { triggerId: 'onShortPress', triggerType: 'onShortPress', children: [] },
    ]);
    fixture.detectChanges();

    expect(component.triggerTabItems().map(t => t.id)).toEqual(['onShortPress', 'onTouchEnd']);
  });

  it('still shows a tab for a configured trigger with no actions', () => {
    fixture.componentRef.setInput('flows', [
      { triggerId: 'onTouchEnd', triggerType: 'onTouchEnd', children: [] },
    ]);
    fixture.detectChanges();

    expect(component.triggerTabItems().map(t => t.id)).toContain('onTouchEnd');
  });

  it('matches a legacy trigger type regardless of casing', () => {
    fixture.componentRef.setInput('flows', [
      { triggerId: 'OnLongPress', triggerType: 'OnLongPress', children: [{ id: 'b1' }] },
    ]);
    fixture.detectChanges();

    const tab = component.triggerTabItems().find(t => t.id === 'onLongPress');
    expect(tab).toBeDefined();
    expect(tab!.badge).toBe(1);
  });

  it('selects and removes a legacy-cased trigger through its canonical tab id', () => {
    fixture.componentRef.setInput('flows', [
      { triggerId: 'OnLongPress', triggerType: 'OnLongPress', children: [{ id: 'b1' }] },
    ]);
    fixture.detectChanges();

    component.selectTab('onLongPress');
    fixture.detectChanges();
    expect(store.selectedFlow()?.triggerId).toBe('OnLongPress');
    expect(component.currentChildren().length).toBe(1);

    component.onTriggerTabRemove('onLongPress');
    fixture.detectChanges();
    expect(component.pendingTriggerRemoval()?.triggerType).toBe('onLongPress');

    component.confirmTriggerRemoval();
    fixture.detectChanges();
    expect(store.flows()).toEqual([]);
    expect(component.triggerTabItems().map(t => t.id)).toEqual(['onShortPress']);
  });

  it('keeps On State Change always visible, non-addable and non-removable in toggle mode', () => {
    fixture.componentRef.setInput('showToggleTriggers', true);
    fixture.detectChanges();

    const items = component.triggerTabItems();
    expect(items.some(t => t.id === 'onStateChange')).toBeTrue();
    expect(items.find(t => t.id === 'onStateChange')!.removable).toBeFalsy();
    expect(component.addableTriggerTabs().some(t => t.triggerType === 'onStateChange')).toBeFalse();
  });

  it('shrinks the add menu and hides it once every trigger is configured', () => {
    expect(component.showAddTrigger()).toBeTrue();
    expect(component.addableTriggerTabs().map(t => t.triggerType))
      .toEqual(['onLongPress', 'onTouchStart', 'onTouchEnd']);

    fixture.componentRef.setInput('flows', [
      { triggerId: 'onShortPress', triggerType: 'onShortPress', children: [] },
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [] },
      { triggerId: 'onTouchStart', triggerType: 'onTouchStart', children: [] },
      { triggerId: 'onTouchEnd', triggerType: 'onTouchEnd', children: [] },
    ]);
    fixture.detectChanges();

    expect(component.addableTriggerTabs()).toEqual([]);
    expect(component.showAddTrigger()).toBeFalse();
  });

  it('adding a trigger selects its new tab', () => {
    component.addTrigger('onLongPress');
    fixture.detectChanges();

    expect(store.selectedTriggerId()).toBe('onLongPress');
    expect(component.triggerTabItems().map(t => t.id)).toContain('onLongPress');
  });

  it('keeps the stand-in Short Press tab when a second trigger is added', () => {
    fixture.componentRef.setInput('flows', []);
    fixture.detectChanges();

    component.addTrigger('onLongPress');
    fixture.detectChanges();

    expect(component.triggerTabItems().map(t => t.id)).toEqual(['onShortPress', 'onLongPress']);
    expect(store.flows().map(f => f.triggerType)).toEqual(['onShortPress', 'onLongPress']);
  });

  it('marks a tab removable once it owns a flow, Short Press included', () => {
    fixture.componentRef.setInput('showToggleTriggers', true);
    fixture.componentRef.setInput('flows', [
      { triggerId: 'onShortPress', triggerType: 'onShortPress', children: [] },
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [] },
    ]);
    fixture.detectChanges();

    const items = component.triggerTabItems();
    expect(items.find(t => t.id === 'onShortPress')!.removable).toBeTrue();
    expect(items.find(t => t.id === 'onLongPress')!.removable).toBeTrue();
    expect(items.find(t => t.id === 'onStateChange')!.removable).toBeFalse();
  });

  it('leaves the stand-in Short Press tab unremovable, so the row can never empty out', () => {
    fixture.componentRef.setInput('flows', []);
    fixture.detectChanges();
    expect(component.triggerTabItems()[0].removable).toBeFalse();

    fixture.componentRef.setInput('flows', [
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [] },
    ]);
    fixture.detectChanges();
    component.onTriggerTabRemove('onLongPress');
    component.confirmTriggerRemoval();
    fixture.detectChanges();

    expect(component.triggerTabItems().map(t => t.id)).toEqual(['onShortPress']);
    expect(component.triggerTabItems()[0].removable).toBeFalse();
  });

  it('confirms before removing a trigger, even an empty one', () => {
    fixture.componentRef.setInput('flows', [
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [] },
    ]);
    fixture.detectChanges();

    component.onTriggerTabRemove('onLongPress');

    expect(component.pendingTriggerRemoval()?.triggerType).toBe('onLongPress');
    expect(store.flows().some(f => f.triggerType === 'onLongPress')).toBeTrue();
    expect(component.pendingTriggerRemovalMessage()).toBe('Remove the Long Press trigger?');

    component.confirmTriggerRemoval();
    expect(store.flows().some(f => f.triggerType === 'onLongPress')).toBeFalse();
  });

  it('asks for confirmation before removing a trigger that still has actions', () => {
    fixture.componentRef.setInput('flows', [
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [{ id: 'b1' }] },
    ]);
    fixture.detectChanges();

    component.onTriggerTabRemove('onLongPress');
    expect(component.pendingTriggerRemoval()?.triggerType).toBe('onLongPress');
    expect(store.flows().some(f => f.triggerType === 'onLongPress')).toBeTrue();

    component.confirmTriggerRemoval();

    expect(component.pendingTriggerRemoval()).toBeNull();
    expect(store.flows().some(f => f.triggerType === 'onLongPress')).toBeFalse();
  });

  it('cancelling the confirmation leaves the trigger untouched', () => {
    fixture.componentRef.setInput('flows', [
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [{ id: 'b1' }] },
    ]);
    fixture.detectChanges();

    component.onTriggerTabRemove('onLongPress');
    component.cancelTriggerRemoval();

    expect(component.pendingTriggerRemoval()).toBeNull();
    expect(store.flows().some(f => f.triggerType === 'onLongPress')).toBeTrue();
  });

  it('is a no-op for Short Press and On State Change even if asked to remove them', () => {
    fixture.componentRef.setInput('showToggleTriggers', true);
    fixture.detectChanges();

    component.onTriggerTabRemove('onShortPress');
    component.onTriggerTabRemove('onStateChange');

    expect(component.pendingTriggerRemoval()).toBeNull();
    expect(component.triggerTabItems().map(t => t.id)).toContain('onShortPress');
    expect(component.triggerTabItems().map(t => t.id)).toContain('onStateChange');
  });

  it('offers no add menu and no removable tabs under a [triggerTabs] override', () => {
    fixture.componentRef.setInput('triggerTabs', [{ triggerType: 'onRun', label: 'Actions' }]);
    fixture.detectChanges();

    expect(component.showAddTrigger()).toBeFalse();
    expect(component.triggerTabItems().every(t => !t.removable)).toBeTrue();
  });

  it('offers no add menu and no removable tabs in singleEventTrigger mode', () => {
    fixture.componentRef.setInput('singleEventTrigger', true);
    fixture.componentRef.setInput('flows', [
      { triggerId: 't1', triggerType: 'onEvent', event: { providerId: 'obs', eventId: 'x' }, children: [] },
    ]);
    fixture.detectChanges();

    expect(component.showAddTrigger()).toBeFalse();
    expect(component.triggerTabItems()).toEqual([]);
  });
});

describe('ActionBuilderComponent inline aux tab', () => {
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
    fixture.componentRef.setInput('auxTabId', 'inputs');
    fixture.componentRef.setInput('auxTabLabel', 'Inputs');
    fixture.detectChanges();
  });

  it('keeps the aux tab out of the trigger bar by default', () => {
    expect(component.mainTabItems().map(t => t.id)).not.toContain('inputs');
    expect(component.auxTabItems().map(t => t.id)).toEqual(['inputs']);
  });

  it('puts the aux tab after the triggers, with its count, when asked for inline', () => {
    fixture.componentRef.setInput('auxTabInline', true);
    fixture.componentRef.setInput('auxTabBadge', 2);
    fixture.detectChanges();

    const items = component.mainTabItems();
    expect(items[items.length - 1]).toEqual(
      jasmine.objectContaining({ id: 'inputs', label: 'Inputs', badge: 2 }),
    );
  });

  it('shows no count pill while nothing is declared', () => {
    fixture.componentRef.setInput('auxTabInline', true);
    fixture.componentRef.setInput('auxTabBadge', 0);
    fixture.detectChanges();

    expect(component.mainTabItems().find(t => t.id === 'inputs')?.badge).toBe(0);
  });
});
