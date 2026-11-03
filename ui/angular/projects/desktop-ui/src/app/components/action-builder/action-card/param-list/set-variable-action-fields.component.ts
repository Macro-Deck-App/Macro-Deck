import { ChangeDetectionStrategy, Component, Input, OnChanges, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ActionBlock, ActionBlockParameter, AppStrings, isEventReference, isVariableReference, parseColor } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import type { VariableType } from '@macro-deck/runtime';
import { ParamInputComponent } from '../../../forms/param-input/param-input.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { ColorPickerComponent } from '../../../forms/color-picker/color-picker.component';
import { TemplateBuilderComponent } from '../../../template-builder/template-builder.component';
import { VariablePickerComponent } from '../../../variable-picker/variable-picker.component';
import { ActionFlowStore } from '../../services/action-flow.store';
import { SET_VARIABLE_DEFAULT_OPERATION, isOperationAllowed, operationOptionsFor } from '../../../../domain/set-variable-operations.util';

// The host reads a comma as a thousands separator, so a typed 1,5 would store 15.
const DECIMAL_COMMA = /^(-?\d+),(\d+)$/;

@Component({
  selector: 'shared-set-variable-action-fields',
  standalone: true,
  imports: [FormsModule, ColorPickerComponent, ParamInputComponent, SelectComponent, TemplateBuilderComponent, VariablePickerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-group form-group--dense">
      <label>{{ 'macrodeck.app:ActionBuilder.SetVariable.OperationField' | translate }}</label>
      <shared-select
        [ariaLabel]="'macrodeck.app:ActionBuilder.SetVariable.OperationField' | translate"
        [options]="operationOptions"
        [ngModel]="operation"
        (ngModelChange)="onOperationChange($event)" />
    </div>

    @if (showsValue) {
      <div class="form-group form-group--dense">
        <label>{{ 'macrodeck.app:ActionBuilder.SetVariable.ValueField' | translate }}</label>
        @if (isReference) {
          <span class="param-variable-chip">
            <span class="chip-text" [title]="referenceLabel">{{ referenceLabel }}</span>
            <button type="button" class="chip-clear" (click)="clearValue()" [attr.aria-label]="'macrodeck.app:ActionBuilder.Param.ClearVariable' | translate">
              <span class="icon icon-x icon-xs"></span>
            </button>
          </span>
        } @else {
          <div class="value-row">
            @if (showsFixedControl) {
              @if (variableType === 'boolean') {
                <shared-select
                  [ariaLabel]="'macrodeck.app:ActionBuilder.SetVariable.ValueField' | translate"
                  [placeholder]="'macrodeck.app:ActionBuilder.SetVariable.ChooseTrueOrFalse' | translate"
                  [options]="booleanOptions"
                  [ngModel]="stringValue"
                  (ngModelChange)="write($event)" />
              } @else {
                <shared-color-picker
                  [ariaLabel]="'macrodeck.app:ActionBuilder.SetVariable.ValueField' | translate"
                  [allowAlpha]="true"
                  [ngModel]="stringValue"
                  (ngModelChange)="write($event)" />
              }
              <shared-template-builder
                [variables]="store.pickerVariables()"
                [scope]="store.previewScope()"
                [scopeRefId]="store.previewScopeRefId()"
                [resultType]="variableType"
                [value]="stringValue"
                (apply$)="write($event)" />
            } @else {
              <shared-param-input
                [ariaLabel]="'macrodeck.app:ActionBuilder.SetVariable.ValueField' | translate"
                [value]="stringValue"
                [variables]="store.pickerVariables()"
                [resultType]="showsVariablePicker ? variableType : undefined"
                [scope]="store.previewScope()"
                [scopeRefId]="store.previewScopeRefId()"
                (valueChange)="writeFromField($event)"
                (valueBlur)="leaveField($event)" />
            }
            @if (showsVariablePicker) {
              <shared-variable-picker
                [variables]="store.pickerVariables()"
                [scope]="store.previewScope()"
                [acceptedTypes]="acceptedVariableTypes"
                (pick)="pickVariable($event)"
                (pickEventParameter)="pickEventParameter($event)" />
            }
          </div>
        }
      </div>
    }
  `,
  styleUrls: ['./set-variable-action-fields.component.scss'],
})
export class SetVariableActionFieldsComponent implements OnChanges {
  @Input({ required: true }) block!: ActionBlock;

  protected readonly store = inject(ActionFlowStore);

  private readonly fieldInUse = signal(false);

  private fieldVariable: unknown;

  ngOnChanges(): void {
    const variable = this.parameterValue('variable');
    if (variable !== this.fieldVariable) {
      this.fieldVariable = variable;
      this.fieldInUse.set(false);
    }

    const type = this.variableType;
    if (type && !isOperationAllowed(this.operation, type)) {
      this.store.updateParam(this.block.id, 'operation', SET_VARIABLE_DEFAULT_OPERATION);
    }
  }

  private readonly localization = inject(LocalizationService);

  protected get booleanOptions(): SelectOption[] {
    return [
      { value: 'true', label: this.localization.translateKey(AppStrings.ActionBuilder.SetVariable.True) },
      { value: 'false', label: this.localization.translateKey(AppStrings.ActionBuilder.SetVariable.False) },
    ];
  }

  protected get variableType(): VariableType | undefined {
    const name = this.parameterValue('variable');
    if (typeof name !== 'string' || name === '') return undefined;
    return this.store.variables().find(variable => variable.name === name)?.type;
  }

  protected get operationOptions(): SelectOption[] {
    return operationOptionsFor(this.variableType, key => this.localization.translateKey(key));
  }

  protected get operation(): string {
    const value = this.parameterValue('operation');
    return typeof value === 'string' && value !== '' ? value : SET_VARIABLE_DEFAULT_OPERATION;
  }

  protected get showsValue(): boolean {
    return this.operation !== 'toggle';
  }

  protected get showsVariablePicker(): boolean {
    return this.variableType === 'numeric' || this.variableType === 'boolean' || this.variableType === 'color';
  }

  protected get acceptedVariableTypes(): VariableType[] | undefined {
    return this.variableType ? [this.variableType] : undefined;
  }

  protected get showsFixedControl(): boolean {
    const value = this.stringValue;
    if (this.fieldInUse()) return false;
    switch (this.variableType) {
      case 'boolean': return value === '' || value === 'true' || value === 'false';
      case 'color': return value === '' || parseColor(value) !== null;
      default: return false;
    }
  }

  protected get isReference(): boolean {
    const value = this.parameterValue('value');
    return isVariableReference(value) || isEventReference(value);
  }

  protected get referenceLabel(): string {
    const value = this.parameterValue('value');
    if (isVariableReference(value)) return `{{ vars.${value.$var} }}`;
    if (isEventReference(value)) return `{{ event.${value.$event} }}`;
    return '';
  }

  protected get stringValue(): string {
    const value = this.parameterValue('value');
    return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean' ? String(value) : '';
  }

  protected onOperationChange(operation: string): void {
    this.store.updateParam(this.block.id, 'operation', operation);
  }

  protected write(value: string): void {
    this.store.updateParam(this.block.id, 'value',
      this.variableType === 'numeric' ? value.replace(DECIMAL_COMMA, '$1.$2') : value);
  }

  protected writeFromField(value: string): void {
    // An emptied contenteditable reports a line break, which no number, boolean or color can hold.
    const written = this.showsVariablePicker && value.trim() === '' ? '' : value;
    this.fieldInUse.set(true);
    this.write(written);
  }

  protected leaveField(value: string): void {
    if (value.trim() === '') {
      this.fieldInUse.set(false);
    }
  }

  protected pickVariable(name: string): void {
    this.fieldInUse.set(false);
    this.store.updateParam(this.block.id, 'value', { $var: name });
  }

  protected pickEventParameter(name: string): void {
    this.fieldInUse.set(false);
    this.store.updateParam(this.block.id, 'value', { $event: name });
  }

  protected clearValue(): void {
    this.store.updateParam(this.block.id, 'value', '');
  }

  private parameterValue(name: string): ActionBlockParameter['value'] | undefined {
    return this.block.parameters?.find(parameter => parameter.name === name)?.value;
  }
}
