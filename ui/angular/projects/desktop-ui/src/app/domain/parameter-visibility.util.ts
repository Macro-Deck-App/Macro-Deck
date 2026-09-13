import { isVariableReference } from '@macro-deck/runtime';
import type { ActionBlockParameter, ActionParameterDef, ParameterVisibility } from '@macro-deck/runtime';

export function isParameterVisible(
  param: Pick<ActionBlockParameter, 'visibleWhen'>,
  siblings: readonly ActionBlockParameter[] | undefined,
): boolean {
  const condition = param.visibleWhen;
  if (!condition) return true;

  const sibling = siblings?.find(candidate => candidate.name === condition.parameterName);
  if (!sibling) return true;

  return matchesVisibility(condition, sibling.value);
}

export function isFieldVisible(
  field: Pick<ActionParameterDef, 'visibleWhen'>,
  fields: readonly Pick<ActionParameterDef, 'name'>[],
  values: Readonly<Record<string, unknown>>,
): boolean {
  const condition = field.visibleWhen;
  if (!condition) return true;
  if (!fields.some(candidate => candidate.name === condition.parameterName)) return true;

  return matchesVisibility(condition, values[condition.parameterName]);
}

function matchesVisibility(condition: ParameterVisibility, value: unknown): boolean {
  if (isVariableReference(value)) return true;

  const current = typeof value === 'string' ? value : String(value ?? '');
  return condition.values.some(candidate => candidate.toLowerCase() === current.toLowerCase());
}
