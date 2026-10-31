import { AppStrings } from '@macro-deck/runtime';
import type { VariableType } from '@macro-deck/runtime';
import type { SelectOption } from '../components/forms/select/select.component';

export type SetVariableOperation = 'set' | 'add' | 'toggle' | 'append';

export const SET_VARIABLE_DEFAULT_OPERATION: SetVariableOperation = 'set';

const OPERATION_LABEL_KEYS: Readonly<Record<SetVariableOperation, string>> = {
  set: AppStrings.Integrations.Variables.Actions.OperationSetTo,
  add: AppStrings.Integrations.Variables.Actions.OperationAdd,
  toggle: AppStrings.Integrations.Variables.Actions.OperationToggle,
  append: AppStrings.Integrations.Variables.Actions.OperationAppend,
};

export function operationsFor(type: VariableType | undefined): SetVariableOperation[] {
  switch (type) {
    case 'boolean':
      return ['set', 'toggle'];
    case 'numeric':
      return ['set', 'add'];
    case 'text':
      return ['set', 'append'];
    case 'color':
      return ['set'];
    default:
      return ['set', 'add', 'toggle', 'append'];
  }
}

export function operationOptionsFor(type: VariableType | undefined, translate: (key: string) => string): SelectOption[] {
  return operationsFor(type).map(operation => ({ value: operation, label: translate(OPERATION_LABEL_KEYS[operation]) }));
}

export function isOperationAllowed(operation: string, type: VariableType | undefined): boolean {
  return operationsFor(type).includes(operation as SetVariableOperation);
}
