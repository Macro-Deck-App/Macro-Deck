import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { ActionParameterType } from '@macro-deck/runtime';
import { ApiService, VariableService } from '@shared';
import { mapActionParameterDef } from '../../../domain/action-parameter-mapping.util';
import { ActionOptionsService } from '../../../services/action-options.service';
import { ActionService } from '../../../services/action.service';
import type { ActionBlockParameter, ActionParameterDef } from '@macro-deck/runtime';
import type { ActionDefinitionModel } from '../../../services/action.service';
import { ActionTesterComponent } from './action-tester.component';

function action(parameters: ActionParameterDef[] = []): ActionDefinitionModel {
  return {
    id: 'log-info',
    integrationId: 'example',
    integrationName: 'Example',
    name: 'Log Info',
    description: 'Logs a message',
    parameters,
  };
}

const variableServiceStub = { visibleForContext: () => [] };

function apiFake(): { onNotification: () => unknown; clientId: string; runActionFlow: jasmine.Spy } & Record<string, unknown> {
  return {
    onNotification: () => EMPTY,
    clientId: 'client-1',
    runActionFlow: jasmine.createSpy('runActionFlow'),
    getVariableCatalogProviders: () => Promise.resolve({ providers: [] }),
  };
}

const literalTranslator = { translate: (_scope: string, key: string) => key };

function fakeBlockParameterDefs(a: ActionDefinitionModel): Omit<ActionBlockParameter, 'value'>[] {
  return a.parameters.map(p => mapActionParameterDef(p, literalTranslator));
}

describe('ActionTesterComponent', () => {
  let fixture: ComponentFixture<ActionTesterComponent>;
  let component: ActionTesterComponent;
  let actionServiceSpy: jasmine.SpyObj<ActionService>;
  let api: ReturnType<typeof apiFake>;

  beforeEach(() => {
    actionServiceSpy = jasmine.createSpyObj<ActionService>('ActionService', ['blockParameterDefs']);
    actionServiceSpy.blockParameterDefs.and.callFake(fakeBlockParameterDefs);
    api = apiFake();

    TestBed.configureTestingModule({
      imports: [ActionTesterComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionService, useValue: actionServiceSpy },
        { provide: ApiService, useValue: api },
        { provide: VariableService, useValue: variableServiceStub },
      ],
    });

    fixture = TestBed.createComponent(ActionTesterComponent);
    component = fixture.componentInstance;
  });

  function setAction(a: ActionDefinitionModel): void {
    fixture.componentRef.setInput('action', a);
    fixture.detectChanges();
  }

  it('seeds working values from parameter defaults', () => {
    setAction(action([
      { name: 'message', type: ActionParameterType.String, description: '', defaultValue: 'hi' },
      { name: 'enabled', type: ActionParameterType.Boolean, description: '' },
      { name: 'tags', type: ActionParameterType.MultiSelect, description: '' },
      { name: 'count', type: ActionParameterType.Number, description: '' },
    ]));

    expect(component['values']() as Record<string, unknown>)
      .toEqual({ message: 'hi', enabled: false, tags: [], count: 0 });
  });

  it('renders parameters with the action-editor param rows', () => {
    setAction(action([
      { name: 'message', type: ActionParameterType.String, description: '' },
      { name: 'level', type: ActionParameterType.Choice, description: '', options: [{ value: 'info' }] },
    ]));

    const rows = fixture.nativeElement.querySelectorAll('shared-param-row');
    expect(rows.length).toBe(2);
    expect(fixture.nativeElement.querySelector('shared-config-field')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('shared-select')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('shared-variable-picker')).toBeFalsy();
  });

  describe('conditional parameters', () => {
    function withBodyType(bodyType: string): NodeListOf<Element> {
      setAction(action([
        {
          name: 'bodyType',
          type: ActionParameterType.Choice,
          description: '',
          defaultValue: bodyType,
          options: [{ value: 'none' }, { value: 'json' }],
        },
        {
          name: 'jsonBody',
          type: ActionParameterType.String,
          description: '',
          required: true,
          visibleWhen: { parameterName: 'bodyType', values: ['json'] },
        },
      ]));
      return fixture.nativeElement.querySelectorAll('shared-param-row');
    }

    it('hides a parameter whose condition is not met', () => {
      expect(withBodyType('none').length).toBe(1);
    });

    it('shows it once the condition is met', () => {
      expect(withBodyType('json').length).toBe(2);
    });

    // A field nobody can see must not be able to disable Run with nothing to point at.
    it('ignores a hidden required parameter when deciding whether the action can run', () => {
      withBodyType('none');
      expect(component['canRun']()).toBeTrue();
    });

    it('still enforces it once visible', () => {
      withBodyType('json');
      expect(component['canRun']()).toBeFalse();
    });
  });

  it('blocks the run and surfaces an error while a required parameter is empty', async () => {
    setAction(action([{ name: 'message', type: ActionParameterType.String, description: '', required: true }]));

    expect(component['canRun']()).toBeFalse();

    await component['run']();

    expect(api.runActionFlow).not.toHaveBeenCalled();
    expect(component['fieldError']('message')).toBe('This parameter is required');
  });

  for (const type of [ActionParameterType.Password, ActionParameterType.Secret]) {
    it(`keeps a required ${type} parameter blocking Run until it has a value`, () => {
      setAction(action([{ name: 'token', type, description: '', required: true }]));

      expect(component['canRun']()).toBeFalse();
    });
  }

  it('offers the template builder on text parameters', () => {
    setAction(action([{ name: 'message', type: ActionParameterType.String, description: '' }]));

    expect(fixture.nativeElement.querySelector('shared-template-builder')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('shared-variable-text-input')).toBeTruthy();
  });

  it('does not offer the template builder on a choice parameter', () => {
    setAction(action([
      { name: 'level', type: ActionParameterType.Choice, description: '', options: [{ value: 'info' }] },
    ]));

    expect(fixture.nativeElement.querySelector('shared-template-builder')).toBeFalsy();
  });

  it('runs the entered template as a one-action flow so the host renders it', async () => {
    api.runActionFlow.and.resolveTo({
      success: true, status: 'Succeeded', durationMs: 12, executionId: 'exec-1',
    });
    setAction(action([{ name: 'message', type: ActionParameterType.String, description: '' }]));

    component['setValue']('message', 'Game Change: {{ vars.category }}');
    await component['run']();

    const request = api.runActionFlow.calls.mostRecent().args[0];
    const flows = JSON.parse(request.flows);
    expect(flows.length).toBe(1);
    expect(request.triggerId).toBe(flows[0].triggerId);
    expect(request.scope).toBe('global');
    expect(request.clientId).toBe('client-1');
    expect(flows[0].children.length).toBe(1);
    expect(flows[0].children[0].integrationId).toBe('example');
    expect(flows[0].children[0].actionId).toBe('log-info');
    expect(flows[0].children[0].parameters).toEqual([
      jasmine.objectContaining({ name: 'message', type: 'string', value: 'Game Change: {{ vars.category }}' }),
    ]);
    expect(component['result']()?.success).toBeTrue();
    expect(component['result']()?.durationMs).toBe(12);
    expect(component['result']()?.status).toBe('Succeeded');
  });

  it('surfaces the host error on a failed run', async () => {
    api.runActionFlow.and.resolveTo({
      success: false,
      status: 'Failed',
      durationMs: 3,
      executionId: 'exec-2',
      error: { code: 'EXECUTION_ERROR', message: 'boom' },
    });
    setAction(action());

    await component['run']();

    expect(component['result']()?.success).toBeFalse();
    expect(component['result']()?.status).toBe('Failed');
    expect(component['result']()?.error?.message).toBe('boom');
  });

  it('reports a network error when the call throws', async () => {
    api.runActionFlow.and.rejectWith(new Error('down'));
    setAction(action());

    await component['run']();

    expect(component['result']()?.success).toBeFalse();
    expect(component['result']()?.error?.code).toBe('NETWORK_ERROR');
    expect(component['running']()).toBeFalse();
  });

  it('reseeds and clears the previous result when the action changes', async () => {
    api.runActionFlow.and.resolveTo({ success: true, durationMs: 1 });
    setAction(action());
    await component['run']();
    expect(component['result']()).not.toBeNull();

    setAction(action([{ name: 'level', type: ActionParameterType.Number, description: '', defaultValue: 50 }]));

    expect(component['result']()).toBeNull();
    expect(component['values']() as Record<string, unknown>).toEqual({ level: 50 });
  });
});

describe('ActionTesterComponent with a script that declares inputs', () => {
  let fixture: ComponentFixture<ActionTesterComponent>;
  let component: ActionTesterComponent;
  let optionsSpy: jasmine.SpyObj<ActionOptionsService>;

  const RUN_SCRIPT: ActionDefinitionModel = {
    id: 'run-script',
    integrationId: 'app.macro-deck.scripts',
    integrationName: 'Scripts',
    name: 'Run Script',
    description: 'Runs one of your scripts',
    parameters: [{ name: 'scriptId', type: ActionParameterType.DynamicChoice, description: '' }],
    role: 'run-script',
  };

  function optionsFor(scriptId: string, inputs: unknown[]) {
    return {
      options: [{ value: scriptId, label: 'Test', metadata: { scriptInputs: JSON.stringify(inputs) } }],
    };
  }

  beforeEach(() => {
    const actionServiceSpy = jasmine.createSpyObj<ActionService>('ActionService', ['blockParameterDefs']);
    actionServiceSpy.blockParameterDefs.and.callFake(fakeBlockParameterDefs);
    optionsSpy = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);

    TestBed.configureTestingModule({
      imports: [ActionTesterComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionService, useValue: actionServiceSpy },
        { provide: ActionOptionsService, useValue: optionsSpy },
        { provide: ApiService, useValue: apiFake() },
        { provide: VariableService, useValue: variableServiceStub },
      ],
    });

    fixture = TestBed.createComponent(ActionTesterComponent);
    component = fixture.componentInstance;
  });

  async function selectScript(scriptId: string, inputs: unknown[]): Promise<void> {
    optionsSpy.getOptions.and.resolveTo(optionsFor(scriptId, inputs) as never);
    component['setValue']('scriptId', scriptId);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  }

  it('offers a field for every input the selected script declares, seeded with its default', async () => {
    fixture.componentRef.setInput('action', RUN_SCRIPT);
    fixture.detectChanges();

    await selectScript('script-1', [
      { name: 'scene', type: 'text', defaultValue: 'Starting Soon', required: true },
      { name: 'volume', type: 'numeric' },
    ]);

    const values = component['values']() as Record<string, unknown>;
    expect(values['input:scene']).toBe('Starting Soon');
    // Left empty rather than seeded to 0: an unanswered number must not be posted as a real value.
    expect(values['input:volume']).toBe('');
  });

  it('drops the fields of the script that was selected before, so a leftover is never posted', async () => {
    fixture.componentRef.setInput('action', RUN_SCRIPT);
    fixture.detectChanges();

    await selectScript('script-1', [{ name: 'scene', type: 'text' }]);
    await selectScript('script-2', [{ name: 'channel', type: 'text' }]);

    const values = component['values']() as Record<string, unknown>;
    expect(Object.keys(values)).not.toContain('input:scene');
    expect(Object.keys(values)).toContain('input:channel');
  });

  it('asks for the declarations without a filter, so the picked script is always in the answer', async () => {
    fixture.componentRef.setInput('action', RUN_SCRIPT);
    fixture.detectChanges();

    await selectScript('script-1', [{ name: 'scene', type: 'text' }]);

    expect(optionsSpy.getOptions).toHaveBeenCalledWith(jasmine.objectContaining({
      integrationId: 'app.macro-deck.scripts',
      actionId: 'run-script',
      parameterName: 'scriptId',
    }));
    expect(optionsSpy.getOptions.calls.mostRecent().args[0].filter).toBeUndefined();
  });
});
