import {
  ActionFlow,
  EVENT_TRIGGER_TYPE,
  isEventFlow,
  qualifiedEventId,
} from './action-builder.interface';
import { resolveList } from './action-flow.util';
import { LocalizationTranslator } from '../localization/localized-text';
import {
  defaultEventConfigurationValue,
  groupEventsByProvider,
  isEventFilterParameter,
  normalizeEventTriggerNames,
  EventDefinition,
} from './event-definition.interface';

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

describe('resolveList with several event flows', () => {
  it('resolves each event flow to its own list', () => {
    const flows = [eventFlow('a', 'scene-changed'), eventFlow('b', 'recording-started'), eventFlow('c')];

    const lists = flows.map(flow => resolveList(flows, `flow:${flow.triggerId}`));

    expect(lists[0]).toBe(flows[0].children);
    expect(lists[1]).toBe(flows[1].children);
    expect(lists[2]).toBe(flows[2].children);
    expect(new Set(lists).size).toBe(3);
  });

  it('still resolves a press trigger by its own id', () => {
    const flows: ActionFlow[] = [
      { triggerId: 'onShortPress', triggerType: 'onShortPress', children: [] },
      eventFlow('a', 'scene-changed'),
    ];

    expect(resolveList(flows, 'flow:onShortPress')).toBe(flows[0].children);
  });

  it('returns null for an unknown list', () => {
    expect(resolveList([eventFlow('a', 'scene-changed')], 'flow:nope')).toBeNull();
  });
});

describe('event flow helpers', () => {
  it('qualifies a binding as providerId::eventId', () => {
    expect(qualifiedEventId({ providerId: 'obs', eventId: 'scene-changed' })).toBe('obs::scene-changed');
  });

  it('has no qualified id while the binding is incomplete', () => {
    expect(qualifiedEventId(undefined)).toBeUndefined();
    expect(qualifiedEventId({ providerId: 'obs', eventId: '' })).toBeUndefined();
    expect(qualifiedEventId({ providerId: '', eventId: 'scene-changed' })).toBeUndefined();
  });

  it('recognises an event flow by its trigger type', () => {
    expect(isEventFlow(eventFlow('a', 'scene-changed'))).toBeTrue();
    expect(isEventFlow({ triggerId: 't', triggerType: 'onShortPress', children: [] })).toBeFalse();
  });
});

describe('defaultEventConfigurationValue', () => {
  function definition(
    configurationParameters: EventDefinition['configurationParameters'],
    payloadParameters: EventDefinition['payloadParameters'] = [],
    deliveryKind: EventDefinition['deliveryKind'] = 'push',
  ): EventDefinition {
    return {
      id: 'voicemeeter::macro-button-changed',
      providerId: 'voicemeeter',
      providerName: 'Voicemeeter',
      isIntegration: true,
      name: 'Macro Button Changed',
      deliveryKind,
      configurationParameters,
      payloadParameters,
    };
  }

  function seed(
    def: EventDefinition,
    declared: EventDefinition['configurationParameters'][number],
  ): unknown {
    return defaultEventConfigurationValue(def, declared);
  }

  it('seeds a numeric filter empty rather than at its minimum', () => {
    const declared = { name: 'button', type: 'number' as const, label: 'Button', min: 0, max: 79 };
    const def = definition([declared], [declared, { name: 'state', type: 'boolean', label: 'On' }]);

    expect(seed(def, declared)).toBeNull();
  });

  it('seeds a boolean filter empty, so it does not narrow to false', () => {
    const declared = { name: 'muted', type: 'boolean' as const, label: 'Muted' };
    const def = definition([declared], [declared]);

    expect(seed(def, declared)).toBeNull();
  });

  it('honours a declared default on a filter', () => {
    const declared = {
      name: 'button',
      type: 'number' as const,
      label: 'Button',
      min: 0,
      defaultValue: 3,
    };
    const def = definition([declared], [declared]);

    expect(seed(def, declared)).toBe(3);
  });

  it('still seeds a parameter that is not a filter from its type default', () => {
    const every = { name: 'every', type: 'number' as const, label: 'Every', min: 1, defaultValue: 5 };
    const unit = { name: 'unit', type: 'number' as const, label: 'Unit', min: 1 };
    const def = definition([every, unit], [{ name: 'firedAt', type: 'string', label: 'Fired at' }], 'scheduled');

    expect(seed(def, every)).toBe(5);
    expect(seed(def, unit)).toBe(1);
  });

  it('recognises a filter by a payload parameter of the same name', () => {
    const def = definition(
      [{ name: 'sceneName', type: 'string', label: 'Scene' }],
      [{ name: 'sceneName', type: 'string', label: 'Scene' }],
    );

    expect(isEventFilterParameter(def, 'sceneName')).toBeTrue();
    expect(isEventFilterParameter(def, 'every')).toBeFalse();
  });
});

describe('groupEventsByProvider', () => {
  function definition(providerId: string, id: string): EventDefinition {
    return {
      id: `${providerId}::${id}`,
      providerId,
      providerName: providerId === 'obs' ? 'OBS Studio' : 'Macro Deck',
      isIntegration: providerId === 'obs',
      name: id,
      deliveryKind: 'push',
      configurationParameters: [],
      payloadParameters: [],
    };
  }

  it('groups by provider and keeps first-appearance order', () => {
    const groups = groupEventsByProvider([
      definition('macro-deck', 'variable-changed'),
      definition('obs', 'scene-changed'),
      definition('macro-deck', 'client-connected'),
    ]);

    expect(groups.map(g => g.providerId)).toEqual(['macro-deck', 'obs']);
    expect(groups[0].events.length).toBe(2);
    expect(groups[1].providerName).toBe('OBS Studio');
  });

  it('returns nothing for an empty catalogue', () => {
    expect(groupEventsByProvider([])).toEqual([]);
  });
});

describe('normalizeEventTriggerNames', () => {
  const translator: LocalizationTranslator = {
    translate: (_scope, key) => (key === 'Obs.SceneChanged.Name' ? 'Scene Changed' : `[[${key}]]`),
  };

  it('resolves a $localized eventName to the exact expected plain string', () => {
    const flow = eventFlow('t1', 'scene-changed');
    flow.event!.eventName = { $localized: { scope: 'macrodeck.app', key: 'Obs.SceneChanged.Name' } } as unknown as string;

    const [normalized] = normalizeEventTriggerNames([flow], translator);

    expect(typeof normalized.event?.eventName).toBe('string');
    expect(normalized.event?.eventName).toBe('Scene Changed');
    expect(JSON.stringify(normalized)).not.toContain('$localized');
  });

  it('leaves an already-plain eventName untouched', () => {
    const flow = eventFlow('t1', 'scene-changed');
    flow.event!.eventName = 'Scene Changed';

    const [normalized] = normalizeEventTriggerNames([flow], translator);

    expect(normalized.event?.eventName).toBe('Scene Changed');
  });

  it('leaves a flow with no event binding untouched', () => {
    const flow = eventFlow('t1');

    const [normalized] = normalizeEventTriggerNames([flow], translator);

    expect(normalized).toEqual(flow);
  });
});
