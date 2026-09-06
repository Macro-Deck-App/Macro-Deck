import { ActionBlockParameter, ComparisonOperator, EventDefinition, eventPayloadVariables } from '@macro-deck/runtime';
import type { Variable } from '@macro-deck/runtime';
import { BOOLEAN_VALUE_OPTIONS, VARIABLES_OPTIONS_SOURCE_ID, filterOperatorsFor, refineEventConfigurationParameters, supportsFilterOperator } from './event-parameter-refinement.util';

function variable(name: string, type: Variable['type']): Variable {
  return {
    id: name,
    name,
    scope: 'global',
    type,
    classification: 'integration',
    value: '',
  };
}

const variableChanged: EventDefinition = {
  id: 'macro-deck::variable-changed',
  providerId: 'macro-deck',
  providerName: 'Macro Deck',
  isIntegration: false,
  name: 'Variable Changed',
  deliveryKind: 'push',
  configurationParameters: [
    { name: 'variable', type: 'autocomplete', label: 'Variable', optionsSourceId: VARIABLES_OPTIONS_SOURCE_ID },
    { name: 'value', type: 'string', label: 'Changed to' },
    { name: 'previousValue', type: 'string', label: 'Changed from' },
  ],
  payloadParameters: [
    { name: 'variable', type: 'string', label: 'Variable' },
    { name: 'value', type: 'string', label: 'New value' },
    { name: 'previousValue', type: 'string', label: 'Previous value' },
  ],
};

function parameters(watched: string): ActionBlockParameter[] {
  return [
    {
      name: 'variable',
      type: 'autocomplete',
      label: 'Variable',
      value: watched,
      optionsSourceId: VARIABLES_OPTIONS_SOURCE_ID,
    },
    { name: 'value', type: 'string', label: 'Changed to', value: '' },
    { name: 'previousValue', type: 'string', label: 'Changed from', value: '' },
  ];
}

describe('refineEventConfigurationParameters', () => {
  const variables = [
    variable('spotify_is_connected', 'boolean'),
    variable('system_cpu_usage_percent', 'numeric'),
    variable('obs_current_scene', 'text'),
  ];

  it('turns the value filters into a true/false choice for a boolean variable', () => {
    const refined = refineEventConfigurationParameters(
      variableChanged,
      parameters('spotify_is_connected'),
      variables,
    );

    expect(refined[1].type).toBe('choice');
    expect(refined[1].options).toEqual(BOOLEAN_VALUE_OPTIONS);
    expect(refined[2].type).toBe('choice');
  });

  it('turns them into number inputs for a numeric variable', () => {
    const refined = refineEventConfigurationParameters(
      variableChanged,
      parameters('system_cpu_usage_percent'),
      variables,
    );

    expect(refined[1].type).toBe('number');
    expect(refined[2].type).toBe('number');
  });

  it('leaves them alone for a text variable', () => {
    const refined = refineEventConfigurationParameters(
      variableChanged,
      parameters('obs_current_scene'),
      variables,
    );

    expect(refined.map(p => p.type)).toEqual(['autocomplete', 'string', 'string']);
  });

  it('never rewrites the variable picker itself', () => {
    const refined = refineEventConfigurationParameters(
      variableChanged,
      parameters('spotify_is_connected'),
      variables,
    );

    expect(refined[0].type).toBe('autocomplete');
    expect(refined[0].optionsSourceId).toBe(VARIABLES_OPTIONS_SOURCE_ID);
  });

  it('leaves everything alone until a variable is chosen', () => {
    const refined = refineEventConfigurationParameters(variableChanged, parameters(''), variables);

    expect(refined.map(p => p.type)).toEqual(['autocomplete', 'string', 'string']);
  });

  it('leaves everything alone for an unknown variable name', () => {
    const refined = refineEventConfigurationParameters(variableChanged, parameters('nope'), variables);

    expect(refined.map(p => p.type)).toEqual(['autocomplete', 'string', 'string']);
  });

  it('does not touch a configuration parameter with no matching payload parameter', () => {
    const definition: EventDefinition = {
      ...variableChanged,
      configurationParameters: [...variableChanged.configurationParameters, { name: 'note', type: 'string', label: 'Note' }],
      payloadParameters: variableChanged.payloadParameters,
    };
    const params: ActionBlockParameter[] = [
      ...parameters('spotify_is_connected'),
      { name: 'note', type: 'string', label: 'Note', value: '' },
    ];

    const refined = refineEventConfigurationParameters(definition, params, variables);

    expect(refined.find(p => p.name === 'note')!.type).toBe('string');
  });

  it('leaves an event without a variable picker alone', () => {
    const definition: EventDefinition = {
      ...variableChanged,
      configurationParameters: [{ name: 'sceneName', type: 'string', label: 'Scene' }],
      payloadParameters: [{ name: 'sceneName', type: 'string', label: 'Scene' }],
    };
    const params: ActionBlockParameter[] = [
      { name: 'sceneName', type: 'string', label: 'Scene', value: 'Live' },
    ];

    expect(refineEventConfigurationParameters(definition, params, variables)).toEqual(params);
  });

  it('ignores an event payload entry with the same name', () => {
    const payloadEntries = eventPayloadVariables(variableChanged);
    const refined = refineEventConfigurationParameters(
      variableChanged,
      parameters('value'),
      payloadEntries,
    );

    expect(refined.map(p => p.type)).toEqual(['autocomplete', 'string', 'string']);
  });
});

describe('supportsFilterOperator', () => {
  const variables = [
    variable('spotify_is_connected', 'boolean'),
    variable('system_cpu_usage_percent', 'numeric'),
  ];

  it('is true for a string filter narrowed to number by refineEventConfigurationParameters', () => {
    const refined = refineEventConfigurationParameters(
      variableChanged,
      parameters('system_cpu_usage_percent'),
      variables,
    );

    expect(supportsFilterOperator(variableChanged, refined[1])).toBeTrue();
  });

  it('is true for a parameter an event declares as number outright, with no variable picker at all', () => {
    const definition: EventDefinition = {
      id: 'obs::recording-progress',
      providerId: 'obs',
      providerName: 'OBS',
      isIntegration: true,
      name: 'Recording Progress',
      deliveryKind: 'push',
      configurationParameters: [
        { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds (min)' },
      ],
      payloadParameters: [
        { name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds' },
      ],
    };
    const param: ActionBlockParameter = {
      name: 'elapsedSeconds', type: 'number', label: 'Elapsed seconds (min)', value: 0,
    };

    expect(supportsFilterOperator(definition, param)).toBeTrue();
  });

  it('is true for the boolean-narrowed choice - availability is a question every type can answer', () => {
    const refined = refineEventConfigurationParameters(
      variableChanged,
      parameters('spotify_is_connected'),
      variables,
    );

    expect(supportsFilterOperator(variableChanged, refined[1])).toBeTrue();
  });

  it('is true for a plain string filter - availability is a question every type can answer', () => {
    const refined = refineEventConfigurationParameters(variableChanged, parameters(''), variables);

    expect(supportsFilterOperator(variableChanged, refined[1])).toBeTrue();
  });

  it('is false for a configuration parameter with no matching payload parameter', () => {
    const definition: EventDefinition = {
      ...variableChanged,
      configurationParameters: [...variableChanged.configurationParameters, { name: 'threshold', type: 'number', label: 'Threshold' }],
    };
    const param: ActionBlockParameter = { name: 'threshold', type: 'number', label: 'Threshold', value: 0 };

    expect(supportsFilterOperator(definition, param)).toBeFalse();
  });

  it('is false when deliveryKind is scheduled', () => {
    const definition: EventDefinition = {
      ...variableChanged,
      deliveryKind: 'scheduled',
      configurationParameters: [{ name: 'value', type: 'number', label: 'Changed to' }],
    };
    const param: ActionBlockParameter = { name: 'value', type: 'number', label: 'Changed to', value: 0 };

    expect(supportsFilterOperator(definition, param)).toBeFalse();
  });

  it('is false for an undefined definition', () => {
    const param: ActionBlockParameter = { name: 'value', type: 'number', label: 'Changed to', value: 0 };

    expect(supportsFilterOperator(undefined, param)).toBeFalse();
  });
});

describe('filterOperatorsFor', () => {
  const STATE_OPS: ComparisonOperator[] = ['isEmpty', 'isNotEmpty', 'isAvailable', 'isNotAvailable'];

  it('offers ordering comparisons only for a number parameter, alongside equality and the state operators', () => {
    const param: ActionBlockParameter = { name: 'value', type: 'number', label: 'Changed to', value: 0 };
    const expected: ComparisonOperator[] = ['==', '!=', ...STATE_OPS, '>', '<', '>=', '<='];

    expect(filterOperatorsFor(variableChanged, param)).toEqual(expected);
  });

  it('drops ordering comparisons for a non-number parameter, keeping equality and the state operators', () => {
    const param: ActionBlockParameter = { name: 'value', type: 'string', label: 'Changed to', value: '' };
    const expected: ComparisonOperator[] = ['==', '!=', ...STATE_OPS];

    expect(filterOperatorsFor(variableChanged, param)).toEqual(expected);
  });

  it('offers nothing for a parameter that does not qualify as a filter at all', () => {
    const definition: EventDefinition = {
      ...variableChanged,
      deliveryKind: 'scheduled',
    };
    const param: ActionBlockParameter = { name: 'value', type: 'number', label: 'Changed to', value: 0 };

    expect(filterOperatorsFor(definition, param)).toEqual([]);
  });
});
