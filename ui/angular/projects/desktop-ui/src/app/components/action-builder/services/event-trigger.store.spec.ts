import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ActionBlockDefinition, ActionFlow, EVENT_TRIGGER_TYPE, createEmptyComparison } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ActionFlowStore } from './action-flow.store';

describe('ActionFlowStore event triggers', () => {
  let store: ActionFlowStore;
  let emitted: ActionFlow[][];

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
    emitted = [];
    store.connect(flows => emitted.push(flows));
  });

  const waitDefinition: ActionBlockDefinition = {
    blockType: 'wait',
    type: 'delay',
    label: 'Wait',
    color: '#000',
    category: 'Logic',
    parameters: [],
  };

  function addBound(eventId: string): string {
    const triggerId = store.addEventTrigger();
    store.updateEventBinding(triggerId, {
      providerId: 'obs',
      eventId,
      eventName: eventId,
      parameters: [{ name: 'sceneName', type: 'string', label: 'Scene', value: '' }],
    });
    return triggerId;
  }

  it('adds an unbound trigger and selects it', () => {
    const triggerId = store.addEventTrigger();

    expect(store.eventFlows().length).toBe(1);
    expect(store.eventFlows()[0].triggerType).toBe(EVENT_TRIGGER_TYPE);
    expect(store.eventFlows()[0].event).toBeUndefined();
    expect(store.selectedTriggerId()).toBe(triggerId);
    expect(store.selectedEventFlow()?.triggerId).toBe(triggerId);
  });

  it('keeps several event triggers apart', () => {
    const first = addBound('scene-changed');
    const second = addBound('recording-started');

    expect(store.eventFlows().length).toBe(2);
    expect(store.eventFlows().map(f => f.triggerId)).toEqual([first, second]);
    expect(store.rootListId()).toBe(`flow:${second}`);
  });

  function sceneValue(triggerId: string): string {
    const flow = store.flows().find(f => f.triggerId === triggerId);
    const param = flow?.event?.parameters?.find(p => p.name === 'sceneName');
    return String(param?.value ?? '');
  }

  it('writes a configuration value to the right trigger only', () => {
    const first = addBound('scene-changed');
    const second = addBound('recording-started');

    store.updateEventParam(first, 'sceneName', 'Live', 'Live');

    expect(sceneValue(first)).toBe('Live');
    expect(sceneValue(second)).toBe('');
  });

  it('replaces the configuration when the bound event changes', () => {
    const triggerId = addBound('scene-changed');
    store.updateEventParam(triggerId, 'sceneName', 'Live');

    store.updateEventBinding(triggerId, {
      providerId: 'obs',
      eventId: 'recording-started',
      parameters: [],
    });

    expect(store.flows()[0].event!.eventId).toBe('recording-started');
    expect(store.flows()[0].event!.parameters).toEqual([]);
  });

  it('removes a trigger without touching the others', () => {
    const first = addBound('scene-changed');
    const second = addBound('recording-started');

    store.removeEventTrigger(first);

    expect(store.eventFlows().map(f => f.triggerId)).toEqual([second]);
  });

  it('adds and clears a filter expression', () => {
    const triggerId = addBound('scene-changed');

    store.updateEventFilter(triggerId, createEmptyComparison());
    expect(store.flows()[0].event!.filter).toBeDefined();

    store.updateEventFilter(triggerId, undefined);
    expect(store.flows()[0].event!.filter).toBeUndefined();
  });

  it('adds a picked action to the selected event flow', () => {
    const triggerId = addBound('scene-changed');
    store.requestAdd(store.rootListId());

    store.pickAction(waitDefinition);

    const flows = store.flows();
    expect(flows.length).toBe(1);
    expect(flows[0].triggerId).toBe(triggerId);
    expect(flows[0].children.length).toBe(1);
  });

  it('still materialises a press flow lazily on the first action', () => {
    store.selectedTriggerId.set('onShortPress');
    store.requestAdd(store.rootListId());

    store.pickAction(waitDefinition);

    expect(store.flows()[0].triggerType).toBe('onShortPress');
    expect(store.flows()[0].children.length).toBe(1);
  });

  it('emits every mutation to the consumer', () => {
    addBound('scene-changed');

    expect(emitted.length).toBeGreaterThan(0);
    expect(emitted[emitted.length - 1][0].event!.eventId).toBe('scene-changed');
  });

  function param(triggerId: string, name: string): { value: unknown; valueLabel?: string; operator?: string } | undefined {
    return store.flows().find(f => f.triggerId === triggerId)?.event?.parameters?.find(p => p.name === name);
  }

  describe('updateEventParamOperator', () => {
    it('writes the operator onto the right parameter of the right trigger, leaving value/valueLabel alone', () => {
      const first = addBound('scene-changed');
      const second = addBound('recording-started');
      store.updateEventParam(first, 'sceneName', 'Live', 'Live');

      store.updateEventParamOperator(first, 'sceneName', '>');

      expect(param(first, 'sceneName')).toEqual(jasmine.objectContaining({
        value: 'Live', valueLabel: 'Live', operator: '>',
      }));
      expect(param(second, 'sceneName')).toEqual(jasmine.objectContaining({ value: '' }));
      expect(param(second, 'sceneName')?.operator).toBeUndefined();
    });

    it('removes the property entirely when the operator picked is ==', () => {
      const triggerId = addBound('scene-changed');
      store.updateEventParamOperator(triggerId, 'sceneName', '>');

      store.updateEventParamOperator(triggerId, 'sceneName', '==');

      const serialized = JSON.stringify(store.flows());
      expect(serialized).not.toContain('operator');
    });

    it('is a no-op for an unknown trigger id', () => {
      const triggerId = addBound('scene-changed');

      store.updateEventParamOperator('does-not-exist', 'sceneName', '>');

      expect(param(triggerId, 'sceneName')?.operator).toBeUndefined();
      expect(store.eventFlows().length).toBe(1);
    });

    it('appends the parameter when the stored flow does not carry it yet', () => {
      const triggerId = addBound('scene-changed');

      store.updateEventParamOperator(triggerId, 'newlyAddedFilter', '>');

      expect(param(triggerId, 'newlyAddedFilter')?.operator).toBe('>');
    });
  });

  describe('updateEventParam upsert', () => {
    it('appends the parameter when the stored flow does not carry it yet', () => {
      const triggerId = addBound('scene-changed');

      store.updateEventParam(triggerId, 'newlyAddedFilter', 'Live', 'Live');

      expect(param(triggerId, 'newlyAddedFilter')).toEqual(jasmine.objectContaining({
        value: 'Live', valueLabel: 'Live',
      }));
    });
  });
});
