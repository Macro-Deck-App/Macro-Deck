import { isVariableReference } from '@macro-deck/runtime';
import type { ActionBlockParameter } from '@macro-deck/runtime';

export function isParameterVisible(
  param: Pick<ActionBlockParameter, 'visibleWhen'>,
  siblings: readonly ActionBlockParameter[] | undefined,
): boolean {
  const condition = param.visibleWhen;
  if (!condition) return true;

  const sibling = siblings?.find(candidate => candidate.name === condition.parameterName);
  if (!sibling) return true;
  if (isVariableReference(sibling.value)) return true;

  const current = typeof sibling.value === 'string' ? sibling.value : String(sibling.value ?? '');
  return condition.values.some(value => value.toLowerCase() === current.toLowerCase());
}
