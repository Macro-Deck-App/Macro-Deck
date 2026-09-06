import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ActionBlock, ActionFlow } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ActionClipboardService } from '../../../services/action-clipboard.service';
import { defaultActionDefs } from '../default-action-defs';
import { ActionFlowStore } from './action-flow.store';

const DEFAULT_ACTION_DEFS = defaultActionDefs(key => key);

function fakeApiService(extra: Record<string, unknown> = {}): ApiService {
  return {
    onNotification: () => new Subject(),
    connectionStateSignal: signal('disconnected'),
    ...extra,
  } as unknown as ApiService;
}

describe('ActionFlowStore.updateParam valueLabel', () => {
  let store: ActionFlowStore;

  const blockId = 'block-1';

  function flowWith(block: ActionBlock): ActionFlow {
    return { triggerId: 't', triggerType: 'onShortPress', children: [block] };
  }

  function currentParam() {
    return store.flows()[0].children[0].parameters!.find(p => p.name === 'folderId')!;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
    store.flows.set([
      flowWith({
        id: blockId,
        type: 'action',
        blockType: 'deck.change-folder',
        label: 'Change Folder',
        color: '#000',
        parameters: [{ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: '' }],
      }),
    ]);
  });

  it('stores the value and the picked label together', () => {
    store.updateParam(blockId, 'folderId', 'guid-123', 'Living Room');

    expect(currentParam().value as string).toBe('guid-123');
    expect(currentParam().valueLabel).toBe('Living Room');
  });

  it('clears a previously cached label when the value changes without one', () => {
    store.updateParam(blockId, 'folderId', 'guid-123', 'Living Room');
    store.updateParam(blockId, 'folderId', 'guid-456');

    expect(currentParam().value as string).toBe('guid-456');
    expect(currentParam().valueLabel).toBeUndefined();
  });

  it('keeps the cached label when the same value is written again without one', () => {
    store.flows.set([
      flowWith({
        id: blockId,
        type: 'action',
        blockType: 'deck.change-folder',
        label: 'Change Folder',
        color: '#000',
        parameters: [{ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', valueLabel: 'Living Room' }],
      }),
    ]);

    store.updateParam(blockId, 'folderId', 'guid-123');

    expect(currentParam().value as string).toBe('guid-123');
    expect(currentParam().valueLabel).toBe('Living Room');
  });

  it('an explicitly passed label still wins', () => {
    store.flows.set([
      flowWith({
        id: blockId,
        type: 'action',
        blockType: 'deck.change-folder',
        label: 'Change Folder',
        color: '#000',
        parameters: [{ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', valueLabel: 'Living Room' }],
      }),
    ]);

    store.updateParam(blockId, 'folderId', 'guid-123', 'Wohnzimmer');

    expect(currentParam().valueLabel).toBe('Wohnzimmer');
  });

  it('a reference value drops the label', () => {
    store.flows.set([
      flowWith({
        id: blockId,
        type: 'action',
        blockType: 'deck.change-folder',
        label: 'Change Folder',
        color: '#000',
        parameters: [{ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123', valueLabel: 'Living Room' }],
      }),
    ]);

    store.updateParam(blockId, 'folderId', { $var: 'target' });

    expect(currentParam().valueLabel).toBeUndefined();
    expect(currentParam().value as object).toEqual({ $var: 'target' });
  });

  it('updateEventParam obeys the same rule', () => {
    store.flows.set([
      {
        triggerId: 'evt-1',
        triggerType: 'onEvent',
        event: {
          providerId: 'p',
          eventId: 'e',
          parameters: [{ name: 'deviceId', type: 'dynamic-choice', label: 'Device', value: 'dev-1', valueLabel: 'Kitchen' }],
        },
        children: [],
      },
    ]);

    store.updateEventParam('evt-1', 'deviceId', 'dev-1');
    expect(store.flows()[0].event!.parameters!.find(p => p.name === 'deviceId')!.valueLabel).toBe('Kitchen');

    store.updateEventParam('evt-1', 'deviceId', 'dev-2');
    expect(store.flows()[0].event!.parameters!.find(p => p.name === 'deviceId')!.valueLabel).toBeUndefined();
  });
});

describe('ActionFlowStore.cacheParamLabel', () => {
  let store: ActionFlowStore;
  let emitted: ActionFlow[][];

  const blockId = 'block-1';

  function flowWith(...children: ActionBlock[]): ActionFlow {
    return { triggerId: 't', triggerType: 'onShortPress', children };
  }

  function paramOf(id: string, name: string) {
    return store.flows()[0].children
      .find(b => b.id === id)!.parameters!.find(p => p.name === name)!;
  }

  beforeEach(() => {
    emitted = [];
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
    store.connect(flows => emitted.push(flows));
  });

  it('records the label without reporting a change', () => {
    store.flows.set([
      flowWith({
        id: blockId,
        type: 'action',
        blockType: 'deck.change-folder',
        label: 'Change Folder',
        color: '#000',
        parameters: [{ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123' }],
      }),
    ]);

    store.cacheParamLabel(blockId, 'folderId', 'guid-123', 'Living Room');

    expect(paramOf(blockId, 'folderId').valueLabel).toBe('Living Room');
    expect(paramOf(blockId, 'folderId').value as string).toBe('guid-123');
    expect(emitted.length).toBe(0);
  });

  it('ordinary edits still report a change', () => {
    store.flows.set([
      flowWith({
        id: blockId,
        type: 'action',
        blockType: 'deck.change-folder',
        label: 'Change Folder',
        color: '#000',
        parameters: [{ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123' }],
      }),
    ]);

    store.updateParam(blockId, 'folderId', 'guid-456', 'Bedroom');

    expect(emitted.length).toBe(1);
  });

  it('only the named block\'s named parameter', () => {
    store.flows.set([
      flowWith(
        {
          id: 'block-1',
          type: 'action',
          blockType: 'deck.change-folder',
          label: 'Change Folder',
          color: '#000',
          parameters: [
            { name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123' },
            { name: 'sceneName', type: 'string', label: 'Scene', value: 'guid-123' },
          ],
        },
        {
          id: 'block-2',
          type: 'action',
          blockType: 'deck.change-folder',
          label: 'Change Folder',
          color: '#000',
          parameters: [{ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123' }],
        },
      ),
    ]);

    store.cacheParamLabel('block-1', 'folderId', 'guid-123', 'Living Room');

    expect(paramOf('block-1', 'folderId').valueLabel).toBe('Living Room');
    expect(paramOf('block-1', 'sceneName').valueLabel).toBeUndefined();
    expect(paramOf('block-2', 'folderId').valueLabel).toBeUndefined();
  });

  it('ignores a label for a value that has changed since', () => {
    store.flows.set([
      flowWith({
        id: blockId,
        type: 'action',
        blockType: 'deck.change-folder',
        label: 'Change Folder',
        color: '#000',
        parameters: [{ name: 'folderId', type: 'dynamic-choice', label: 'Folder', value: 'guid-123' }],
      }),
    ]);

    store.updateParam(blockId, 'folderId', 'guid-999');
    store.cacheParamLabel(blockId, 'folderId', 'guid-123', 'Living Room');

    expect(paramOf(blockId, 'folderId').value as string).toBe('guid-999');
    expect(paramOf(blockId, 'folderId').valueLabel).toBeUndefined();
    expect(emitted.length).toBe(1);
  });
});

describe('ActionFlowStore.toggleDisabled', () => {
  let store: ActionFlowStore;

  function makeBlock(id: string, children?: ActionBlock[]): ActionBlock {
    return { id, type: 'action', blockType: 'system.run', label: 'Run', color: '#000', children };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
  });

  function setFlow(...children: ActionBlock[]): void {
    store.flows.set([{ triggerId: 't', triggerType: 'onShortPress', children }]);
  }

  it('switches a block off and back on', () => {
    setFlow(makeBlock('block-1'));

    store.toggleDisabled('block-1');
    expect(store.flows()[0].children[0].disabled).toBeTrue();

    store.toggleDisabled('block-1');
    expect(store.flows()[0].children[0].disabled).toBeFalse();
  });

  it('reaches a nested block without touching its container', () => {
    setFlow(makeBlock('container', [makeBlock('nested')]));

    store.toggleDisabled('nested');

    expect(store.flows()[0].children[0].disabled).toBeFalsy();
    expect(store.flows()[0].children[0].children![0].disabled).toBeTrue();
  });

  it('leaves nested blocks alone when the container is switched off', () => {
    setFlow(makeBlock('container', [makeBlock('nested')]));

    store.toggleDisabled('container');

    expect(store.flows()[0].children[0].children![0].disabled).toBeFalsy();
  });
});

describe('ActionFlowStore.updateBlockComment', () => {
  let store: ActionFlowStore;
  let emitted: ActionFlow[][];

  function makeBlock(id: string, children?: ActionBlock[]): ActionBlock {
    return { id, type: 'action', blockType: 'system.run', label: 'Run', color: '#000', children };
  }

  beforeEach(() => {
    emitted = [];
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
    store.connect(flows => emitted.push(flows));
  });

  function setFlow(...children: ActionBlock[]): void {
    store.flows.set([{ triggerId: 't', triggerType: 'onShortPress', children }]);
  }

  it('sets a comment on a top-level block', () => {
    setFlow(makeBlock('block-1'));

    store.updateBlockComment('block-1', 'Retry three times before giving up');

    expect(store.flows()[0].children[0].comment).toBe('Retry three times before giving up');
  });

  it('reaches a nested block without disturbing its container', () => {
    setFlow(makeBlock('container', [makeBlock('nested')]));

    store.updateBlockComment('nested', 'Nested note');

    expect(store.flows()[0].children[0].comment).toBeUndefined();
    expect(store.flows()[0].children[0].children![0].comment).toBe('Nested note');
  });

  it('drops the key entirely for a whitespace-only value', () => {
    setFlow(makeBlock('block-1'));
    store.updateBlockComment('block-1', 'A note');

    store.updateBlockComment('block-1', '   ');

    const block = store.flows()[0].children[0];
    expect('comment' in block).toBeFalse();
  });

  it('leaves other fields untouched', () => {
    setFlow({ ...makeBlock('block-1'), disabled: true, label: 'Run' });

    store.updateBlockComment('block-1', 'A note');

    const block = store.flows()[0].children[0];
    expect(block.disabled).toBeTrue();
    expect(block.label).toBe('Run');
  });

  it('emits the change', () => {
    setFlow(makeBlock('block-1'));

    store.updateBlockComment('block-1', 'A note');

    expect(emitted.length).toBe(1);
  });
});

describe('ActionFlowStore run', () => {
  let store: ActionFlowStore;
  let runActionFlow: jasmine.Spy;

  function makeBlock(id: string, disabled?: boolean): ActionBlock {
    return { id, type: 'action', blockType: 'system.run', label: 'Run', color: '#000', disabled };
  }

  beforeEach(() => {
    runActionFlow = jasmine.createSpy('runActionFlow')
      .and.resolveTo({ success: true, status: 'Succeeded', durationMs: 1, executionId: 'exec-1', actions: [] });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService({ runActionFlow, clientId: 'client-1' }) },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
    store.selectedTriggerId.set('onShortPress');
  });

  function setFlow(...children: ActionBlock[]): void {
    store.flows.set([{ triggerId: 'flow-1', triggerType: 'onShortPress', children }]);
  }

  it('sends the flow as authored, selected by its trigger id', async () => {
    store.previewScope.set('widget');
    store.previewScopeRefId.set('widget-1');
    setFlow(makeBlock('block-1'));

    const response = await store.runSelectedFlow();

    expect(response?.status).toBe('Succeeded');
    expect(runActionFlow).toHaveBeenCalledOnceWith({
      flows: JSON.stringify(store.flows()),
      triggerId: 'flow-1',
      scope: 'widget',
      scopeRefId: 'widget-1',
      clientId: 'client-1',
    });
  });

  it('returns the host response as-is, including a failure', async () => {
    runActionFlow.and.resolveTo({ success: false, status: 'Failed', durationMs: 0, error: { code: 'X', message: 'nope' } });
    setFlow(makeBlock('block-1'));

    const response = await store.runSelectedFlow();

    expect(response?.status).toBe('Failed');
    expect(response?.error?.message).toBe('nope');
  });

  it('reports an unreachable host instead of throwing', async () => {
    runActionFlow.and.rejectWith(new Error('offline'));
    setFlow(makeBlock('block-1'));

    const response = await store.runSelectedFlow();

    expect(response?.success).toBeFalse();
    expect(response?.error?.message).toBe('The actions could not be run');
    expect(store.running()).toBeFalse();
  });

  it('cannot run a flow whose every block is switched off', () => {
    setFlow(makeBlock('block-1', true));

    expect(store.canRun()).toBeFalse();
  });

  it('can run once one block is enabled', () => {
    setFlow(makeBlock('block-1', true), makeBlock('block-2'));

    expect(store.canRun()).toBeTrue();
  });

  it('cannot run a trigger that has no flow yet', () => {
    store.selectedTriggerId.set('onLongPress');
    setFlow(makeBlock('block-1'));

    expect(store.canRun()).toBeFalse();
  });
});

describe('ActionFlowStore.duplicateBlock', () => {
  let store: ActionFlowStore;
  let emitted: ActionFlow[][];
  let cloneSecret: jasmine.Spy;

  function makeBlock(overrides: Partial<ActionBlock> = {}): ActionBlock {
    return {
      id: 'block-1',
      type: 'action',
      blockType: 'system.kill-process',
      label: 'Kill Application',
      color: '#000',
      parameters: [{ name: 'processName', type: 'string', label: 'Process', value: 'SteelSeriesGG' }],
      ...overrides,
    };
  }

  beforeEach(() => {
    cloneSecret = jasmine.createSpy('cloneSecret');
    emitted = [];

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService({ cloneSecret }) },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
    store.connect(flows => emitted.push(flows));
  });

  function setFlow(...children: ActionBlock[]): void {
    store.flows.set([{ triggerId: 't', triggerType: 'onShortPress', children }]);
  }

  it('inserts an independent copy with a fresh id directly after the original', async () => {
    setFlow(makeBlock(), makeBlock({ id: 'block-2', label: 'Other' }));

    await store.duplicateBlock('block-1');

    const children = store.flows()[0].children;
    expect(children.length).toBe(3);
    expect(children[1].label).toBe('Kill Application');
    expect(children[1].id).not.toBe('block-1');
    expect(children[1].parameters![0].value as string).toBe('SteelSeriesGG');
    expect(children[2].id).toBe('block-2');
    expect(emitted.length).toBe(1);
  });

  it('marks the copy expanded so it can be edited right away', async () => {
    setFlow(makeBlock());

    await store.duplicateBlock('block-1');

    expect(store.isExpanded(store.flows()[0].children[1].id)).toBeTrue();
  });

  it('re-keys secret references through the host so the copy owns its secrets', async () => {
    cloneSecret.and.resolveTo({ id: 'secret-copy' });
    setFlow(makeBlock({
      parameters: [{ name: 'token', type: 'secret', label: 'Token', value: { $secret: 'secret-1' } }],
    }));

    await store.duplicateBlock('block-1');

    expect(cloneSecret).toHaveBeenCalledOnceWith('secret-1');
    const copy = store.flows()[0].children[1];
    expect(copy.parameters![0].value as object).toEqual({ $secret: 'secret-copy' });
  });

  it('drops a secret reference the host could not clone instead of sharing it', async () => {
    cloneSecret.and.rejectWith(new Error('host unreachable'));
    setFlow(makeBlock({
      parameters: [{ name: 'token', type: 'secret', label: 'Token', value: { $secret: 'secret-1' } }],
    }));

    await store.duplicateBlock('block-1');

    const copy = store.flows()[0].children[1];
    expect(copy.parameters![0].value).toBeNull();
  });

  it('does nothing for an unknown block id', async () => {
    setFlow(makeBlock());

    await store.duplicateBlock('missing');

    expect(store.flows()[0].children.length).toBe(1);
    expect(emitted.length).toBe(0);
  });

  it('keeps the comment on the copy', async () => {
    setFlow(makeBlock({ comment: 'Do not remove' }));

    await store.duplicateBlock('block-1');

    expect(store.flows()[0].children[1].comment).toBe('Do not remove');
  });
});

describe('ActionFlowStore.addTrigger / removeTrigger', () => {
  let store: ActionFlowStore;
  let emitted: ActionFlow[][];

  beforeEach(() => {
    emitted = [];
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
    store.connect(flows => emitted.push(flows));
  });

  it('adds an empty flow for the trigger and selects it', () => {
    store.addTrigger('onLongPress');

    expect(store.flows()).toEqual([
      { triggerId: 'onLongPress', triggerType: 'onLongPress', triggerLabel: 'onLongPress', children: [] },
    ]);
    expect(store.selectedTriggerId()).toBe('onLongPress');
    expect(emitted.length).toBe(1);
  });

  it('uses the connected label lookup for the new flow', () => {
    store.connect(
      flows => emitted.push(flows),
      triggerType => (triggerType === 'onLongPress' ? 'Long Press' : undefined),
    );

    store.addTrigger('onLongPress');

    expect(store.flows()[0].triggerLabel).toBe('Long Press');
  });

  it('matches a differently-cased persisted trigger type, as the host does', () => {
    store.flows.set([
      { triggerId: 'OnLongPress', triggerType: 'OnLongPress', children: [{ id: 'b1' } as never] },
    ]);
    emitted = [];

    store.addTrigger('onLongPress');
    expect(store.flows().length).toBe(1);

    store.removeTrigger('onLongPress');
    expect(store.flows()).toEqual([]);
  });

  it('is idempotent: a second call just re-selects instead of creating a second flow', () => {
    store.addTrigger('onLongPress');
    store.selectedTriggerId.set('onShortPress');
    emitted = [];

    store.addTrigger('onLongPress');

    expect(store.flows().length).toBe(1);
    expect(store.selectedTriggerId()).toBe('onLongPress');
    expect(emitted.length).toBe(0);
  });

  it('reuses the flow addTrigger created when an action is picked into it', () => {
    store.addTrigger('onLongPress');

    store.requestAdd(store.rootListId());
    store.pickAction(DEFAULT_ACTION_DEFS[0]);

    const flows = store.flows().filter(f => f.triggerType === 'onLongPress');
    expect(flows.length).toBe(1);
    expect(flows[0].children.length).toBe(1);
  });

  it('removes the trigger and, when it was selected, falls back to Short Press', () => {
    store.addTrigger('onLongPress');

    store.removeTrigger('onLongPress');

    expect(store.flows()).toEqual([]);
    expect(store.selectedTriggerId()).toBe('onShortPress');
  });

  it('leaves the selection alone when the removed tab was not the selected one', () => {
    store.addTrigger('onLongPress');
    store.addTrigger('onTouchStart');
    store.selectedTriggerId.set('onTouchStart');
    emitted = [];

    store.removeTrigger('onLongPress');

    expect(store.selectedTriggerId()).toBe('onTouchStart');
    expect(store.flows().some(f => f.triggerType === 'onLongPress')).toBeFalse();
    expect(emitted.length).toBe(1);
  });

  it('leaves event flows and other fixed flows untouched', () => {
    store.flows.set([
      { triggerId: 'evt-1', triggerType: 'onEvent', children: [] },
      { triggerId: 'onShortPress', triggerType: 'onShortPress', children: [{ id: 'b1' } as never] },
    ]);
    store.addTrigger('onLongPress');

    store.removeTrigger('onLongPress');

    expect(store.flows().map(f => f.triggerId)).toEqual(['evt-1', 'onShortPress']);
  });

  it('is a no-op for a trigger that was never added', () => {
    store.removeTrigger('onLongPress');

    expect(emitted.length).toBe(0);
  });

  it('does not auto-prune: removing every block leaves the flow present with no children', () => {
    store.addTrigger('onLongPress');
    store.requestAdd(store.rootListId());
    store.pickAction(DEFAULT_ACTION_DEFS[0]);
    const blockId = store.flows().find(f => f.triggerType === 'onLongPress')!.children[0].id;

    store.removeBlock(blockId);

    const flow = store.flows().find(f => f.triggerType === 'onLongPress');
    expect(flow).toBeDefined();
    expect(flow!.children).toEqual([]);
  });
});

describe('ActionFlowStore.hasOwnerWidget', () => {
  let store: ActionFlowStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
  });

  it('is false by default', () => {
    expect(store.hasOwnerWidget()).toBeFalse();
  });

  it('is true once the script toggle is on', () => {
    store.scriptRunsOnWidget.set(true);

    expect(store.hasOwnerWidget()).toBeTrue();
  });

  it('is true for a widget flow regardless of the script toggle', () => {
    for (const scriptRunsOnWidget of [true, false]) {
      store.previewScope.set('widget');
      store.previewScopeRefId.set('w-1');
      store.scriptRunsOnWidget.set(scriptRunsOnWidget);

      expect(store.hasOwnerWidget())
        .withContext(`scriptRunsOnWidget=${scriptRunsOnWidget}`)
        .toBeTrue();
    }
  });

  // Acceptance scenario 12 (second half): turning the toggle off while a flow still targets `$self`
  // must surface the validation error, not silently rewrite the flow's stored target value.
  it('surfaces the validation error, without touching the flow, when the toggle is turned off under a $self target', () => {
    const flowWithSelfTarget: ActionFlow[] = [{
      triggerId: 'onRun',
      triggerType: 'onRun',
      children: [{
        id: 'block-1',
        type: 'action',
        blockType: 'app.macro-deck.widget.set-label',
        label: 'Set Label',
        color: '#000',
        parameters: [{ name: 'widget', type: 'widget-target', label: 'Widget', value: '$self', required: true }],
      }],
    }];
    store.flows.set(flowWithSelfTarget);
    store.scriptRunsOnWidget.set(true);
    expect(store.validation().valid).toBeTrue();

    store.scriptRunsOnWidget.set(false);

    expect(store.validation().valid).toBeFalse();
    expect(store.validation().errors.some(e => e.blockId === 'block-1')).toBeTrue();
    expect(store.flows()).toEqual(flowWithSelfTarget);
  });
});

describe('ActionFlowStore copy/cut/paste', () => {
  let store: ActionFlowStore;
  let clipboard: ActionClipboardService;
  let emitted: ActionFlow[][];
  let cloneSecret: jasmine.Spy;

  function makeBlock(overrides: Partial<ActionBlock> = {}): ActionBlock {
    return {
      id: 'block-1',
      type: 'action',
      blockType: 'system.kill-process',
      label: 'Kill Application',
      color: '#000',
      parameters: [{ name: 'processName', type: 'string', label: 'Process', value: 'Steam' }],
      ...overrides,
    };
  }

  function makeLoop(id: string, children: ActionBlock[]): ActionBlock {
    return { id, type: 'loop', blockType: 'flow.repeat', label: 'Repeat', color: '#000', children };
  }

  beforeEach(() => {
    cloneSecret = jasmine.createSpy('cloneSecret');
    emitted = [];

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService({ cloneSecret }) },
      ],
    });
    store = TestBed.inject(ActionFlowStore);
    clipboard = TestBed.inject(ActionClipboardService);
    clipboard.clear();
    store.connect(flows => emitted.push(flows));
    store.previewScope.set('widget');
    store.previewScopeRefId.set('widget-a');
    store.selectedTriggerId.set('onShortPress');
  });

  function setFlow(...children: ActionBlock[]): void {
    store.flows.set([{ triggerId: 'onShortPress', triggerType: 'onShortPress', children }]);
  }

  it('pastes an independent copy with fresh ids all the way down', async () => {
    setFlow(makeLoop('loop-1', [makeBlock({ id: 'inner-1' })]));
    store.copyBlock('loop-1');

    await store.pasteAfter('loop-1');

    const children = store.flows()[0].children;
    expect(children.length).toBe(2);
    expect(children[1].id).not.toBe('loop-1');
    expect(children[1].children![0].id).not.toBe('inner-1');
    expect(children[1].children![0].label).toBe('Kill Application');
  });

  it('pastes into a nested list', async () => {
    setFlow(makeBlock(), makeLoop('loop-1', []));
    store.copyBlock('block-1');

    await store.pasteIntoList('children:loop-1');

    const loop = store.flows()[0].children[1];
    expect(loop.children!.length).toBe(1);
    expect(loop.children![0].label).toBe('Kill Application');
    expect(loop.children![0].id).not.toBe('block-1');
  });

  it('pastes into an If branch', async () => {
    const branch = {
      id: 'if-1',
      type: 'condition' as const,
      blockType: 'flow.if',
      label: 'If',
      color: '#000',
      branches: [{ id: 'branch-1', kind: 'if' as const, children: [] }],
    };
    setFlow(makeBlock(), branch);
    store.copyBlock('block-1');

    await store.pasteIntoList('branch:if-1:branch-1');

    const children = store.flows()[0].children[1].branches![0].children;
    expect(children.length).toBe(1);
    expect(children[0].label).toBe('Kill Application');
  });

  it('does not insert anything for a list that does not exist', async () => {
    setFlow(makeBlock());
    store.copyBlock('block-1');

    await store.pasteIntoList('children:nowhere');

    expect(store.flows()[0].children.length).toBe(1);
  });

  it('keeps the copy reusable, so the same action can be pasted onto several targets', async () => {
    setFlow(makeBlock());
    store.copyBlock('block-1');

    await store.pasteAfter('block-1');
    await store.pasteAfter('block-1');

    const ids = store.flows()[0].children.map(c => c.id);
    expect(ids.length).toBe(3);
    expect(new Set(ids).size).toBe(3);
    expect(clipboard.hasContent()).toBeTrue();
  });

  it('gives every paste its own secrets', async () => {
    cloneSecret.and.returnValues(
      Promise.resolve({ id: 'secret-copy-1' }),
      Promise.resolve({ id: 'secret-copy-2' }),
    );
    setFlow(makeBlock({
      parameters: [{ name: 'token', type: 'secret', label: 'Token', value: { $secret: 'secret-1' } }],
    }));
    store.copyBlock('block-1');

    await store.pasteAfter('block-1');
    await store.pasteAfter('block-1');

    const secretIds = store.flows()[0].children
      .map(c => (c.parameters![0].value as { $secret: string }).$secret)
      .sort();
    expect(secretIds).toEqual(['secret-1', 'secret-copy-1', 'secret-copy-2']);
  });

  it('drops a secret the host could not clone rather than sharing the original', async () => {
    cloneSecret.and.rejectWith(new Error('host unreachable'));
    setFlow(makeBlock({
      parameters: [{ name: 'token', type: 'secret', label: 'Token', value: { $secret: 'secret-1' } }],
    }));
    store.copyBlock('block-1');

    await store.pasteAfter('block-1');

    expect(store.flows()[0].children[1].parameters![0].value).toBeNull();
  });

  it('materialises the selected trigger flow when pasting into an empty tab', async () => {
    setFlow(makeBlock());
    store.copyBlock('block-1');
    store.selectedTriggerId.set('onLongPress');

    await store.pasteIntoList(store.rootListId());

    const created = store.flows().find(f => f.triggerId === 'onLongPress');
    expect(created).toBeDefined();
    expect(created!.children.length).toBe(1);
    expect(created!.children[0].label).toBe('Kill Application');
  });

  it('removes the source right away when a cut is pasted into the same record', async () => {
    setFlow(makeBlock(), makeBlock({ id: 'block-2', label: 'Other' }));
    store.cutBlock('block-1');
    expect(store.isCutSource('block-1')).toBeTrue();

    await store.pasteAfter('block-2');

    const children = store.flows()[0].children;
    expect(children.map(c => c.label)).toEqual(['Other', 'Kill Application']);
    expect(children.some(c => c.id === 'block-1')).toBeFalse();
    expect(clipboard.entry()).toBeNull();
  });

  it('leaves the source of a cross-record cut in place and defers it to that record saving', async () => {
    setFlow(makeBlock());
    store.cutBlock('block-1');

    store.previewScopeRefId.set('widget-b');
    store.flows.set([{ triggerId: 'onShortPress', triggerType: 'onShortPress', children: [] }]);
    await store.pasteIntoList(store.rootListId());

    const pasted = store.flows()[0].children[0];
    expect(clipboard.entry()!.pendingPasteOwner).toEqual({ kind: 'widget', widgetId: 'widget-b' });
    expect(clipboard.entry()!.pendingPasteBlockId).toBe(pasted.id);
  });

  it('degrades a cut to a copy when nothing names the record it came from', () => {
    store.previewScopeRefId.set(undefined);
    setFlow(makeBlock());

    store.cutBlock('block-1');

    expect(clipboard.entry()!.isCut).toBeFalse();
    expect(store.isCutSource('block-1')).toBeFalse();
  });

  it('calls off a cut that has not been pasted anywhere', () => {
    setFlow(makeBlock());
    store.cutBlock('block-1');

    store.cancelCut();

    expect(clipboard.entry()).toBeNull();
    expect(store.isCutSource('block-1')).toBeFalse();
  });

  it('does not clone secrets for a paste that cannot land', async () => {
    setFlow(makeBlock({
      parameters: [{ name: 'token', type: 'secret', label: 'Token', value: { $secret: 'secret-1' } }],
    }));
    store.copyBlock('block-1');

    await store.pasteIntoList('children:nowhere');

    expect(cloneSecret).not.toHaveBeenCalled();
  });

  it('aborts a paste whose record changed while the secrets were being cloned', async () => {
    let releaseClone: (value: { id: string }) => void = () => undefined;
    cloneSecret.and.returnValue(new Promise<{ id: string }>(resolve => { releaseClone = resolve; }));
    setFlow(makeBlock({
      parameters: [{ name: 'token', type: 'secret', label: 'Token', value: { $secret: 'secret-1' } }],
    }));
    store.copyBlock('block-1');

    const paste = store.pasteAfter('block-1');
    store.previewScopeRefId.set('widget-b');
    releaseClone({ id: 'secret-copy' });
    await paste;

    expect(store.flows()[0].children.length).toBe(1);
  });

  it('does nothing when there is nothing to paste', async () => {
    setFlow(makeBlock());

    await store.pasteAfter('block-1');

    expect(store.flows()[0].children.length).toBe(1);
    expect(emitted.length).toBe(0);
  });
});
