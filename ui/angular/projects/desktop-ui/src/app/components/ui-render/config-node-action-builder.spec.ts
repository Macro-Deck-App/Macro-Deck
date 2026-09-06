import { By } from '@angular/platform-browser';
import { TestBed } from '@angular/core/testing';
import { EMPTY, Observable } from 'rxjs';
import { AppStrings, bundledTranslator, UiNode } from '@macro-deck/runtime';
import { ApiService, ConnectionState } from '@shared';
import { ActionBuilderComponent } from '../action-builder/action-builder.component';
import { ActionFlowStore } from '../action-builder/services/action-flow.store';
import { renderTree } from './ui-render-test-support';

const DEMO_ACTION = {
  id: 'ping',
  integrationId: 'com.example.demo',
  integrationName: 'Demo',
  name: 'Ping the demo device',
  description: '',
  parameters: [],
};

function fakeApi(): jasmine.SpyObj<ApiService> {
  const api = jasmine.createSpyObj<ApiService>('ApiService', ['getActions', 'getIntegrations', 'onNotification']);
  api.getActions.and.resolveTo({ actions: [DEMO_ACTION] });
  api.getIntegrations.and.resolveTo({ integrations: [] });
  api.onNotification.and.callFake(<T>(): Observable<T> => EMPTY);
  // `VariableService`'s auto-load effect calls this the way it calls the real signal.
  (api as unknown as { connectionStateSignal: () => ConnectionState }).connectionStateSignal =
    () => 'disconnected';
  return api;
}

function actionsListNode(properties: Record<string, unknown> = {}): UiNode {
  return { id: 'flows', type: 'actions-list-editor', properties: { value: [], ...properties } };
}

async function renderWithApi(root: UiNode, api: jasmine.SpyObj<ApiService> = fakeApi()) {
  const rendered = await renderTree(root, null, [{ provide: ApiService, useValue: api }]);
  // `ActionService.loadActions`/`IntegrationService.loadIntegrations` are fire-and-forget from the
  // wrapper's constructor - give their promises a turn to resolve and the resulting signal writes a
  // chance to flush before asserting on them.
  await Promise.resolve();
  await Promise.resolve();
  rendered.fixture.detectChanges();
  await rendered.fixture.whenStable();
  return rendered;
}

function builder(rendered: Awaited<ReturnType<typeof renderTree>>): ActionBuilderComponent {
  return rendered.fixture.debugElement.query(By.directive(ActionBuilderComponent)).componentInstance;
}

function storeFor(rendered: Awaited<ReturnType<typeof renderTree>>): ActionFlowStore {
  return rendered.fixture.debugElement.query(By.directive(ActionBuilderComponent)).injector.get(ActionFlowStore);
}

describe('shared-ui-node actions-list-editor', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders a tree of ordinary nodes with no extra providers - the wrapper isolation this pattern exists for', async () => {
    // No ApiService/VariableService/ActionService override: `shared-ui-input` itself must not need
    // one to construct for a node type that has nothing to do with actions or variables.
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        { id: 'flag', type: 'boolean', properties: { value: true } },
        { id: 'count', type: 'number', properties: { value: 3 } },
      ],
    };
    const rendered = await renderTree(root);

    expect(rendered.fixture.nativeElement.querySelector('.config-node-unsupported')).toBeNull();
    expect(rendered.fixture.nativeElement.querySelector('shared-toggle-switch')).not.toBeNull();
  });

  it('feeds the host action catalogue into the builder, not just an empty one', async () => {
    const rendered = await renderWithApi(actionsListNode());

    expect(storeFor(rendered).availableBlocks().some(block => block.blockType === 'com.example.demo.ping'))
      .toBeTrue();
  });

  it('emits the edited flow list as the node change event', async () => {
    const rendered = await renderWithApi(actionsListNode({ events: ['change'] }));
    const store = storeFor(rendered);

    // Drives a real store mutation (picking the fetched catalogue's own action into the default
    // trigger) rather than poking `flows` directly - `ActionFlowStore.emit` is private, reached only
    // through methods like this one, so this is also further proof the catalogue reached the store.
    const definition = store.availableBlocks().find(block => block.blockType === 'com.example.demo.ping')!;
    store.requestAdd(store.rootListId());
    store.pickAction(definition);
    rendered.fixture.detectChanges();
    await rendered.fixture.whenStable();

    expect(rendered.events.length).toBe(1);
    const event = rendered.events[0];
    expect(event.nodeId).toBe('flows');
    expect(event.name).toBe('change');
    const flows = event.data as { children: { blockType: string }[] }[];
    expect(flows.some(flow => flow.children.some(block => block.blockType === 'com.example.demo.ping')))
      .toBeTrue();
  });

  it('derives the toggle trigger tab from a "triggers" list that names it', async () => {
    const withToggle = await renderWithApi(
      actionsListNode({ triggers: ['onShortPress', 'onLongPress', 'onStateChange'] }),
    );
    expect(builder(withToggle).effectiveTriggerTabs().map(t => t.triggerType)).toContain('onStateChange');

    const withoutToggle = await renderWithApi(actionsListNode({ triggers: ['onShortPress', 'onLongPress'] }));
    expect(builder(withoutToggle).effectiveTriggerTabs().map(t => t.triggerType)).not.toContain('onStateChange');
  });

  it('forwards canRun to allowRun, gating the Run affordance', async () => {
    const withRun = await renderWithApi(actionsListNode({ canRun: true }));
    expect(builder(withRun).showRun()).toBeTrue();

    const withoutRun = await renderWithApi(actionsListNode({ canRun: false }));
    expect(builder(withoutRun).showRun()).toBeFalse();
  });

  it('forwards the states the node carries as the draft state list a state picker merges in', async () => {
    const rendered = await renderWithApi(actionsListNode({
      states: [{ value: 'off', label: 'Off' }, { value: 'recording', label: 'Recording' }],
    }));

    expect(storeFor(rendered).previewScopeStates())
      .toEqual([{ id: 'off', label: 'Off' }, { id: 'recording', label: 'Recording' }]);
  });

  it('leaves the draft state list absent when the node carries no states, so the host answers alone', async () => {
    const rendered = await renderWithApi(actionsListNode());

    expect(storeFor(rendered).previewScopeStates()).toBeUndefined();
  });

  it('carries the widget scope, its id and unsaved-changes state the tree was given down to the builder', async () => {
    const rendered = await renderTree(actionsListNode(), null, [{ provide: ApiService, useValue: fakeApi() }]);
    rendered.fixture.componentRef.setInput('scopeRefId', 'widget-1');
    rendered.fixture.componentRef.setInput('unsavedChanges', true);
    rendered.fixture.detectChanges();
    await rendered.fixture.whenStable();

    const store = storeFor(rendered);
    expect(store.previewScope()).toBe('widget');
    expect(store.previewScopeRefId()).toBe('widget-1');
    // Unsaved changes refuse a Run regardless of what the node's own `canRun` would otherwise allow -
    // `runTitle` says so before it even looks at whether a flow is selected.
    expect(builder(rendered).runTitle()).toBe(bundledTranslator(AppStrings.ActionBuilder.Toolbar.SaveFirstTooltip));
  });
});
