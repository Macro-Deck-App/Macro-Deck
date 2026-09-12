import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ActionBlockDefinition, Device, UiNode, UiNodeEvent, Variable } from '@macro-deck/runtime';
import { ApiService, HOST_URL_RESOLVER, VariableService } from '@shared';
import { ActionBuilderComponent } from '../action-builder/action-builder.component';
import { ActionPickerComponent } from '../action-builder/action-picker/action-picker.component';
import { VariablePickerComponent } from '../variable-picker/variable-picker.component';
import { DeviceService } from '../../services/device.service';
import { Integration, IntegrationService } from '../../services/integration.service';
import { UiTreeComponent } from './ui-tree.component';
import { el, renderTree, tick, type RenderedTree } from './ui-render-test-support';

async function renderWithStubbedActionBuilder(root: UiNode): Promise<RenderedTree> {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [UiTreeComponent],
    providers: [provideZonelessChangeDetection(), { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' }],
  }).overrideComponent(ActionBuilderComponent, { set: { template: '', imports: [] } });

  const fixture = TestBed.createComponent(UiTreeComponent);
  const events: UiNodeEvent[] = [];
  fixture.componentInstance.nodeEvent.subscribe((event: UiNodeEvent) => events.push(event));
  fixture.componentRef.setInput('root', root);
  const rendered: RenderedTree = { fixture, events };
  await tick(rendered);
  return rendered;
}

function makeDevice(id: string, name: string): Device {
  return {
    id,
    name,
    nameIsCustom: true,
    clientType: 'native',
    formFactor: 'desktop',
    online: true,
    connectionCount: 1,
    hasActiveSession: true,
    lastSeenAt: '2024-01-01T00:00:00Z',
    createdAt: '2024-01-01T00:00:00Z',
  };
}

function makeIntegration(id: string, name: string, capabilities: string[] = []): Integration {
  return {
    id,
    name,
    version: '1.0.0',
    isInternal: false,
    enabled: true,
    actionCount: 0,
    variableCount: 0,
    supportsConfigFlow: false,
    allowsMultipleConfigurations: false,
    configuredEntryCount: 0,
    hasIcon: false,
    iconVersion: null,
    issueCount: 0,
    issueSeverity: null,
    isInitialized: true,
    variablesDependOnConfiguration: false,
    providedCapabilities: capabilities.map(kind => ({ kind, name: kind })),
  };
}

function makeVariable(partial: Partial<Variable> & Pick<Variable, 'name' | 'type'>): Variable {
  return {
    id: partial.name,
    scope: 'global',
    classification: 'user',
    value: '',
    canWrite: true,
    ...partial,
  };
}

describe('shared-ui-node the five new configuration node types (issue #837)', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('device-picker', () => {
    it('lists devices from DeviceService, sorted by name, and emits the picked id as change', async () => {
      const devices = [makeDevice('d1', 'Zeta Deck'), makeDevice('d2', 'Alpha Deck')];
      const rendered = await renderTree(
        { id: 'n', type: 'device-picker', properties: { events: ['change'] } },
        null,
        [{ provide: DeviceService, useValue: { devices: signal(devices), load: () => Promise.resolve() } }],
      );
      const host = el(rendered);

      (host.querySelector('button.control') as HTMLButtonElement)?.click();
      await tick(rendered);

      const labels = Array.from(host.querySelectorAll('.sel-option')).map(o => o.textContent?.trim());
      expect(labels).toEqual(['Alpha Deck', 'Zeta Deck']);

      (host.querySelectorAll('.sel-option')[0] as HTMLElement).click();
      await tick(rendered);

      expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'd2' }]);
    });
  });

  describe('integration-picker', () => {
    it('honours capability, offering only integrations that declare it', async () => {
      const integrations = [
        makeIntegration('spotify', 'Spotify', ['media-player']),
        makeIntegration('obs', 'OBS', ['scene-switcher']),
      ];
      const rendered = await renderTree(
        { id: 'n', type: 'integration-picker', properties: { capability: 'media-player', events: ['change'] } },
        null,
        [
          {
            provide: IntegrationService,
            useValue: { integrations: signal(integrations), loadIntegrations: () => Promise.resolve() },
          },
        ],
      );
      const host = el(rendered);

      (host.querySelector('button.control') as HTMLButtonElement)?.click();
      await tick(rendered);

      const labels = Array.from(host.querySelectorAll('.sel-option')).map(o => o.textContent?.trim());
      expect(labels).toEqual(['Spotify']);

      (host.querySelector('.sel-option') as HTMLElement).click();
      await tick(rendered);

      expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'spotify' }]);
    });

    it('honours configurationEntries, picking one of the integration\'s configured connections instead of the integration itself', async () => {
      const integrations = [makeIntegration('obs', 'OBS')];
      const rendered = await renderTree(
        { id: 'n', type: 'integration-picker', properties: { configurationEntries: true, events: ['change'] } },
        null,
        [
          {
            provide: IntegrationService,
            useValue: { integrations: signal(integrations), loadIntegrations: () => Promise.resolve() },
          },
          {
            provide: ApiService,
            useValue: {
              getConfigEntries: () => Promise.resolve({
                entries: [{ id: 'entry-1', title: 'Studio Mic', createdAt: '2024-01-01T00:00:00Z' }],
              }),
              onNotification: () => new Subject(),
              connectionStateSignal: signal('disconnected'),
            },
          },
        ],
      );
      const host = el(rendered);
      await tick(rendered);

      (host.querySelector('button.control') as HTMLButtonElement)?.click();
      await tick(rendered);

      const labels = Array.from(host.querySelectorAll('.sel-option')).map(o => o.textContent?.trim());
      expect(labels).toEqual(['Studio Mic']);

      (host.querySelector('.sel-option') as HTMLElement).click();
      await tick(rendered);

      expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'entry-1' }]);
    });
  });

  describe('variable-picker', () => {
    it('honours writableOnly and variableTypes, and emits the picked variable\'s bare name', async () => {
      const variables = [
        makeVariable({ name: 'readOnlyText', type: 'text', classification: 'integration', canWrite: false }),
        makeVariable({ name: 'writableText', type: 'text' }),
        makeVariable({ name: 'writableNumber', type: 'numeric' }),
      ];
      const root: UiNode = {
        id: 'n',
        type: 'variable-picker',
        properties: { writableOnly: true, variableTypes: ['text'], events: ['change'] },
      };
      const rendered = await renderTree(root, null, [
        { provide: VariableService, useValue: { variables: signal(variables) } },
      ]);

      const picker = rendered.fixture.debugElement.query(By.directive(VariablePickerComponent))
        .componentInstance as VariablePickerComponent;

      // writableOnly is applied before handing the list to the picker; variableTypes is passed
      // through as `acceptedTypes` for the picker's own type filter.
      expect(picker.variables.map(v => v.name)).toEqual(['writableText', 'writableNumber']);
      expect(picker.acceptedTypes).toEqual(['text']);

      picker.pick.emit('writableText');
      await tick(rendered);

      expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'writableText' }]);
    });

    it('in a widget editor offers only globals and that widget\'s own variables; without a scope, everything', async () => {
      const own = makeVariable({ name: 'own', type: 'numeric', scope: 'widget', scopeRefId: 'widget-a' });
      const foreign = makeVariable({ name: 'foreign', type: 'numeric', scope: 'widget', scopeRefId: 'widget-b' });
      const global = makeVariable({ name: 'global', type: 'numeric' });
      const visibleForContext = jasmine.createSpy('visibleForContext').and.returnValue([own, global]);
      const root: UiNode = { id: 'n', type: 'variable-picker', properties: { events: ['change'] } };
      const rendered = await renderTree(root, null, [
        { provide: VariableService, useValue: { variables: signal([own, foreign, global]), visibleForContext } },
      ]);
      const pickerNames = (): string[] =>
        (rendered.fixture.debugElement.query(By.directive(VariablePickerComponent))
          .componentInstance as VariablePickerComponent).variables.map(v => v.name);

      expect(pickerNames()).toEqual(['own', 'foreign', 'global']);

      rendered.fixture.componentRef.setInput('scopeRefId', 'widget-a');
      await tick(rendered);

      expect(visibleForContext).toHaveBeenCalledWith('widget', 'widget-a');
      expect(pickerNames()).toEqual(['own', 'global']);
    });

    it('shows a chip once a variable is picked, and clearing it emits an empty string', async () => {
      const root: UiNode = { id: 'n', type: 'variable-picker', properties: { value: 'myVar', events: ['change'] } };
      const rendered = await renderTree(root);
      const host = el(rendered);

      expect(host.querySelector('shared-variable-picker')).toBeNull();
      expect(host.querySelector('.config-node-chip')?.textContent).toContain('myVar');

      (host.querySelector('.config-node-chip shared-button button') as HTMLButtonElement).click();
      await tick(rendered);

      expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: '' }]);
    });
  });

  describe('action-picker', () => {
    it('opens the picker, emits the picked action\'s qualified id, and then shows it as a chip', async () => {
      const root: UiNode = { id: 'n', type: 'action-picker', properties: { events: ['change'] } };
      const rendered = await renderTree(root);
      const host = el(rendered);

      expect(host.querySelector('.config-node-chip')).toBeNull();
      expect(host.querySelector('shared-action-picker')).toBeNull();

      (host.querySelector('shared-button button') as HTMLButtonElement).click();
      await tick(rendered);

      expect(host.querySelector('shared-action-picker')).not.toBeNull();

      const picker = rendered.fixture.debugElement.query(By.directive(ActionPickerComponent))
        .componentInstance as ActionPickerComponent;
      const def: ActionBlockDefinition = {
        blockType: 'test.block',
        type: 'action',
        label: 'Test block',
        color: '#123456',
        category: 'Test',
      };
      picker.picked.emit(def);
      await tick(rendered);

      expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: 'test.block' }]);
      expect(host.querySelector('shared-action-picker')).toBeNull();
      expect(host.querySelector('.config-node-chip')?.textContent).toContain('test.block');
    });
  });

  describe('actions-list-editor', () => {
    it('feeds the node\'s value in as flows and surfaces flowsChange as the node\'s own change event', async () => {
      const flows = [{ triggerId: 'flow1', triggerType: 'onShortPress', children: [] }];
      const root: UiNode = {
        id: 'n',
        type: 'actions-list-editor',
        properties: { value: flows, canRun: true, events: ['change'] },
      };
      const rendered = await renderWithStubbedActionBuilder(root);
      const host = el(rendered);

      expect(host.querySelector('shared-action-builder')).not.toBeNull();

      const builder = rendered.fixture.debugElement.query(By.directive(ActionBuilderComponent))
        .componentInstance as ActionBuilderComponent;
      expect(builder.flows).toEqual(flows);

      const newFlows = [
        { triggerId: 'flow1', triggerType: 'onShortPress', children: [] },
        { triggerId: 'flow2', triggerType: 'onLongPress', children: [] },
      ];
      builder.flowsChange.emit(newFlows);
      await tick(rendered);

      expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: newFlows }]);
    });
  });
});
