import { ActionBlock, ActionBlockParameter, AppStrings, ConditionExpression, ParameterValue, formatCombo, isComparisonExpression, isHotkeyValue, isVariableReference } from '@macro-deck/runtime';

type Translator = (key: string, args?: Record<string, unknown>) => string;

function displayValue(value: ParameterValue, t: Translator): string {
  if (isVariableReference(value)) return `{{ vars.${value.$var} }}`;
  if (value === null || value === undefined || value === '') return '-';
  if (isHotkeyValue(value)) return formatCombo(value.modifiers, value.key) || '-';
  if (isKeyboardSequence(value)) {
    return t(AppStrings.ActionBuilder.Summary.StepCount, { count: value.steps.length });
  }
  if (typeof value === 'object') return '…';
  return String(value);
}

function isKeyboardSequence(value: ParameterValue): value is { steps: unknown[] } {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    Array.isArray((value as { steps?: unknown }).steps)
  );
}

export function summarizeParameters(params: ActionBlockParameter[] | undefined, t: Translator): string {
  if (!params || params.length === 0) return '';
  return params.map(p => `${p.label}: ${paramValueSummary(p, t)}`).join(' · ');
}

function paramValueSummary(p: ActionBlockParameter, t: Translator): string {
  if (p.valueLabel && typeof p.value === 'string' && p.value !== '') {
    return p.valueLabel;
  }
  return displayValue(p.value, t);
}

export function countActions(blocks: ActionBlock[] | undefined): number {
  if (!blocks) return 0;
  let count = blocks.length;
  for (const block of blocks) {
    count += countActions(block.children);
    for (const branch of block.branches ?? []) {
      count += countActions(branch.children);
    }
  }
  return count;
}

export function actionCountLabel(count: number, t: Translator): string {
  return t(AppStrings.ActionBuilder.Summary.ActionCount, { count });
}

export function describeExpression(expr: ConditionExpression | undefined, t: Translator): string {
  if (!expr) return '';
  if (isComparisonExpression(expr)) {
    return `${displayValue(expr.left, t)} ${expr.operator} ${displayValue(expr.right, t)}`;
  }
  const parts = expr.operands.slice(0, 2).map(operand => describeExpression(operand, t));
  const joined = parts.join(expr.kind === 'and'
    ? ` ${t(AppStrings.ActionBuilder.Summary.And)} `
    : ` ${t(AppStrings.ActionBuilder.Summary.Or)} `);
  return expr.operands.length > 2 ? `${joined} …` : joined;
}
