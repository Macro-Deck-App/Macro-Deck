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

describe('ActionBuilderComponent single-event mode', () => {
  let fixture: ComponentFixture<ActionBuilderComponent>;
  let component: ActionBuilderComponent;
  let store: ActionFlowStore;

  function eventFlow(triggerId: string, children: unknown[] = []): unknown {
    return {
      triggerId,
      triggerType: 'onEvent',
      event: { providerId: 'obs', eventId: 'scene-changed', eventName: 'Scene Changed' },
      children,
    };
  }

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

  function enterSingleEventMode(flows: unknown[] = [eventFlow('t1')]): void {
    fixture.componentRef.setInput('singleEventTrigger', true);
    fixture.componentRef.setInput('flows', flows);
    fixture.detectChanges();
  }

  it('drops the fixed trigger tabs entirely', () => {
    enterSingleEventMode();

    expect(component.triggerTabItems()).toEqual([]);
  });

  it('offers no way to add or pick between triggers', () => {
    enterSingleEventMode();

    expect(component.showAddEventTrigger()).toBeFalse();
  });

  it('offers no add affordance even when allowEventTriggers is on', () => {
    fixture.componentRef.setInput('allowEventTriggers', true);
    enterSingleEventMode();

    expect(component.showAddEventTrigger()).toBeFalse();
  });

  it('makes the trigger non-removable', () => {
    enterSingleEventMode();

    expect(component.eventTriggerRemovable()).toBeFalse();
  });

  it('leaves a widget trigger removable', () => {
    expect(component.eventTriggerRemovable()).toBeTrue();
  });

  it('overrides a caller-supplied tab set', () => {
    fixture.componentRef.setInput('triggerTabs', [{ triggerType: 'onRun', label: 'Actions' }]);
    enterSingleEventMode();

    expect(component.triggerTabItems()).toEqual([]);
  });

  it('selects the single trigger', async () => {
    enterSingleEventMode();
    await fixture.whenStable();

    expect(store.selectedTriggerId()).toBe('t1');
    expect(component.selectedEventFlow()?.triggerId).toBe('t1');
  });

  it('shows the trigger\'s actions', async () => {
    enterSingleEventMode([eventFlow('t1', [{ id: 'b1' }, { id: 'b2' }])]);
    await fixture.whenStable();

    expect(component.currentChildren().length).toBe(2);
  });

  it('drops the toolbar row entirely', () => {
    enterSingleEventMode();
    fixture.componentRef.setInput('allowRun', false);
    fixture.detectChanges();

    expect(component.showTabRow()).toBeFalse();
    expect(component.showRun()).toBeFalse();
  });

  it('reports whether the single flow has anything to run', async () => {
    enterSingleEventMode([eventFlow('t1')]);
    await fixture.whenStable();
    expect(component.canRun()).toBeFalse();

    fixture.componentRef.setInput('flows', [eventFlow('t1', [{ id: 'b1' }])]);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.canRun()).toBeTrue();
  });

  it('has nothing to run while every action is switched off', async () => {
    enterSingleEventMode([eventFlow('t1', [{ id: 'b1', disabled: true }])]);
    await fixture.whenStable();

    expect(component.canRun()).toBeFalse();
  });

  it('has no trigger to author before the caller\'s flow binds', () => {
    fixture.componentRef.setInput('singleEventTrigger', true);
    fixture.detectChanges();

    expect(component.awaitingTrigger()).toBeTrue();
  });

  it('has a trigger to author once the flow binds', () => {
    enterSingleEventMode();

    expect(component.awaitingTrigger()).toBeFalse();
  });

  it('never awaits a trigger for a widget', () => {
    expect(component.awaitingTrigger()).toBeFalse();
  });

  it('offers no add-trigger affordance and no per-tab menu (#480)', () => {
    enterSingleEventMode();

    expect(component.showAddTrigger()).toBeFalse();
    expect(component.triggerTabItems()).toEqual([]);
  });

  it('falls back to the widget tabs when one-event mode is switched off again', () => {
    enterSingleEventMode();
    fixture.componentRef.setInput('singleEventTrigger', false);
    fixture.detectChanges();

    expect(component.triggerTabItems().map(item => item.id)).toContain('onShortPress');
  });

  it('normalizes a $localized eventName instead of crashing on it (#757)', () => {
    const flow = eventFlow('t1') as { event: { eventName: unknown } };
    flow.event.eventName = { $localized: { scope: 'macrodeck.app', key: 'Obs.SceneChanged.Name' } };

    expect(() => enterSingleEventMode([flow])).not.toThrow();
    expect(typeof component.eventTabItems()[0].label).toBe('string');
  });
});
