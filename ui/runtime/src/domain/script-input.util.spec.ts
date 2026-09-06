import type { ActionFlow } from './action-builder.interface';
import type { ScriptInput } from './script.interface';
import {
  SCRIPT_INPUTS_METADATA_KEY,
  normalizeScriptInputs,
  parseScriptInputsMetadata,
  scriptInputDefaultValue,
  scriptInputParameter,
  scriptInputVariables,
} from './script-input.util';

describe('script input declarations', () => {
  function flowWith(...parameters: ReturnType<typeof scriptInputParameter>[]): ActionFlow[] {
    return [{
      triggerId: 'press',
      triggerType: 'press',
      children: [{
        id: 'block-1',
        type: 'action',
        blockType: 'app.macro-deck.scripts.run-script',
        label: 'Run Script',
        color: 'red',
        integrationId: 'app.macro-deck.scripts',
        actionId: 'run-script',
        parameters,
      }],
    }] as ActionFlow[];
  }

  it('names the parameter after the input and picks the control its type asks for', () => {
    const numeric: ScriptInput = { name: 'volume', type: 'numeric', label: 'Volume' };

    const parameter = scriptInputParameter(numeric, 42);

    expect(parameter.name).toBe('input:volume');
    expect(parameter.type).toBe('number');
    expect(parameter.label).toBe('Volume');
  });

  it('gives every type the control and the accepted variable type a variable of it gets', () => {
    const controls = (['text', 'numeric', 'boolean'] as const).map(type =>
      scriptInputParameter({ name: 'x', type }, ''));

    expect(controls.map(parameter => parameter.type)).toEqual(['string', 'number', 'boolean']);
    expect(controls.map(parameter => parameter.acceptedVariableTypes))
      .toEqual([['text'], ['numeric'], ['boolean']]);
  });

  it('starts a field on the declared default, typed', () => {
    const seed = (input: ScriptInput): unknown => scriptInputDefaultValue(input);

    expect(seed({ name: 'a', type: 'text', defaultValue: 'Live' })).toBe('Live');
    expect(seed({ name: 'b', type: 'numeric', defaultValue: '42' })).toBe(42);
    expect(seed({ name: 'c', type: 'boolean', defaultValue: 'true' })).toBeTrue();
    expect(seed({ name: 'd', type: 'text' })).toBe('');
  });

  it('offers declarations in the picker as plain variables, marked by origin', () => {
    const variables = scriptInputVariables([
      { name: 'scene', type: 'text' },
      { name: 'volume', type: 'numeric' },
    ]);

    expect(variables.map(variable => variable.name)).toEqual(['scene', 'volume']);
    expect(variables.map(variable => variable.origin)).toEqual(['input', 'input']);
    expect(variables.map(variable => variable.type)).toEqual(['text', 'numeric']);
  });

  it('reads declarations off a picker option, and an option without them as none', () => {
    const encoded = JSON.stringify([{ name: 'scene', type: 'text', required: true }]);

    expect(parseScriptInputsMetadata({ [SCRIPT_INPUTS_METADATA_KEY]: encoded }))
      .toEqual([{ name: 'scene', type: 'text', required: true }]);
    expect(parseScriptInputsMetadata(undefined)).toEqual([]);
    expect(parseScriptInputsMetadata({ [SCRIPT_INPUTS_METADATA_KEY]: '{ not json' })).toEqual([]);
  });

  it('drops entries that are not usable declarations rather than rendering them', () => {
    expect(normalizeScriptInputs([{ type: 'text' }, { name: '' }, null, 'scene'])).toEqual([]);
    expect(normalizeScriptInputs(undefined)).toEqual([]);
    expect(normalizeScriptInputs([{ name: 'scene', type: 'nonsense' }]))
      .toEqual([{ name: 'scene', type: 'text' }]);
  });
});
