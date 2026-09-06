import { ActionBlockParameter, ComparisonOperator, EventDefinition, isEventFilterParameter } from '@macro-deck/runtime';
import type { Variable } from '@macro-deck/runtime';

const ORDERING_OPERATORS: ReadonlyArray<ComparisonOperator> = ['>', '<', '>=', '<='];
const UNIVERSAL_OPERATORS: ReadonlyArray<ComparisonOperator> =
  ['==', '!=', 'isEmpty', 'isNotEmpty', 'isAvailable', 'isNotAvailable'];

export const VARIABLES_OPTIONS_SOURCE_ID = 'macrodeck.variables';

export const BOOLEAN_VALUE_OPTIONS: { label: string; value: string }[] = [
  { label: 'true', value: 'true' },
  { label: 'false', value: 'false' },
];

export function refineEventConfigurationParameters(
  definition: EventDefinition | undefined,
  parameters: ActionBlockParameter[],
  variables: Variable[],
): ActionBlockParameter[] {
  if (!definition) return parameters;

  const picker = parameters.find(p => p.optionsSourceId === VARIABLES_OPTIONS_SOURCE_ID);
  const watchedName = typeof picker?.value === 'string' ? picker.value : '';
  if (!watchedName) return parameters;

  const watched = variables.find(v => v.name === watchedName && v.origin !== 'event');
  if (!watched || watched.type === 'text') return parameters;

  const payloadNames = new Set(definition.payloadParameters.map(p => p.name));

  return parameters.map(param => {
    if (param === picker || !payloadNames.has(param.name) || param.type !== 'string') {
      return param;
    }

    return watched.type === 'boolean'
      ? { ...param, type: 'choice' as const, options: BOOLEAN_VALUE_OPTIONS }
      : { ...param, type: 'number' as const };
  });
}

export function supportsFilterOperator(
  definition: EventDefinition | undefined,
  param: ActionBlockParameter,
): boolean {
  if (!definition || definition.deliveryKind === 'scheduled') return false;
  return isEventFilterParameter(definition, param.name);
}

export function filterOperatorsFor(
  definition: EventDefinition | undefined,
  param: ActionBlockParameter,
): ReadonlyArray<ComparisonOperator> {
  if (!supportsFilterOperator(definition, param)) return [];
  return param.type === 'number'
    ? [...UNIVERSAL_OPERATORS, ...ORDERING_OPERATORS]
    : UNIVERSAL_OPERATORS;
}
