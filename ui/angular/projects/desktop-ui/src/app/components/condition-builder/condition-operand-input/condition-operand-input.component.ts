import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ActionBlockParameter, AppStrings, ParameterValue } from '@macro-deck/runtime';
import { InputComponent, LocalizationService } from '@shared';
import type { Variable } from '@macro-deck/runtime';
import { ActionOptionsService } from '../../../services/action-options.service';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { ComboboxComponent, ComboboxOption } from '../../forms/combobox/combobox.component';
import { DateTimePickerComponent } from '../../forms/datetime-picker/datetime-picker.component';
import { VariableTextInputComponent } from '../../forms/variable-text-input/variable-text-input.component';

export type TypedControlType = 'choice' | 'dynamic-choice' | 'autocomplete' | 'boolean' | 'number' | 'datetime';

export const TYPED_CONTROL_TYPES: ReadonlySet<string> = new Set<TypedControlType>([
  'choice', 'dynamic-choice', 'autocomplete', 'boolean', 'number', 'datetime',
]);

@Component({
  selector: 'shared-condition-operand-input',
  standalone: true,
  imports: [FormsModule, SelectComponent, ComboboxComponent, InputComponent, DateTimePickerComponent, VariableTextInputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './condition-operand-input.component.html',
  styleUrls: ['./condition-operand-input.component.scss'],
})
export class ConditionOperandInputComponent implements OnInit, OnChanges, OnDestroy {
  @Input() value: string | number | boolean | null | undefined;
  @Input() parameter?: Omit<ActionBlockParameter, 'value'>;
  @Input() placeholder = '';
  @Input() variables: Variable[] = [];
  @Input() eventId?: string;
  @Input() currentParameters?: Record<string, unknown>;

  @Output() readonly valueChange = new EventEmitter<ParameterValue>();

  private readonly optionsService = inject(ActionOptionsService);
  private readonly localization = inject(LocalizationService);

  readonly dynOptions = signal<ComboboxOption[] | null>(null);
  readonly dynLoading = signal(false);

  private filterDebounce: ReturnType<typeof setTimeout> | null = null;

  readonly booleanOptions = computed<SelectOption[]>(() => [
    { value: 'true', label: this.localization.translateKey(AppStrings.ConditionBuilder.True) },
    { value: 'false', label: this.localization.translateKey(AppStrings.ConditionBuilder.False) },
  ]);

  get controlType(): TypedControlType | undefined {
    const type = this.parameter?.type;
    return type && TYPED_CONTROL_TYPES.has(type) ? (type as TypedControlType) : undefined;
  }

  get stringValue(): string {
    return this.value === null || this.value === undefined ? '' : `${this.value}`;
  }

  get numberValue(): number | null {
    return typeof this.value === 'number' ? this.value : null;
  }

  get booleanValue(): string | null {
    if (this.value === true || this.value === 'true') return 'true';
    if (this.value === false || this.value === 'false') return 'false';
    return null;
  }

  get choiceOptions(): SelectOption[] {
    return (this.parameter?.options ?? []).map(o => ({ value: `${o.value}`, label: o.label }));
  }

  get isDynamic(): boolean {
    return !!this.parameter?.dynamicOptions || !!this.parameter?.optionsSourceId;
  }

  get effectiveOptions(): ComboboxOption[] {
    if (this.isDynamic) {
      return this.dynOptions() ?? (this.stringValue ? [{ value: this.stringValue, label: this.stringValue }] : []);
    }
    return this.choiceOptions;
  }

  get displayLabel(): string {
    return this.dynOptions()?.find(o => o.value === this.stringValue)?.label ?? '';
  }

  ngOnInit(): void {
    if (this.isDynamic && this.stringValue) {
      this.loadDynamicOptions();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['parameter'];
    if (!change || change.isFirstChange()) return;

    const previous = change.previousValue as Omit<ActionBlockParameter, 'value'> | undefined;
    if (previous?.name === this.parameter?.name && previous?.optionsSourceId === this.parameter?.optionsSourceId) {
      return;
    }

    this.dynOptions.set(null);
    if (this.isDynamic && this.stringValue) {
      this.loadDynamicOptions();
    }
  }

  ngOnDestroy(): void {
    if (this.filterDebounce) clearTimeout(this.filterDebounce);
  }

  emit(value: ParameterValue): void {
    this.valueChange.emit(value);
  }

  emitNumber(value: string | number): void {
    if (value === '' || value == null) {
      this.emit('');
      return;
    }
    const parsed = Number(value);
    if (Number.isFinite(parsed)) this.emit(parsed);
  }

  loadDynamicOptions(filter?: string): void {
    const parameter = this.parameter;
    if (!parameter) return;

    this.dynLoading.set(true);
    void this.optionsService
      .loadLabeledOptions({
        integrationId: '',
        actionId: '',
        eventId: this.eventId,
        eventParameterKind: 'payload',
        optionsSourceId: parameter.optionsSourceId,
        parameterName: parameter.name,
        filter: filter || undefined,
        currentParameters: this.currentParameters,
      })
      .then(({ options }) => this.dynOptions.set(options))
      .finally(() => this.dynLoading.set(false));
  }

  onDynamicFilterChange(filter: string): void {
    if (this.filterDebounce) clearTimeout(this.filterDebounce);
    this.filterDebounce = setTimeout(() => this.loadDynamicOptions(filter), 250);
  }
}
