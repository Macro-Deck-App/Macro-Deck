import type { Variable } from './variable.interface';

export const STATE_VARIABLE_NAME = 'state';

// Snake case because host variable names are lowercase-only: a camelCase name is rejected outright
// rather than sanitized, so the variable would never appear.
export const STATE_LABEL_VARIABLE_NAME = 'state_label';

export function createPendingStateVariable(scopeRefId: string, firstStateId: string): Variable {
  return {
    id: `pending-state-${scopeRefId}`,
    name: STATE_VARIABLE_NAME,
    scope: 'widget',
    scopeRefId,
    type: 'text',
    classification: 'widget',
    value: firstStateId,
  };
}

export function createPendingStateLabelVariable(scopeRefId: string, firstStateLabel: string): Variable {
  return {
    id: `pending-state-label-${scopeRefId}`,
    name: STATE_LABEL_VARIABLE_NAME,
    scope: 'widget',
    scopeRefId,
    type: 'text',
    classification: 'widget',
    value: firstStateLabel,
  };
}
