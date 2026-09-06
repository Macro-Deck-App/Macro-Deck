import { scriptInputParameter } from '@macro-deck/runtime';
import type { ActionFlow, ScriptInput } from '@macro-deck/runtime';
import { validateActionFlows } from './action-flow-validation.util';

describe('script input declarations under action-flow validation', () => {
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

  it('writes required: false on an optional input, so it cannot block Save changes', () => {
      const optional: ScriptInput = { name: 'scene', type: 'text' };
  
      const parameter = scriptInputParameter(optional, '');
  
      expect(parameter.required).toBeFalse();
      expect(validateActionFlows(flowWith(parameter)).valid).toBeTrue();
    });

  it('leaves a required input with no default blocking until it is filled in', () => {
      const required: ScriptInput = { name: 'scene', type: 'text', required: true };
  
      expect(validateActionFlows(flowWith(scriptInputParameter(required, ''))).valid).toBeFalse();
      expect(validateActionFlows(flowWith(scriptInputParameter(required, 'Live'))).valid).toBeTrue();
    });

  it('does not require a value for a required input that declares a default', () => {
      const required: ScriptInput = {
        name: 'scene',
        type: 'text',
        required: true,
        defaultValue: 'Starting Soon',
      };
  
      expect(validateActionFlows(flowWith(scriptInputParameter(required, ''))).valid).toBeTrue();
    });
});
