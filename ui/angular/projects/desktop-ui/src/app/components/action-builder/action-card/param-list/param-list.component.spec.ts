import { CUSTOM_ELEMENTS_SCHEMA, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ActionBlock } from '@macro-deck/runtime';
import { HOST_URL_RESOLVER } from '@shared';
import type { UiNode } from '@macro-deck/runtime';
import type { UiSessionHandle, UiSessionRejection } from '@shared';
import { ActionOptionsService } from '../../../../services/action-options.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ParamListComponent } from './param-list.component';

describe('ParamListComponent state row', () => {
  let fixture: ComponentFixture<ParamListComponent>;
  let options: jasmine.SpyObj<ActionOptionsService>;
  let ownerId: string | undefined;
  let draftHasOnOffStates: boolean | undefined;
  let providesButtonState: boolean;

  const ALL_STATES = [
    { value: 'current', label: 'Current state' },
    { value: 'on', label: 'On state' },
    { value: 'off', label: 'Off state' },
    { value: 'both', label: 'Both states' },
  ];
  const SINGLE_STATE = [{ value: 'current', label: 'Current state' }];

  function block(overrides: Partial<ActionBlock> = {}): ActionBlock {
    return {
      id: 'action-1',
      type: 'action',
      blockType: 'app.macro-deck.widget.set-label',
      label: 'Set Label',
      color: '',
      integrationId: 'app.macro-deck.widget',
      actionId: 'set-label',
      parameters: [
        { name: 'widget', type: 'widget-target', label: 'Widget', value: 'a-clock' },
        { name: 'state', type: 'dynamic-choice', label: 'State', value: 'current' },
        { name: 'label', type: 'string', label: 'Label', value: '' },
      ],
      ...overrides,
    };
  }

  async function render(input: ActionBlock): Promise<void> {
    fixture.componentRef.setInput('block', input);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function rowCount(): number {
    return fixture.nativeElement.querySelectorAll('shared-param-row').length;
  }

  function showsStateRow(): boolean {
    return rowCount() > 1;
  }

  beforeEach(async () => {
    ownerId = undefined;
    draftHasOnOffStates = undefined;
    providesButtonState = false;
    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);
    options.getOptions.and.resolveTo({ options: ALL_STATES, allowsCustomValue: false });

    await TestBed.configureTestingModule({
      imports: [ParamListComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionOptionsService, useValue: options },
        {
          provide: ActionFlowStore,
          useValue: {
            previewScopeRefId: () => ownerId,
            hasOwnerWidget: () => !!ownerId,
            previewScopeHasOnOffStates: () => draftHasOnOffStates,
            providesButtonState: () => providesButtonState,
          },
        },
      ],
    })
      .overrideComponent(ParamListComponent, { set: { imports: [], schemas: [CUSTOM_ELEMENTS_SCHEMA] } })
      .compileComponents();

    fixture = TestBed.createComponent(ParamListComponent);
  });

  it('hides the state row when the target has a single appearance', async () => {
    options.getOptions.and.resolveTo({ options: SINGLE_STATE, allowsCustomValue: false });

    await render(block());

    expect(showsStateRow()).toBeFalse();
  });

  it('shows the state row when the target has two appearances', async () => {
    await render(block());

    expect(showsStateRow()).toBeTrue();
  });

  it('shows the state row for a target that cannot be resolved', async () => {
    await render(block({
      parameters: [
        { name: 'widget', type: 'widget-target', label: 'Widget', value: '$self' },
        { name: 'state', type: 'dynamic-choice', label: 'State', value: 'current' },
        { name: 'label', type: 'string', label: 'Label', value: '' },
      ],
    }));

    expect(showsStateRow()).toBeTrue();
  });

  it('shows the state row when the lookup fails', async () => {
    options.getOptions.and.rejectWith(new Error('offline'));

    await render(block());

    expect(showsStateRow()).toBeTrue();
  });

  it('applies to every appearance action, not only Set Label', async () => {
    options.getOptions.and.resolveTo({ options: SINGLE_STATE, allowsCustomValue: false });

    await render(block({
      actionId: 'set-border',
      blockType: 'app.macro-deck.widget.set-border',
      parameters: [
        { name: 'widget', type: 'widget-target', label: 'Widget', value: 'a-clock' },
        { name: 'state', type: 'dynamic-choice', label: 'State', value: 'both' },
      ],
    }));

    expect(showsStateRow()).toBeFalse();
  });

  it('leaves another integration’s state parameter alone', async () => {
    options.getOptions.and.resolveTo({ options: SINGLE_STATE, allowsCustomValue: false });

    await render(block({
      integrationId: 'app.macro-deck.obs',
      actionId: 'set-scene',
      parameters: [
        { name: 'scene', type: 'widget-target', label: 'Scene', value: 'a-scene' },
        { name: 'state', type: 'dynamic-choice', label: 'State', value: 'current' },
      ],
    }));

    expect(showsStateRow()).toBeTrue();
    expect(options.getOptions).not.toHaveBeenCalled();
  });

  it('re-resolves when the selected target changes', async () => {
    await render(block());
    expect(showsStateRow()).toBeTrue();

    options.getOptions.and.resolveTo({ options: SINGLE_STATE, allowsCustomValue: false });
    await render(block({
      parameters: [
        { name: 'widget', type: 'widget-target', label: 'Widget', value: 'a-slider' },
        { name: 'state', type: 'dynamic-choice', label: 'State', value: 'current' },
        { name: 'label', type: 'string', label: 'Label', value: '' },
      ],
    }));

    expect(showsStateRow()).toBeFalse();
  });

  it('leaves the appearance fields to their own presenter', async () => {
    await render(block());

    expect(rowCount()).toBe(2);
  });

  it('never renders the provide-button-state field, which belongs to the card header', async () => {
    providesButtonState = true;
    await render(block());

    expect(fixture.nativeElement.querySelector('shared-provide-button-state-field')).toBeNull();
    expect(rowCount()).toBe(2);
  });

  describe('Set Button State', () => {
    function setStateBlock(): ActionBlock {
      return block({
        actionId: 'set-state',
        blockType: 'app.macro-deck.widget.set-state',
        label: 'Set Button State',
        parameters: [
          { name: 'widget', type: 'widget-target', label: 'Widget', value: 'a-button' },
          { name: 'state', type: 'dynamic-choice', label: 'State', value: '' },
        ],
      });
    }

    // Its id starts with `set-`, but it writes the active state, not an appearance field - the
    // appearance presenter's fallback would draw a Label editor that sets nothing.
    it('is not an appearance action, so no appearance field is drawn for it', async () => {
      await render(setStateBlock());

      expect(fixture.nativeElement.querySelector('shared-widget-appearance-action-fields')).toBeNull();
    });

    it('keeps its state field even when the target reports a single state', async () => {
      options.getOptions.and.resolveTo({ options: SINGLE_STATE, allowsCustomValue: false });

      await render(setStateBlock());

      expect(rowCount()).toBe(2);
    });
  });
  describe('the widget being edited', () => {
    beforeEach(() => {
      ownerId = 'a-clock';
      options.getOptions.and.resolveTo({ options: SINGLE_STATE, allowsCustomValue: false });
    });

    it('shows the row as soon as the draft switches to two states, without a save', async () => {
      draftHasOnOffStates = true;

      await render(block());

      expect(showsStateRow()).toBeTrue();
    });

    it('hides the row while the draft has one state, whatever the host last saw', async () => {
      draftHasOnOffStates = false;
      options.getOptions.and.resolveTo({ options: ALL_STATES, allowsCustomValue: false });

      await render(block());

      expect(showsStateRow()).toBeFalse();
    });

    it('leaves the answer to the host for a target that is not the edited widget', async () => {
      draftHasOnOffStates = true;
      ownerId = 'someone-else';

      await render(block());

      expect(showsStateRow()).toBeFalse();
    });
  });
});

describe('ParamListComponent conditional rows', () => {
  let fixture: ComponentFixture<ParamListComponent>;

  function httpBlock(authType: string, bodyType: string): ActionBlock {
    return {
      id: 'action-1',
      type: 'action',
      blockType: 'app.macro-deck.http.send-request',
      label: 'Send HTTP Request',
      color: '',
      integrationId: 'app.macro-deck.http',
      actionId: 'send-request',
      parameters: [
        { name: 'url', type: 'url', label: 'URL', value: 'https://example.invalid/' },
        { name: 'authType', type: 'choice', label: 'Authentication', value: authType },
        {
          name: 'authUsername',
          type: 'string',
          label: 'Username',
          value: '',
          visibleWhen: { parameterName: 'authType', values: ['basic'] },
        },
        {
          name: 'authHeaderName',
          type: 'string',
          label: 'Header name',
          value: '',
          visibleWhen: { parameterName: 'authType', values: ['header'] },
        },
        { name: 'bodyType', type: 'choice', label: 'Body type', value: bodyType },
        {
          name: 'jsonBody',
          type: 'json',
          label: 'JSON body',
          value: '',
          visibleWhen: { parameterName: 'bodyType', values: ['json'] },
        },
        {
          name: 'textBody',
          type: 'string',
          label: 'Text body',
          value: '',
          visibleWhen: { parameterName: 'bodyType', values: ['text'] },
        },
      ],
    };
  }

  async function renderedRows(input: ActionBlock): Promise<string[]> {
    fixture.componentRef.setInput('block', input);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return Array.from(
      fixture.nativeElement.querySelectorAll('shared-param-row') as NodeListOf<Element>,
    ).map(row => row.getAttribute('ng-reflect-param') ?? '');
  }

  async function rowCount(input: ActionBlock): Promise<number> {
    return (await renderedRows(input)).length;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ParamListComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ActionOptionsService,
          useValue: jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']),
        },
        {
          provide: ActionFlowStore,
          useValue: {
            previewScopeRefId: () => undefined,
            hasOwnerWidget: () => false,
            previewScopeHasOnOffStates: () => undefined,
            providesButtonState: () => false,
          },
        },
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
      ],
    })
      .overrideComponent(ParamListComponent, { set: { imports: [], schemas: [CUSTOM_ELEMENTS_SCHEMA] } })
      .compileComponents();

    fixture = TestBed.createComponent(ParamListComponent);
  });

  it('hides every mode-specific field when no mode is selected', async () => {
    expect(await rowCount(httpBlock('none', 'none'))).toBe(3);
  });

  it('shows only the selected authentication field', async () => {
    expect(await rowCount(httpBlock('basic', 'none'))).toBe(4);
    expect(await rowCount(httpBlock('header', 'none'))).toBe(4);
  });

  it('shows only the selected body editor', async () => {
    expect(await rowCount(httpBlock('none', 'json'))).toBe(4);
    expect(await rowCount(httpBlock('none', 'text'))).toBe(4);
  });

  it('combines the two independent conditions', async () => {
    expect(await rowCount(httpBlock('basic', 'json'))).toBe(5);
  });
});

describe('ParamListComponent config tree', () => {
  let fixture: ComponentFixture<ParamListComponent>;
  let options: jasmine.SpyObj<ActionOptionsService>;

  function block(overrides: Partial<ActionBlock> = {}): ActionBlock {
    return {
      id: 'action-1',
      type: 'action',
      blockType: 'app.macro-deck.example.do-thing',
      label: 'Do Thing',
      color: '',
      integrationId: 'app.macro-deck.example',
      actionId: 'do-thing',
      parameters: [],
      ...overrides,
    };
  }

  beforeEach(async () => {
    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);
    options.getOptions.and.resolveTo({ options: [], allowsCustomValue: false });

    await TestBed.configureTestingModule({
      imports: [ParamListComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionOptionsService, useValue: options },
        {
          provide: ActionFlowStore,
          useValue: {
            previewScope: () => 'widget',
            previewScopeRefId: () => undefined,
            hasOwnerWidget: () => false,
            previewScopeHasOnOffStates: () => undefined,
            providesButtonState: () => false,
            errorsFor: () => [],
            updateParam: jasmine.createSpy('updateParam'),
          },
        },
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ParamListComponent);
  });

  it('renders no tree and the usual rows when no root is supplied', async () => {
    fixture.componentRef.setInput('block', block({
      parameters: [{ name: 'widget', type: 'widget-target', label: 'Widget', value: 'a-clock' }],
    }));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('shared-ui-tree')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('shared-param-row').length).toBe(1);
  });

  it('renders a root-driven tree with zero legacy rows and zero option lookups', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [
        { id: 'widget', type: 'widget-target', properties: { label: 'Widget' } },
        { id: 'label', type: 'string', properties: { label: 'Label' } },
      ],
    };

    fixture.componentRef.setInput('block', block());
    fixture.componentRef.setInput('root', root);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('shared-param-row').length).toBe(0);
    expect(host.querySelector('[data-node-id="widget"]')).not.toBeNull();
    expect(host.querySelector('[data-node-id="label"]')).not.toBeNull();
    expect(options.getOptions).not.toHaveBeenCalled();
  });
});

describe('ParamListComponent node event forwarding', () => {
  let fixture: ComponentFixture<ParamListComponent>;
  let store: { updateParam: jasmine.Spy };

  function block(): ActionBlock {
    return {
      id: 'action-1',
      type: 'action',
      blockType: 'app.macro-deck.example.do-thing',
      label: 'Do Thing',
      color: '',
      integrationId: 'app.macro-deck.example',
      actionId: 'do-thing',
      parameters: [],
    };
  }

  function fakeSession(): { handle: UiSessionHandle; send: jasmine.Spy } {
    const send = jasmine.createSpy('send');
    const handle: UiSessionHandle = {
      root: signal<UiNode | null>(null),
      revision: signal(0),
      // Never rejected here: these tests are about a session that opened, and the parameter list
      // reads this only to tell "no tree yet" from "no provider".
      rejection: signal<UiSessionRejection | null>(null),
      send,
      close: () => {},
    };
    return { handle, send };
  }

  async function render(root: UiNode, session: UiSessionHandle): Promise<void> {
    fixture.componentRef.setInput('block', block());
    fixture.componentRef.setInput('root', root);
    fixture.componentRef.setInput('session', session);
    fixture.detectChanges();
    await fixture.whenStable();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    store = { updateParam: jasmine.createSpy('updateParam') };

    await TestBed.configureTestingModule({
      imports: [ParamListComponent],
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ActionOptionsService,
          useValue: jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']),
        },
        { provide: ActionFlowStore, useValue: store },
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ParamListComponent);
  });

  it("forwards a DynamicChoice node's reload event and does not touch the store", async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{ id: 'scene', type: 'dynamic-choice', properties: { label: 'Scene', events: ['reload'] } }],
    };
    const session = fakeSession();
    await render(root, session.handle);

    host().querySelector<HTMLElement>('[data-node-id="scene"] shared-button .icon-refresh')!
      .closest('button')!.click();
    await settle();

    expect(session.send).toHaveBeenCalledTimes(1);
    expect(session.send).toHaveBeenCalledWith({ nodeId: 'scene', name: 'reload' });
    expect(store.updateParam).not.toHaveBeenCalled();

    const updated: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{
        id: 'scene',
        type: 'dynamic-choice',
        properties: { label: 'Scene', events: ['reload'], options: [{ value: 'a' }, { value: 'b' }] },
      }],
    };
    fixture.componentRef.setInput('root', updated);
    await settle();

    host().querySelector<HTMLButtonElement>('[data-node-id="scene"] .control')!.click();
    await settle();
    expect(host().querySelectorAll('[data-node-id="scene"] .sel-option').length).toBe(2);
  });

  it('forwards open, filter, add and remove - the "special-case reload only" counterexample', async () => {
    const openRoot: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{ id: 'scene', type: 'choice', properties: { label: 'Scene', events: ['open'] } }],
    };
    let session = fakeSession();
    await render(openRoot, session.handle);
    host().querySelector<HTMLButtonElement>('[data-node-id="scene"] .control')!.click();
    await settle();
    expect(session.send).toHaveBeenCalledWith({ nodeId: 'scene', name: 'open' });

    const filterRoot: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{ id: 'scene', type: 'autocomplete', properties: { label: 'Scene', events: ['filter'] } }],
    };
    session = fakeSession();
    await render(filterRoot, session.handle);
    const filterInput = host().querySelector<HTMLInputElement>('[data-node-id="scene"] .cb-input')!;
    filterInput.value = 'sce';
    filterInput.dispatchEvent(new Event('input'));
    await settle();
    expect(session.send).toHaveBeenCalledWith({ nodeId: 'scene', name: 'filter', data: 'sce' });

    const arrayRoot: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{
        id: 'items',
        type: 'array',
        properties: { label: 'Items', events: ['add', 'remove'] },
        children: [{ id: 'items.0', type: 'string', properties: { value: 'a' } }],
      }],
    };
    session = fakeSession();
    await render(arrayRoot, session.handle);

    const addButton = host().querySelector<HTMLButtonElement>(
      '.config-node-array > shared-button[variant="secondary"] button');
    addButton!.click();
    await settle();
    expect(session.send).toHaveBeenCalledWith({ nodeId: 'items', name: 'add' });

    const removeButton = host().querySelector<HTMLButtonElement>(
      '.config-node-array-item shared-button[variant="ghost"] button');
    removeButton!.click();
    await settle();
    expect(session.send).toHaveBeenCalledWith({ nodeId: 'items', name: 'remove', data: 'items.0' });
  });

  it('sends change exactly once and still updates the store, with no spurious extra event', async () => {
    const root: UiNode = {
      id: 'root',
      type: 'stack',
      children: [{
        id: 'scene',
        type: 'choice',
        properties: { label: 'Scene', events: ['change', 'reload'], options: [{ value: 'a' }, { value: 'b' }] },
      }],
    };
    const session = fakeSession();
    await render(root, session.handle);

    host().querySelector<HTMLButtonElement>('[data-node-id="scene"] .control')!.click();
    await settle();
    const optionB = Array.from(host().querySelectorAll<HTMLButtonElement>('[data-node-id="scene"] .sel-option'))
      .find(o => o.textContent?.trim() === 'b');
    optionB!.click();
    await settle();

    expect(session.send).toHaveBeenCalledTimes(1);
    expect(session.send).toHaveBeenCalledWith({ nodeId: 'scene', name: 'change', data: 'b' });
    expect(store.updateParam).toHaveBeenCalledTimes(1);
    expect(store.updateParam).toHaveBeenCalledWith('action-1', 'scene', 'b');
  });
});
