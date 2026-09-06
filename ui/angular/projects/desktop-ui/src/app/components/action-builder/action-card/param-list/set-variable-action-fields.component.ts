import { ChangeDetectionStrategy, Component, Input, OnChanges, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ActionBlock, ActionBlockParameter, AppStrings, isEventReference, isVariableReference } from '@macro-deck/runtime';
import { InputComponent, LocalizationService, TranslatePipe } from '@shared';
import type { VariableType } from '@macro-deck/runtime';
import { ParamInputComponent } from '../../../forms/param-input/param-input.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { VariablePickerComponent } from '../../../variable-picker/variable-picker.component';
import { ActionFlowStore } from '../../services/action-flow.store';
import { SET_VARIABLE_DEFAULT_OPERATION, isOperationAllowed, operationOptionsFor } from '../../../../domain/set-variable-operations.util';

@Component({
  selector: 'shared-set-variable-action-fields',
  standalone: true,
  imports: [FormsModule, InputComponent, ParamInputComponent, SelectComponent, VariablePickerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-group form-group--dense">
      <label>{{ 'macrodeck.app:ActionBuilder.SetVariable.OperationField' | translate }}</label>
      <shared-select
        [options]="operationOptions"
        [ngModel]="operation"
        (ngModelChange)="onOperationChange($event)" />
    </div>

    @if (showsValue) {
      <div class="form-group form-group--dense">
        <label>{{ 'macrodeck.app:ActionBuilder.SetVariable.ValueField' | translate }}</label>
        @if (isReference) {
          <span class="param-variable-chip">
            <span class="chip-text">{{ referenceLabel }}</span>
            <button type="button" class="chip-clear" (click)="clearValue()" [attr.aria-label]="'macrodeck.app:ActionBuilder.Param.ClearVariable' | translate">
              <span class="icon icon-x icon-xs"></span>
            </button>
          </span>
        } @else {
          <div class="value-row">
            @switch (variableType) {
              @case ('boolean') {
                <shared-select
                  [placeholder]="'macrodeck.app:ActionBuilder.SetVariable.ChooseTrueOrFalse' | translate"
                  [options]="booleanOptions"
                  [ngModel]="stringValue"
                  (ngModelChange)="write($event)" />
              }
              @case ('numeric') {
                <shared-input
                  type="number"
                  [step]="numberStep"
                  [ngModel]="stringValue"
                  (ngModelChange)="write($event === null ? '' : String($event))" />
              }
              @default {
                <shared-param-input
                  [value]="stringValue"
                  [variables]="store.pickerVariables()"
                  [scope]="store.previewScope()"
                  [scopeRefId]="store.previewScopeRefId()"
                  (valueChange)="write($event)" />
              }
            }
            @if (showsVariablePicker) {
              <shared-variable-picker
                [variables]="store.pickerVariables()"
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

  ngOnChanges(): void {
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

  protected readonly String = String;

  protected get variableType(): VariableType | undefined {
    const name = this.parameterValue('variable');
    if (typeof name !== 'string' || name === '') return undefined;
    return this.store.variables().find(variable => variable.name === name)?.type;
  }

  protected get operationOptions(): SelectOption[] {
    return operationOptionsFor(this.variableType);
  }

  protected get operation(): string {
    const value = this.parameterValue('operation');
    return typeof value === 'string' && value !== '' ? value : SET_VARIABLE_DEFAULT_OPERATION;
  }

  protected get showsValue(): boolean {
    return this.operation !== 'toggle';
  }

  protected get showsVariablePicker(): boolean {
    return this.variableType === 'numeric' || this.variableType === 'boolean';
  }

  protected get acceptedVariableTypes(): VariableType[] | undefined {
    return this.variableType ? [this.variableType] : undefined;
  }

  protected get numberStep(): number | undefined {
    const decimals = this.store.variables()
      .find(variable => variable.name === this.parameterValue('variable'))?.decimalPlaces;
    return decimals === undefined || decimals <= 0 ? undefined : Number('1e-' + decimals);
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
    return typeof value === 'string' || typeof value === 'number' ? String(value) : '';
  }

  protected onOperationChange(operation: string): void {
    this.store.updateParam(this.block.id, 'operation', operation);
  }

  protected write(value: string): void {
    this.store.updateParam(this.block.id, 'value', value);
  }

  protected pickVariable(name: string): void {
    this.store.updateParam(this.block.id, 'value', { $var: name });
  }

  protected pickEventParameter(name: string): void {
    this.store.updateParam(this.block.id, 'value', { $event: name });
  }

  protected clearValue(): void {
    this.store.updateParam(this.block.id, 'value', '');
  }

  private parameterValue(name: string): ActionBlockParameter['value'] | undefined {
    return this.block.parameters?.find(parameter => parameter.name === name)?.value;
  }
}
