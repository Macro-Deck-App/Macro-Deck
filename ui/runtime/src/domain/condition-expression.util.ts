import {
  ComparisonExpression,
  ComparisonOperator,
  ConditionExpression,
  LogicalExpression,
  isComparisonExpression,
  isLogicalExpression,
} from './action-builder.interface';

function newId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID();
  }
  return `expr-${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
}

export function createEmptyComparison(): ComparisonExpression {
  return {
    kind: 'compare',
    id: newId(),
    left: '',
    operator: '==',
    right: '',
  };
}

export function createComparison(
  left: ComparisonExpression['left'],
  operator: ComparisonOperator,
  right: ComparisonExpression['right'],
): ComparisonExpression {
  return { kind: 'compare', id: newId(), left, operator, right };
}

export function createLogical(
  kind: 'and' | 'or',
  operands: ConditionExpression[],
): LogicalExpression {
  return { kind, id: newId(), operands };
}

export function* iterateComparisons(
  expr: ConditionExpression,
): Generator<ComparisonExpression> {
  if (isComparisonExpression(expr)) {
    yield expr;
    return;
  }
  for (const operand of expr.operands) yield* iterateComparisons(operand);
}

export function collectComparisons(expr: ConditionExpression): ComparisonExpression[] {
  return Array.from(iterateComparisons(expr));
}

export function appendComparison(
  expr: ConditionExpression,
  kind: 'and' | 'or',
): LogicalExpression {
  const fresh = createEmptyComparison();
  if (isLogicalExpression(expr) && expr.kind === kind) {
    return { ...expr, operands: [...expr.operands, fresh] };
  }
  return createLogical(kind, [expr, fresh]);
}

export function appendGroup(
  expr: ConditionExpression,
  kind: 'and' | 'or',
): LogicalExpression {
  const innerGroup = createLogical(
    kind === 'and' ? 'or' : 'and',
    [createEmptyComparison(), createEmptyComparison()],
  );
  if (isLogicalExpression(expr) && expr.kind === kind) {
    return { ...expr, operands: [...expr.operands, innerGroup] };
  }
  return createLogical(kind, [expr, innerGroup]);
}

export function removeOperand(
  logical: LogicalExpression,
  index: number,
): ConditionExpression | null {
  if (index < 0 || index >= logical.operands.length) return null;
  const next = logical.operands.filter((_, i) => i !== index);
  if (next.length === 0) return null;
  if (next.length === 1) return next[0];
  return { ...logical, operands: next };
}

export function replaceOperand(
  logical: LogicalExpression,
  index: number,
  replacement: ConditionExpression,
): LogicalExpression {
  return {
    ...logical,
    operands: logical.operands.map((o, i) => (i === index ? replacement : o)),
  };
}

export function flipConnectiveAt(
  group: LogicalExpression,
  position: number,
): ConditionExpression {
  if (position < 0 || position >= group.operands.length - 1) return group;
  const flipped: 'and' | 'or' = group.kind === 'and' ? 'or' : 'and';
  const subGroup = createLogical(flipped, [
    group.operands[position],
    group.operands[position + 1],
  ]);
  const nextOps: ConditionExpression[] = [
    ...group.operands.slice(0, position),
    subGroup,
    ...group.operands.slice(position + 2),
  ];
  const restructured: ConditionExpression =
    nextOps.length === 1 ? nextOps[0] : { ...group, operands: nextOps };
  return flatten(restructured);
}

export function flatten(expr: ConditionExpression): ConditionExpression {
  if (isComparisonExpression(expr)) return expr;

  const flattened: ConditionExpression[] = [];
  for (const operand of expr.operands) {
    const child = flatten(operand);
    if (isLogicalExpression(child) && child.kind === expr.kind) {
      flattened.push(...child.operands);
    } else {
      flattened.push(child);
    }
  }

  if (flattened.length === 1) return flattened[0];
  return { ...expr, operands: flattened };
}
