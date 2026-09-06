import { ActionBlock, ActionBlockParameter, ActionFlow, AppStrings, ConditionExpression, ParameterValue, WIDGET_TARGET_SELF, containsLiquid, isComparisonExpression, isEventFlow, isEventReference, isStateOperator, isVariableReference } from '@macro-deck/runtime';
import { isParameterVisible } from './parameter-visibility.util';
import { isValidUrl } from './url-input.util';

export interface ActionFlowValidationError {
  blockId: string;
  branchId?: string;
  paramName?: string;
  message: string;
}

export interface ActionFlowValidationResult {
  valid: boolean;
  errors: ActionFlowValidationError[];
}

export interface ActionFlowValidationContext {
  hasOwnerWidget?: boolean;
}

export type ValidationTranslator = (key: string) => string;

const KEY_ONLY: ValidationTranslator = key => key;

export function validateActionFlows(
  flows: ActionFlow[],
  context: ActionFlowValidationContext = {},
  t: ValidationTranslator = KEY_ONLY,
): ActionFlowValidationResult {
  const errors: ActionFlowValidationError[] = [];
  for (const flow of flows) {
    if (isEventFlow(flow)) {
      validateEventBinding(flow, errors, t);
    }
    for (const block of flow.children) {
      validateBlock(block, errors, context, t);
    }
  }
  return { valid: errors.length === 0, errors };
}

function validateEventBinding(flow: ActionFlow, errors: ActionFlowValidationError[],
  t: ValidationTranslator): void {
  const binding = flow.event;
  if (!binding?.providerId || !binding.eventId) {
    errors.push({
      blockId: flow.triggerId,
      message: t(AppStrings.ActionBuilder.Validation.PickEvent),
    });
    return;
  }

  for (const param of binding.parameters ?? []) {
    // A state operator (isEmpty/isAvailable/…) is meaningful for a blank filter value - it is what
    // asks whether the payload arrived at all - so an empty value must not block save here.
    if (param.required === true && isValueEmpty(param.value) && !isStateOperator(param.operator)) {
      errors.push({
        blockId: flow.triggerId,
        paramName: param.name,
        message: `${param.label || param.name} is required`,
      });
      continue;
    }

    const formatError = parameterFormatError(param);
    if (formatError) {
      errors.push({
        blockId: flow.triggerId,
        paramName: param.name,
        message: formatError,
      });
    }
  }
}

function validateBlock(
  block: ActionBlock,
  errors: ActionFlowValidationError[],
  context: ActionFlowValidationContext,
  t: ValidationTranslator,
): void {
  if (block.disabled) return;

  if (block.parameters) {
    for (const param of block.parameters) {
      // A field the editor is not showing cannot be filled in, so it must not be able to block a
      // save - the body fields of the four body types the user did not pick are the case in point.
      if (!isParameterVisible(param, block.parameters)) continue;

      if (isParameterEmpty(param, context)) {
        errors.push({
          blockId: block.id,
          paramName: param.name,
          message: `${param.label || param.name} is required`,
        });
        continue;
      }

      const formatError = parameterFormatError(param);
      if (formatError) {
        errors.push({
          blockId: block.id,
          paramName: param.name,
          message: formatError,
        });
      }
    }
  }

  if (block.condition) {
    validateExpression(block.condition, block.id, undefined, errors, t);
  }

  if (block.children) {
    for (const child of block.children) validateBlock(child, errors, context, t);
  }

  if (block.branches) {
    for (const branch of block.branches) {
      if (branch.kind !== 'else' && branch.condition) {
        validateExpression(branch.condition, block.id, branch.id, errors, t);
      }
      for (const child of branch.children) validateBlock(child, errors, context, t);
    }
  }
}

export function isConditionComplete(expr: ConditionExpression): boolean {
  if (isComparisonExpression(expr)) {
    if (isConditionOperandEmpty(expr.left)) return false;
    if (isStateOperator(expr.operator)) {
      // A state operator's right operand is ignored, but the left side must be a variable/event
      // reference - free text is rendered before comparison, so a typed placeholder would read a
      // quietly wrong verdict. Keep in sync with validateExpression below.
      return isVariableReference(expr.left) || isEventReference(expr.left);
    }
    return !isConditionOperandEmpty(expr.right);
  }
  return expr.operands.every(isConditionComplete);
}

function validateExpression(
  expr: ConditionExpression,
  blockId: string,
  branchId: string | undefined,
  errors: ActionFlowValidationError[],
  t: ValidationTranslator,
): void {
  if (isComparisonExpression(expr)) {
    const stateOp = isStateOperator(expr.operator);
    if (isConditionOperandEmpty(expr.left)) {
      errors.push({ blockId, branchId, message: t(AppStrings.ActionBuilder.Validation.ConditionLeftEmpty) });
    } else if (stateOp && !isVariableReference(expr.left) && !isEventReference(expr.left)) {
      // Free text is rendered before it is compared, so a hand-typed `{{ vars.artist }}` becomes the
      // literal placeholder text for an unavailable variable and a state operator would read a
      // quietly wrong verdict - only a genuine variable/event reference is trustworthy here.
      errors.push({ blockId, branchId, message: t(AppStrings.ActionBuilder.Validation.ConditionStateOperandNotVariable) });
    }
    if (!stateOp && isConditionOperandEmpty(expr.right)) {
      errors.push({ blockId, branchId, message: t(AppStrings.ActionBuilder.Validation.ConditionRightEmpty) });
    }
    return;
  }
  for (const operand of expr.operands) {
    validateExpression(operand, blockId, branchId, errors, t);
  }
}

function isParameterEmpty(param: ActionBlockParameter, context: ActionFlowValidationContext): boolean {
  if (param.type === 'boolean') return false;
  if (param.required === false) return false;
  if (param.type === 'widget-target' && param.value === WIDGET_TARGET_SELF && !context.hasOwnerWidget) {
    return true;
  }
  return isValueEmpty(param.value);
}

const IPV4_PATTERN = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/;
const IPV6_PATTERN = /^[0-9a-fA-F:]+$/;

function parameterFormatError(param: ActionBlockParameter): string | null {
  const value = param.value;
  if (value === null || value === undefined || isVariableReference(value)) return null;

  if (typeof value === 'string' && value.trim() !== '') {
    if (param.validationRegex) {
      try {
        if (!new RegExp(param.validationRegex).test(value)) {
          return `${param.label || param.name} does not match the expected format`;
        }
      } catch {
      }
    }

    if (param.maxLength && value.length > param.maxLength) {
      return `${param.label || param.name} exceeds ${param.maxLength} characters`;
    }

    if (containsLiquid(value)) return null;

    switch (param.type) {
      case 'url':
        if (!isValidUrl(value)) {
          return `${param.label || param.name} is not a valid URL`;
        }
        break;
      case 'ipaddress': {
        const v4 = IPV4_PATTERN.exec(value);
        const validV4 = v4 !== null && v4.slice(1).every(part => Number(part) <= 255);
        const validV6 = value.includes(':') && IPV6_PATTERN.test(value);
        if (!validV4 && !validV6) {
          return `${param.label || param.name} is not a valid IP address`;
        }
        break;
      }
      case 'json':
        try {
          JSON.parse(value);
        } catch {
          return `${param.label || param.name} is not valid JSON`;
        }
        break;
    }
  }

  if (typeof value === 'number' && (param.type === 'number' || param.type === 'duration')) {
    if (param.min !== undefined && value < param.min) {
      return `${param.label || param.name} must be at least ${param.min}`;
    }
    if (param.max !== undefined && value > param.max) {
      return `${param.label || param.name} must be at most ${param.max}`;
    }
  }

  return null;
}

function isConditionOperandEmpty(value: ParameterValue | undefined): boolean {
  return isValueEmpty(value);
}

function isValueEmpty(value: ParameterValue | undefined): boolean {
  if (value === undefined || value === null) return true;
  if (isVariableReference(value)) return value.$var.trim() === '';
  if (typeof value === 'string') return value.trim() === '';
  if (typeof value === 'number') return Number.isNaN(value);
  return false;
}
