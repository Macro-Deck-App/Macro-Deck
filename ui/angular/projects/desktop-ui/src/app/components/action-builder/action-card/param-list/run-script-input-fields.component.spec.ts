import { CUSTOM_ELEMENTS_SCHEMA, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ActionBlock, ActionBlockParameter, SCRIPT_INPUTS_METADATA_KEY, SCRIPT_RUNS_ON_WIDGET_METADATA_KEY, isScriptInputParam } from '@macro-deck/runtime';
import { ApiService, TranslatePipe } from '@shared';
import type { ActionFlow, ScriptInput } from '@macro-deck/runtime';
import { validateActionFlows } from '../../../../domain/action-flow-validation.util';
import { ActionOptionsService } from '../../../../services/action-options.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { RunScriptInputFieldsComponent } from './run-script-input-fields.component';

describe('RunScriptInputFieldsComponent', () => {
  let fixture: ComponentFixture<RunScriptInputFieldsComponent>;
  let options: jasmine.SpyObj<ActionOptionsService>;
  let written: ActionBlockParameter[] | null;

  const SCENE: ScriptInput = { name: 'scene', type: 'text', label: 'Scene', required: true };
  const VOLUME: ScriptInput = { name: 'volume', type: 'numeric' };

  function optionsFor(scripts: Record<string, ScriptInput[]>) {
    return {
      options: Object.entries(scripts).map(([id, inputs]) => ({
        value: id,
        label: id,
        metadata: { [SCRIPT_INPUTS_METADATA_KEY]: JSON.stringify(inputs) },
      })),
      allowsCustomValue: false,
    };
  }

  function block(parameters: ActionBlockParameter[]): ActionBlock {
    return {
      id: 'block-1',
      type: 'action',
      blockType: 'app.macro-deck.scripts.run-script',
      label: 'Run Script',
      color: '',
      integrationId: 'app.macro-deck.scripts',
      actionId: 'run-script',
      parameters,
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

  beforeEach(async () => {
    written = null;
    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);
    options.getOptions.and.resolveTo(optionsFor({ 's-1': [SCENE, VOLUME], 's-2': [] }));

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    await TestBed.configureTestingModule({
      imports: [RunScriptInputFieldsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionOptionsService, useValue: options },
        {
          provide: ActionFlowStore,
          useValue: {
            setBlockParameters: (_id: string, parameters: ActionBlockParameter[]) => {
              written = parameters;
            },
          },
        },
        { provide: ApiService, useValue: apiSpy },
      ],
    })
      .overrideComponent(RunScriptInputFieldsComponent, {
        set: { imports: [TranslatePipe], schemas: [CUSTOM_ELEMENTS_SCHEMA] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(RunScriptInputFieldsComponent);
  });

  it('asks for the script options unfiltered, so the selected script stays in the answer', async () => {
    await render(block([{ name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' }]));

    expect(options.getOptions).toHaveBeenCalledWith(jasmine.objectContaining({
      integrationId: 'app.macro-deck.scripts',
      actionId: 'run-script',
      parameterName: 'scriptId',
    }));
    expect(options.getOptions.calls.mostRecent().args[0].filter).toBeUndefined();
  });

  it('adds one parameter per declared input, named input:<name>', async () => {
    await render(block([{ name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' }]));

    expect(written?.map(parameter => parameter.name))
      .toEqual(['scriptId', 'input:scene', 'input:volume']);
  });

  it('renders a row for every declared input', async () => {
    const parameters: ActionBlockParameter[] = [
      { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' },
      { name: 'input:scene', type: 'string', label: 'Scene', value: 'Live', required: true },
      { name: 'input:volume', type: 'number', label: 'volume', value: 42, required: false },
    ];

    await render(block(parameters));

    expect(rowCount()).toBe(2);
  });

  it('leaves a block that is already in line alone, so opening a flow cannot mark it changed', async () => {
    await render(block([
      { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' },
      {
        name: 'input:scene',
        type: 'string',
        label: 'Scene',
        value: 'Live',
        required: true,
        acceptedVariableTypes: ['text'],
      },
      {
        name: 'input:volume',
        type: 'number',
        label: 'volume',
        value: '',
        required: false,
        acceptedVariableTypes: ['numeric'],
      },
    ]));

    expect(written).toBeNull();
  });

  it('prunes the previous script\'s inputs when another script is selected', async () => {
    await render(block([
      { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-2' },
      { name: 'input:scene', type: 'string', label: 'Scene', value: 'Live', required: true },
    ]));

    expect(written?.map(parameter => parameter.name)).toEqual(['scriptId']);
  });

  it('leaves a leftover input from a cleared script behind nothing that blocks Save changes', async () => {
    await render(block([
      { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-2' },
      { name: 'input:scene', type: 'string', label: 'Scene', value: '', required: true },
    ]));

    const flows: ActionFlow[] = [{
      triggerId: 'press',
      triggerType: 'press',
      children: [{ ...block(written ?? []) }],
    }] as ActionFlow[];
    expect(validateActionFlows(flows).valid).toBeTrue();
  });

  it('keeps a value the user already filled in when the declarations are re-read', async () => {
    await render(block([
      { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' },
      { name: 'input:scene', type: 'string', label: 'Scene', value: '{{ vars.next_scene }}' },
    ]));

    const scene = written?.find(parameter => parameter.name === 'input:scene');
    expect(scene?.value as string).toBe('{{ vars.next_scene }}');
  });

  it('keeps the values when the option lookup fails, rather than dropping them', async () => {
    options.getOptions.and.rejectWith(new Error('offline'));

    await render(block([
      { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' },
      { name: 'input:scene', type: 'string', label: 'Scene', value: 'Live' },
    ]));

    expect(written).toBeNull();
  });

  // The root cause of issue #806 - a lookup that *succeeds* but does not contain the selected script
  // must not be read as "this script declares nothing" - is covered by the "unresolved lookup (S4)"
  // describe block below.
});

describe('RunScriptInputFieldsComponent writes an edited value under its input: name', () => {
  let fixture: ComponentFixture<RunScriptInputFieldsComponent>;
  let updateParam: jasmine.Spy;

  function setUp(integrationId: string, actionId: string, blockType: string): void {
    updateParam = jasmine.createSpy('updateParam');
    const store = {
      updateParam,
      setBlockParameters: jasmine.createSpy('setBlockParameters'),
      hasOwnerWidget: () => false,
      errorsFor: () => [],
      pickerVariables: () => [],
      previewScope: () => 'global',
      previewScopeRefId: () => undefined,
    };

    const options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);
    options.getOptions.and.resolveTo({
      options: [{
        value: 'script-1',
        label: 'Script One',
        metadata: { [SCRIPT_INPUTS_METADATA_KEY]: JSON.stringify([{ name: 'scene', type: 'text', label: 'Scene' }]) },
      }],
      allowsCustomValue: false,
    });

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [RunScriptInputFieldsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionOptionsService, useValue: options },
        { provide: ActionFlowStore, useValue: store },
        { provide: ApiService, useValue: apiSpy },
      ],
    });

    fixture = TestBed.createComponent(RunScriptInputFieldsComponent);
    fixture.componentRef.setInput('block', {
      id: 'block-1',
      type: 'action',
      blockType,
      label: 'Run',
      color: '',
      integrationId,
      actionId,
      parameters: [
        { name: 'instance', type: 'string', label: 'Instance', value: 'inst-1' },
        { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 'script-1' },
        { name: 'input:scene', type: 'string', label: 'Scene', value: '' },
      ],
    } as ActionBlock);
  }

  async function typeIntoScene(text: string): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const editor = fixture.nativeElement.querySelector('.vti-editor') as HTMLElement;
    editor.textContent = text;
    editor.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('at the remote delegation call site (app.macro-deck.delegate)', async () => {
    setUp('app.macro-deck.delegate', 'run-remote-script', 'app.macro-deck.delegate.run-remote-script');

    await typeIntoScene('BRB');

    const args = updateParam.calls.mostRecent().args;
    expect(args[1]).toBe('input:scene');
    expect(args[2] as string).toBe('BRB');
    expect(updateParam.calls.allArgs().some(a => a[1] === 'scene')).toBeFalse();
  });

  it('at the direct Run Script call site (app.macro-deck.scripts), as the control case', async () => {
    setUp('app.macro-deck.scripts', 'run-script', 'app.macro-deck.scripts.run-script');

    await typeIntoScene('BRB');

    const args = updateParam.calls.mostRecent().args;
    expect(args[1]).toBe('input:scene');
    expect(args[2] as string).toBe('BRB');
    expect(updateParam.calls.allArgs().some(a => a[1] === 'scene')).toBeFalse();
  });
});

describe('RunScriptInputFieldsComponent unresolved lookup (S4)', () => {
  let fixture: ComponentFixture<RunScriptInputFieldsComponent>;
  let options: jasmine.SpyObj<ActionOptionsService>;
  let written: ActionBlockParameter[] | null;

  function remoteBlock(): ActionBlock {
    return {
      id: 'block-1',
      type: 'action',
      blockType: 'app.macro-deck.delegate.run-remote-script',
      label: 'Run Remote Script',
      color: '',
      integrationId: 'app.macro-deck.delegate',
      actionId: 'run-remote-script',
      parameters: [
        { name: 'instance', type: 'string', label: 'Instance', value: 'inst-1' },
        { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 'script-1' },
        { name: 'input:scene', type: 'string', label: 'Scene', value: 'BRB' },
        { name: 'input:volume', type: 'number', label: 'volume', value: 70 },
      ],
    };
  }

  function rowValues(): unknown[] {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-param-row') as NodeListOf<Element>)
      .map(row => (row as unknown as { param: ActionBlockParameter }).param?.value);
  }

  function rowCount(): number {
    return fixture.nativeElement.querySelectorAll('shared-param-row').length;
  }

  async function render(): Promise<void> {
    fixture.componentRef.setInput('block', remoteBlock());
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    written = null;
    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [RunScriptInputFieldsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionOptionsService, useValue: options },
        {
          provide: ActionFlowStore,
          useValue: {
            setBlockParameters: (_id: string, parameters: ActionBlockParameter[]) => {
              written = parameters;
            },
            hasOwnerWidget: () => false,
          },
        },
        { provide: ApiService, useValue: apiSpy },
      ],
    })
      .overrideComponent(RunScriptInputFieldsComponent, {
        set: { imports: [TranslatePipe], schemas: [CUSTOM_ELEMENTS_SCHEMA] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(RunScriptInputFieldsComponent);
  });

  it('S4.1a: an unreachable remote (error field, empty options) keeps both rows and their values, with no store write', async () => {
    options.getOptions.and.resolveTo({ options: [], allowsCustomValue: false, error: { code: 'NOT_CONNECTED', message: 'Not connected' } });

    await render();

    expect(rowCount()).toBe(2);
    expect(rowValues()).toEqual(['BRB', 70]);
    expect(written).toBeNull();
  });

  it('S4.1b: a plain empty answer keeps both rows and their values, with no store write', async () => {
    options.getOptions.and.resolveTo({ options: [], allowsCustomValue: false });

    await render();

    expect(rowCount()).toBe(2);
    expect(rowValues()).toEqual(['BRB', 70]);
    expect(written).toBeNull();
  });

  it('S4.1c: a non-empty answer that does not contain the selected script id keeps both rows and their values, with no store write', async () => {
    options.getOptions.and.resolveTo({
      options: [{ value: 'other-script', label: 'Other', metadata: { [SCRIPT_INPUTS_METADATA_KEY]: '[]' } }],
      allowsCustomValue: false,
    });

    await render();

    expect(rowCount()).toBe(2);
    expect(rowValues()).toEqual(['BRB', 70]);
    expect(written).toBeNull();
  });

  it('S4.2: pruning still happens once the script is confidently found - the "never prune" counterexample', async () => {
    options.getOptions.and.resolveTo({
      options: [{
        value: 'script-1',
        label: 'Script One',
        metadata: { [SCRIPT_INPUTS_METADATA_KEY]: JSON.stringify([{ name: 'scene', type: 'text' }]) },
      }],
      allowsCustomValue: false,
    });

    await render();

    expect(rowCount()).toBe(1);
    expect(rowValues()).toEqual(['BRB']);
    expect(written?.some(p => p.name === 'input:volume')).toBeFalse();
    expect(written?.find(p => p.name === 'input:scene')?.value as unknown).toBe('BRB');
  });

  it('S4.3: a value survives a transient failure and is sent correctly on the recovery pass, not reset to a default', async () => {
    options.getOptions.and.resolveTo({ options: [], allowsCustomValue: false, error: { code: 'NOT_CONNECTED', message: 'Not connected' } });
    await render();
    expect(rowValues()).toEqual(['BRB', 70]);
    expect(written).toBeNull();

    options.getOptions.and.resolveTo({
      options: [{
        value: 'script-1',
        label: 'Script One',
        metadata: {
          [SCRIPT_INPUTS_METADATA_KEY]: JSON.stringify([{ name: 'scene', type: 'text' }, { name: 'volume', type: 'numeric' }]),
        },
      }],
      allowsCustomValue: false,
    });
    fixture.componentRef.setInput('block', remoteBlock());
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rowCount()).toBe(2);
    expect(rowValues()).toEqual(['BRB', 70]);
    // The recovery pass does write - it normalizes each row to the declared shape - so what matters
    // is that it carries the filled-in values across rather than the declarations' own defaults.
    const recovered = written?.filter(parameter => isScriptInputParam(parameter.name)) ?? [];
    expect(recovered.map(parameter => parameter.name)).toEqual(['input:scene', 'input:volume']);
    expect(recovered[0].value as string).toBe('BRB');
    expect(recovered[1].value as number).toBe(70);
  });
});

describe('RunScriptInputFieldsComponent widget target row', () => {
  let fixture: ComponentFixture<RunScriptInputFieldsComponent>;
  let options: jasmine.SpyObj<ActionOptionsService>;
  let written: ActionBlockParameter[] | null;

  function optionsFor(scripts: Record<string, { inputs?: ScriptInput[]; runsOnWidget?: boolean }>) {
    return {
      options: Object.entries(scripts).map(([id, script]) => ({
        value: id,
        label: id,
        metadata: {
          ...(script.inputs?.length ? { [SCRIPT_INPUTS_METADATA_KEY]: JSON.stringify(script.inputs) } : {}),
          ...(script.runsOnWidget ? { [SCRIPT_RUNS_ON_WIDGET_METADATA_KEY]: 'true' } : {}),
        },
      })),
      allowsCustomValue: false,
    };
  }

  function localBlock(parameters: ActionBlockParameter[]): ActionBlock {
    return {
      id: 'block-1',
      type: 'action',
      blockType: 'app.macro-deck.scripts.run-script',
      label: 'Run Script',
      color: '',
      integrationId: 'app.macro-deck.scripts',
      actionId: 'run-script',
      parameters,
    };
  }

  function remoteBlock(parameters: ActionBlockParameter[]): ActionBlock {
    return {
      id: 'block-1',
      type: 'action',
      blockType: 'app.macro-deck.delegate.run-remote-script',
      label: 'Run Remote Script',
      color: '',
      integrationId: 'app.macro-deck.delegate',
      actionId: 'run-remote-script',
      parameters,
    };
  }

  function setUp(hasOwnerWidget: boolean): void {
    options = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);
    options.getOptions.and.resolveTo(optionsFor({
      's-1': { runsOnWidget: true },
      's-2': { runsOnWidget: false },
    }));

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [RunScriptInputFieldsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionOptionsService, useValue: options },
        {
          provide: ActionFlowStore,
          useValue: {
            setBlockParameters: (_id: string, parameters: ActionBlockParameter[]) => {
              written = parameters;
            },
            hasOwnerWidget: () => hasOwnerWidget,
          },
        },
        { provide: ApiService, useValue: apiSpy },
      ],
    })
      .overrideComponent(RunScriptInputFieldsComponent, {
        set: { imports: [TranslatePipe], schemas: [CUSTOM_ELEMENTS_SCHEMA] },
      });

    fixture = TestBed.createComponent(RunScriptInputFieldsComponent);
  }

  async function render(input: ActionBlock): Promise<void> {
    fixture.componentRef.setInput('block', input);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    written = null;
  });

  it('defaults to "$self" inside a flow that already has an owner widget', async () => {
    setUp(true);

    await render(localBlock([{ name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' }]));

    expect(written?.find(p => p.name === 'widget')?.value as unknown).toBe('$self');
  });

  it('leaves the target unset and required in a script or automation with no owner widget', async () => {
    setUp(false);

    await render(localBlock([{ name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' }]));

    const param = written?.find(p => p.name === 'widget');
    expect(param?.value as unknown).toBe('');
    expect(param?.required).toBeTrue();
  });

  it('adds no widget row for a script whose flag is off', async () => {
    setUp(true);

    await render(localBlock([{ name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-2' }]));

    expect((written ?? []).some(p => p.name === 'widget')).toBeFalse();
  });

  it('drops the widget row once a previously widget-enabled script is swapped for one whose flag is off', async () => {
    setUp(true);

    await render(localBlock([
      { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-2' },
      { name: 'widget', type: 'widget-target', label: 'Widget', value: '$self', required: true },
    ]));

    expect(written?.some(p => p.name === 'widget')).toBeFalse();
  });

  it('keeps a widget the user already picked when the declarations are re-read', async () => {
    setUp(true);

    await render(localBlock([
      { name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' },
      { name: 'widget', type: 'widget-target', label: 'Widget', value: 'w-2', required: true },
    ]));

    expect(written?.find(p => p.name === 'widget')?.value as unknown).toBe('w-2');
  });

  it('creates no widget parameter at the remote call site', async () => {
    setUp(true);

    await render(remoteBlock([{ name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' }]));

    expect((written ?? []).some(p => p.name === 'widget')).toBeFalse();
  });

  it('renders a localized notice at the remote call site for a widget-enabled script', async () => {
    setUp(true);

    await render(remoteBlock([{ name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-1' }]));

    expect(fixture.nativeElement.textContent).toContain('cannot run remotely');
  });

  it('renders no notice at the remote call site for a script whose flag is off', async () => {
    setUp(true);

    await render(remoteBlock([{ name: 'scriptId', type: 'dynamic-choice', label: 'Script', value: 's-2' }]));

    expect(fixture.nativeElement.textContent).not.toContain('cannot run remotely');
  });
});
