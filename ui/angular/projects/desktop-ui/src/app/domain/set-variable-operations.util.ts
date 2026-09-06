import type { VariableType } from '@macro-deck/runtime';
import type { SelectOption } from '../components/forms/select/select.component';

export type SetVariableOperation = 'set' | 'add' | 'toggle' | 'append';

export const SET_VARIABLE_DEFAULT_OPERATION: SetVariableOperation = 'set';

const OPERATION_LABELS: Readonly<Record<SetVariableOperation, string>> = {
  set: 'Set to',
  add: 'Add',
  toggle: 'Toggle',
  append: 'Append',
};

export function operationsFor(type: VariableType | undefined): SetVariableOperation[] {
  switch (type) {
    case 'boolean':
      return ['set', 'toggle'];
    case 'numeric':
      return ['set', 'add'];
    case 'text':
      return ['set', 'append'];
    default:
      return ['set', 'add', 'toggle', 'append'];
  }
}

export function operationOptionsFor(type: VariableType | undefined): SelectOption[] {
  return operationsFor(type).map(operation => ({ value: operation, label: OPERATION_LABELS[operation] }));
}

export function isOperationAllowed(operation: string, type: VariableType | undefined): boolean {
  return operationsFor(type).includes(operation as SetVariableOperation);
}
