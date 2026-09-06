import { ActionFlow, EVENT_TRIGGER_TYPE } from '@macro-deck/runtime';
import { validateActionFlows } from './action-flow-validation.util';

function eventFlow(triggerId: string, eventId?: string): ActionFlow {
  return {
    triggerId,
    triggerType: EVENT_TRIGGER_TYPE,
    event: eventId
      ? { providerId: 'obs', eventId, parameters: [] }
      : undefined,
    children: [
      {
        id: `${triggerId}-block`,
        type: 'action',
        blockType: 'obs.set-scene',
        label: 'Set Scene',
        color: '#000',
        parameters: [],
      },
    ],
  };
}

describe('validateActionFlows for event triggers', () => {
  it('rejects a trigger with no event bound, keyed by its trigger id', () => {
    const result = validateActionFlows([eventFlow('a')]);

    expect(result.valid).toBeFalse();
    expect(result.errors[0].blockId).toBe('a');
  });

  it('rejects an empty required configuration parameter', () => {
    const flow = eventFlow('a', 'scene-changed');
    flow.event!.parameters = [
      { name: 'sceneName', type: 'string', label: 'Scene', value: '', required: true },
    ];

    const result = validateActionFlows([flow]);

    expect(result.valid).toBeFalse();
    expect(result.errors[0].paramName).toBe('sceneName');
  });

  it('accepts a bound trigger whose optional parameters are empty', () => {
    const flow = eventFlow('a', 'scene-changed');
    flow.event!.parameters = [{ name: 'sceneName', type: 'string', label: 'Scene', value: '' }];

    expect(validateActionFlows([flow]).valid).toBeTrue();
  });
});
