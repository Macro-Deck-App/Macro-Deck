import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { ActionParameterType } from '@macro-deck/runtime';
import { ApiService } from '@shared';
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

const literalTranslator = { translate: (_scope: string, key: string) => key };

function fakeBlockParameterDefs(a: ActionDefinitionModel): Omit<ActionBlockParameter, 'value'>[] {
  return a.parameters.map(p => mapActionParameterDef(p, literalTranslator));
}

describe('ActionTesterComponent', () => {
  let fixture: ComponentFixture<ActionTesterComponent>;
  let component: ActionTesterComponent;
  let actionServiceSpy: jasmine.SpyObj<ActionService>;

  beforeEach(() => {
    actionServiceSpy = jasmine.createSpyObj<ActionService>('ActionService', ['runAction', 'blockParameterDefs']);
    actionServiceSpy.blockParameterDefs.and.callFake(fakeBlockParameterDefs);

    TestBed.configureTestingModule({
      imports: [ActionTesterComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionService, useValue: actionServiceSpy },
        { provide: ApiService, useValue: { onNotification: () => EMPTY } },
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

    expect(actionServiceSpy.runAction).not.toHaveBeenCalled();
    expect(component['fieldError']('message')).toBe('This parameter is required');
  });

  it('runs the action with the entered values and surfaces a success result', async () => {
    actionServiceSpy.runAction.and.resolveTo({
      success: true, status: 'Succeeded', durationMs: 12, executionId: 'exec-1',
    });
    setAction(action([{ name: 'message', type: ActionParameterType.String, description: '' }]));

    component['setValue']('message', 'hello');
    await component['run']();

    expect(actionServiceSpy.runAction).toHaveBeenCalledWith('example', 'log-info', { message: 'hello' });
    expect(component['result']()?.success).toBeTrue();
    expect(component['result']()?.durationMs).toBe(12);
    expect(component['result']()?.status).toBe('Succeeded');
  });

  it('surfaces the host error on a failed run', async () => {
    actionServiceSpy.runAction.and.resolveTo({
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
    actionServiceSpy.runAction.and.rejectWith(new Error('down'));
    setAction(action());

    await component['run']();

    expect(component['result']()?.success).toBeFalse();
    expect(component['result']()?.error?.code).toBe('NETWORK_ERROR');
  });

  it('reseeds and clears the previous result when the action changes', async () => {
    actionServiceSpy.runAction.and.resolveTo({ success: true, durationMs: 1 });
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
  };

  function optionsFor(scriptId: string, inputs: unknown[]) {
    return {
      options: [{ value: scriptId, label: 'Test', metadata: { scriptInputs: JSON.stringify(inputs) } }],
    };
  }

  beforeEach(() => {
    const actionServiceSpy = jasmine.createSpyObj<ActionService>('ActionService', ['runAction', 'blockParameterDefs']);
    actionServiceSpy.blockParameterDefs.and.callFake(fakeBlockParameterDefs);
    optionsSpy = jasmine.createSpyObj<ActionOptionsService>('ActionOptionsService', ['getOptions']);

    TestBed.configureTestingModule({
      imports: [ActionTesterComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ActionService, useValue: actionServiceSpy },
        { provide: ActionOptionsService, useValue: optionsSpy },
        { provide: ApiService, useValue: { onNotification: () => EMPTY } },
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
