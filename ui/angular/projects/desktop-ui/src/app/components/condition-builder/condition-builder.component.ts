import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  ViewEncapsulation,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';

import { ActionBlockParameter, AppStrings, ComparisonExpression, ComparisonOperator, ConditionExpression, EventDefinition, LogicalExpression, ParameterValue, appendComparison, flipConnectiveAt, isComparisonExpression, isEventReference, isLogicalExpression, isStateOperator, isVariableReference, removeOperand, replaceOperand, resolveEventPayloadParameter, soleVariableToken, variableTokenLabel, variableTokenText } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, OverlayPanelComponent } from '@shared';
import type { Variable, VariableScope, VariableType } from '@macro-deck/runtime';
import { VariablePickerComponent } from '../variable-picker/variable-picker.component';
import { VariableTextInputComponent } from '../forms/variable-text-input/variable-text-input.component';
import { SelectCaretComponent } from '../forms/select-caret/select-caret.component';
import { ConditionOperandInputComponent, TYPED_CONTROL_TYPES } from './condition-operand-input/condition-operand-input.component';

const BOOLEAN_VARIABLE_OPERAND_PARAMETER: Omit<ActionBlockParameter, 'value'> = {
  name: '',
  type: 'boolean',
  label: '',
};

const EQUALITY_OPERATORS: ReadonlySet<ComparisonOperator> = new Set<ComparisonOperator>(['==', '!=']);
const NUMERIC_OPERATORS: ReadonlySet<ComparisonOperator> =
  new Set<ComparisonOperator>(['==', '!=', '>', '<', '>=', '<=']);
const STATE_OPERATORS: ReadonlySet<ComparisonOperator> =
  new Set<ComparisonOperator>(['isEmpty', 'isNotEmpty', 'isAvailable', 'isNotAvailable']);

export type LeafBadgeState =
  | { kind: 'pending' }
  | { kind: 'short-circuit' }
  | { kind: 'ok'; result: boolean; leftDisplay: string; rightDisplay: string }
  | { kind: 'error'; message: string };

export type LeafStateLookup = (leafId: string) => LeafBadgeState | undefined;

type ChangeFn = (replacement: ConditionExpression) => void;
type RemoveFn = () => void;

@Component({
  selector: 'shared-condition-builder',
  standalone: true,
  imports: [CommonModule, VariablePickerComponent, ButtonComponent,
    VariableTextInputComponent, SelectCaretComponent, OverlayPanelComponent,
    ConditionOperandInputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  templateUrl: './condition-builder.component.html',
  styleUrls: ['./condition-builder.component.scss'],
})
export class ConditionBuilderComponent {
  @Input({ required: true }) expression!: ConditionExpression;
  @Input() variables: Variable[] = [];
  @Input() previewScope: VariableScope = 'global';
  @Input() previewScopeRefId?: string;
  @Input() comparisonOperators: ReadonlyArray<{ label: string; value: ComparisonOperator }> = [];
  @Input() leafState: LeafStateLookup = () => undefined;
  @Input() eventDefinition?: EventDefinition;
  @Input() eventConfiguration?: Record<string, unknown>;

  @Output() expressionChange = new EventEmitter<ConditionExpression>();

  private readonly localization = inject(LocalizationService);

  readonly openPopover = signal<string | null>(null);

  readonly valuePlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.ConditionBuilder.ValuePlaceholder));
  readonly removeConditionLabel = computed(() =>
    this.localization.translateKey(AppStrings.ConditionBuilder.RemoveCondition));
  readonly addConditionLabel = computed(() =>
    this.localization.translateKey(AppStrings.ConditionBuilder.AddCondition));

  readonly booleanAcceptedTypes: VariableType[] = ['boolean'];

  isComparison(e: ConditionExpression): e is ComparisonExpression { return isComparisonExpression(e); }
  isLogical(e: ConditionExpression): e is LogicalExpression { return isLogicalExpression(e); }
  isVariable(value: ParameterValue | undefined): boolean {
    return isVariableReference(value) || isEventReference(value)
      || (typeof value === 'string' && soleVariableToken(value) !== null);
  }

  variableLabel(value: ParameterValue | undefined): string {
    if (isVariableReference(value)) return `vars.${value.$var}`;
    if (isEventReference(value)) return `event.${value.$event}`;
    const token = typeof value === 'string' ? soleVariableToken(value) : null;
    return token ? variableTokenLabel(token.kind, token.name) : '';
  }

  asPrimitive(value: ParameterValue | undefined): string | number | boolean | null {
    if (isVariableReference(value) || isEventReference(value)) return null;
    return (value ?? null) as string | number | boolean | null;
  }

  literalText(value: ParameterValue | undefined): string {
    if (isVariableReference(value)) return variableTokenText('variable', value.$var);
    if (isEventReference(value)) return variableTokenText('event', value.$event);
    const primitive = this.asPrimitive(value);
    return primitive === null ? '' : `${primitive}`;
  }

  isEmpty(value: ParameterValue | undefined): boolean {
    if (this.isVariable(value)) return false;
    if (value === null || value === undefined) return true;
    if (typeof value === 'string') return value.length === 0;
    return false;
  }

  // Both spellings count - the stored { $var } object and a typed lone {{ vars.x }} - or the
  // boolean-operand dropdown would stop appearing the moment a reference is typed rather than picked
  // (issue #97).
  private resolveVariable(value: ParameterValue | undefined): Variable | undefined {
    const name = this.referencedVariableName(value);
    return name === null ? undefined : this.variables.find(v => v.name === name);
  }

  private referencedVariableName(value: ParameterValue | undefined): string | null {
    if (isVariableReference(value)) return value.$var;
    if (typeof value !== 'string') return null;
    const token = soleVariableToken(value);
    return token?.kind === 'variable' ? token.name : null;
  }

  isBooleanOperand(comp: ComparisonExpression, side: 'left' | 'right'): boolean {
    const opposite = side === 'left' ? comp.right : comp.left;
    return this.resolveVariable(opposite)?.type === 'boolean';
  }

  operandParameter(comp: ComparisonExpression, side: 'left' | 'right'): Omit<ActionBlockParameter, 'value'> | undefined {
    if (this.isBooleanOperand(comp, side)) return BOOLEAN_VARIABLE_OPERAND_PARAMETER;
    return this.payloadParameterFor(comp, side);
  }

  private payloadParameterFor(
    comp: ComparisonExpression,
    side: 'left' | 'right',
  ): Omit<ActionBlockParameter, 'value'> | undefined {
    const opposite = side === 'left' ? comp.right : comp.left;
    const parameter = resolveEventPayloadParameter(this.eventDefinition, opposite);
    return parameter && TYPED_CONTROL_TYPES.has(parameter.type) ? parameter : undefined;
  }

  operatorLabel(value: ComparisonOperator): string {
    return this.comparisonOperators.find(o => o.value === value)?.label ?? value;
  }

  operatorOptionsFor(comp: ComparisonExpression): ReadonlyArray<{ label: string; value: ComparisonOperator }> {
    switch (this.narrowingParameter(comp)?.type) {
      case 'choice':
      case 'dynamic-choice':
      case 'autocomplete':
      case 'boolean':
        return this.comparisonOperators.filter(o => EQUALITY_OPERATORS.has(o.value) || STATE_OPERATORS.has(o.value));
      case 'number':
        return this.comparisonOperators.filter(o => NUMERIC_OPERATORS.has(o.value) || STATE_OPERATORS.has(o.value));
      default:
        return this.comparisonOperators;
    }
  }

  isStateOperator(op: ComparisonOperator | string | undefined): boolean {
    return isStateOperator(op);
  }

  private narrowingParameter(comp: ComparisonExpression): Omit<ActionBlockParameter, 'value'> | undefined {
    const left = this.payloadParameterFor(comp, 'right');
    const right = this.payloadParameterFor(comp, 'left');
    if (left && right) return left.type === right.type ? left : undefined;
    return left ?? right;
  }

  readonly rootChange: ChangeFn = (replacement) => this.expressionChange.emit(replacement);
  readonly noopRemove: RemoveFn = () => undefined;

  childChange(parent: LogicalExpression, index: number, propagate: ChangeFn): ChangeFn {
    return (replacement) => propagate(replaceOperand(parent, index, replacement));
  }

  childRemove(parent: LogicalExpression, index: number, propagate: ChangeFn): RemoveFn {
    return () => {
      const next = removeOperand(parent, index);
      if (next === null) return;
      propagate(next);
    };
  }

  changeComparisonField(
    comp: ComparisonExpression,
    side: 'left' | 'operator' | 'right',
    value: ParameterValue | string,
    change: ChangeFn,
  ): void {
    const next: ComparisonExpression =
      side === 'operator'
        ? { ...comp, operator: value as ComparisonOperator }
        : { ...comp, [side]: value as ParameterValue };
    change(next);
  }

  pickComparisonVariable(
    comp: ComparisonExpression,
    side: 'left' | 'right',
    variableName: string,
    change: ChangeFn,
  ): void {
    this.changeComparisonField(comp, side, { $var: variableName }, change);
  }

  pickComparisonEventParameter(
    comp: ComparisonExpression,
    side: 'left' | 'right',
    parameterName: string,
    change: ChangeFn,
  ): void {
    this.changeComparisonField(comp, side, { $event: parameterName }, change);
  }

  clearComparisonVariable(
    comp: ComparisonExpression,
    side: 'left' | 'right',
    change: ChangeFn,
  ): void {
    this.changeComparisonField(comp, side, '', change);
  }

  flipConnectiveAt(logical: LogicalExpression, position: number, change: ChangeFn): void {
    change(flipConnectiveAt(logical, position));
  }

  connectiveLabel(kind: 'and' | 'or'): string {
    return this.localization.translateKey(
      kind === 'and' ? AppStrings.ConditionBuilder.ConnectiveAnd : AppStrings.ConditionBuilder.ConnectiveOr,
    );
  }

  connectiveToggleTitle(kind: 'and' | 'or'): string {
    return this.localization.translateKey(
      kind === 'and' ? AppStrings.ConditionBuilder.SwitchToOr : AppStrings.ConditionBuilder.SwitchToAnd,
    );
  }

  clearVariableAriaLabel(side: 'left' | 'right'): string {
    return this.localization.translateKey(
      side === 'left' ? AppStrings.ConditionBuilder.ClearLeftVariable : AppStrings.ConditionBuilder.ClearRightVariable,
    );
  }

  addCondition(current: ConditionExpression, change: ChangeFn): void {
    const joinKind = isLogicalExpression(current) ? current.kind : 'and';
    change(appendComparison(current, joinKind));
  }

  popoverKey(comp: ComparisonExpression): string {
    return comp.id;
  }

  isPopoverOpen(comp: ComparisonExpression): boolean {
    return this.openPopover() === this.popoverKey(comp);
  }

  toggleOperatorPopover(comp: ComparisonExpression, event: Event): void {
    event.stopPropagation();
    const key = this.popoverKey(comp);
    this.openPopover.set(this.openPopover() === key ? null : key);
  }

  pickOperator(comp: ComparisonExpression, op: ComparisonOperator, change: ChangeFn): void {
    this.changeComparisonField(comp, 'operator', op, change);
    this.openPopover.set(null);
  }

  closeOperatorPopover(): void {
    this.openPopover.set(null);
  }

  badgeFor(leafId: string): LeafBadgeState | undefined {
    return this.leafState(leafId);
  }

  dotClass(state: LeafBadgeState | undefined): string {
    if (!state) return 'cb-dot cb-dot-pending';
    switch (state.kind) {
      case 'pending':
      case 'short-circuit': return 'cb-dot cb-dot-pending';
      case 'error':         return 'cb-dot cb-dot-error';
      case 'ok':            return state.result ? 'cb-dot cb-dot-true' : 'cb-dot cb-dot-false';
    }
  }

  dotTitle(state: LeafBadgeState | undefined, comp: ComparisonExpression): string {
    if (!state) return '';
    if (state.kind === 'ok') {
      const parts = [state.leftDisplay, this.operatorLabel(comp.operator), state.rightDisplay].filter(p => p.length > 0);
      return `${parts.join(' ')} → ${state.result}`;
    }
    if (state.kind === 'error') return state.message;
    if (state.kind === 'short-circuit') {
      return this.localization.translateKey(AppStrings.ConditionBuilder.ShortCircuited);
    }
    return this.localization.translateKey(AppStrings.ConditionBuilder.Evaluating);
  }
}
