import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  Injector,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ActionParameterDef, ActionParameterType, AppStrings, LocalizedText, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, CheckboxComponent, LocalizationService, LocalizedTextPipe, TranslatePipe } from '@shared';
import { SelectComponent, SelectOption } from '../forms/select/select.component';
import { MultiSelectComponent, MultiSelectOption } from '../forms/multi-select/multi-select.component';
import { WidgetTargetPickerComponent } from '../forms/widget-target-picker/widget-target-picker.component';
import { ComboboxOption } from '../forms/combobox/combobox.component';
import { ActionOptionsService } from '../../services/action-options.service';

@Component({
  selector: 'shared-config-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    SelectComponent,
    CheckboxComponent,
    ButtonComponent,
    MultiSelectComponent,
    WidgetTargetPickerComponent,
    LocalizedTextPipe,
    TranslatePipe,
  ],
  template: `
    <div class="cf-field">
      <label class="cf-label" [attr.for]="field.name">
        {{ (field.label ?? field.name) | localizedText }}
        @if (field.required) {
          <span class="cf-required" aria-hidden="true">*</span>
        }
      </label>

      @switch (controlType) {
        @case ('secret') {
          <div class="cf-secret">
            <input
              class="cf-input cf-secret-input"
              [type]="secretRevealed ? 'text' : 'password'"
              autocomplete="new-password"
              [id]="field.name"
              [placeholder]="(field.placeholder | localizedText) || enterValuePlaceholder()"
              [ngModel]="stringValue"
              (ngModelChange)="emit($event)">
            <shared-button
              class="cf-reveal"
              variant="ghost"
              size="icon"
              [attr.aria-label]="secretRevealed ? hideValueLabel() : revealValueLabel()"
              (click)="secretRevealed = !secretRevealed">
              @if (secretRevealed) {
                <svg class="cf-reveal-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                  <path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49"/>
                  <path d="M14.084 14.158a3 3 0 0 1-4.242-4.242"/>
                  <path d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143"/>
                  <path d="m2 2 20 20"/>
                </svg>
              } @else {
                <svg class="cf-reveal-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                  <path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0"/>
                  <circle cx="12" cy="12" r="3"/>
                </svg>
              }
            </shared-button>
          </div>
        }
        @case ('choice') {
          <shared-select
            [options]="options"
            [placeholder]="(field.placeholder | localizedText) || ('macrodeck:Common.Select' | translate)"
            [ngModel]="stringValue"
            (ngModelChange)="emit($event)" />
        }
        @case ('boolean') {
          <shared-checkbox
            [label]="field.description | localizedText"
            [ngModel]="booleanValue"
            (ngModelChange)="emit($event)" />
        }
        @case ('widget-target') {
          <shared-widget-target-picker
            [value]="stringValue"
            [options]="widgetOptions()"
            [loading]="widgetOptionsLoading()"
            [hasOwnerWidget]="false"
            (valueChange)="emit($event)"
            (filterChange)="loadWidgetOptions($event)"
            (opened)="loadWidgetOptions()" />
        }
        @case ('multiselect') {
          <shared-multi-select
            [options]="multiSelectOptions"
            [value]="arrayValue"
            (valueChange)="emit($event)" />
        }
        @case ('number') {
          <input
            class="cf-input"
            type="number"
            [id]="field.name"
            [placeholder]="field.placeholder | localizedText"
            [min]="field.min ?? null"
            [max]="field.max ?? null"
            [step]="field.step ?? null"
            [ngModel]="stringValue"
            (ngModelChange)="emitNumber($event)">
        }
        @default {
          <input
            class="cf-input"
            [type]="inputType"
            [id]="field.name"
            [placeholder]="field.placeholder | localizedText"
            [maxlength]="field.maxLength ?? null"
            [ngModel]="stringValue"
            (ngModelChange)="emit($event)">
        }
      }

      @if (field.description && controlType !== 'boolean') {
        <p class="cf-description">{{ field.description | localizedText }}</p>
      }
      @if (controlType === 'secret' && secretStored) {
        <div class="cf-stored-secret">
          <span>{{ storedSecretLabel() }}</span>
          <shared-button variant="ghost" size="compact" (click)="clearSecret.emit()">
            {{ clearStoredSecretLabel() }}
          </shared-button>
        </div>
      }
      @if (error) {
        <p class="cf-error">{{ error }}</p>
      }
    </div>
  `,
  styleUrls: ['./config-field.component.scss'],
})
export class ConfigFieldComponent {
  @Input({ required: true }) field!: ActionParameterDef;
  @Input() value: unknown;
  @Input() error: string | null = null;
  @Input() secretStored = false;

  @Output() valueChange = new EventEmitter<unknown>();
  @Output() clearSecret = new EventEmitter<void>();

  secretRevealed = false;

  readonly widgetOptions = signal<ComboboxOption[]>([]);
  readonly widgetOptionsLoading = signal(false);

  // Resolved on demand rather than in a field: only a widget-target field ever needs it, and injecting
  // it eagerly would put the whole API stack behind every config field a flow renders.
  private readonly injector = inject(Injector);
  private readonly localization = inject(LocalizationService);

  protected readonly enterValuePlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.ConfigFlow.EnterValuePlaceholder));
  protected readonly hideValueLabel = computed(() =>
    this.localization.translateKey(AppStrings.ConfigFlow.HideValue));
  protected readonly revealValueLabel = computed(() =>
    this.localization.translateKey(AppStrings.ConfigFlow.RevealValue));
  protected readonly storedSecretLabel = computed(() =>
    this.localization.translateKey(AppStrings.ConfigFlow.StoredSecret));
  protected readonly clearStoredSecretLabel = computed(() =>
    this.localization.translateKey(AppStrings.ConfigFlow.ClearStoredSecret));

  get controlType(): 'secret' | 'choice' | 'boolean' | 'number' | 'multiselect' | 'widget-target' | 'text' {
    switch (this.field.type) {
      case ActionParameterType.Password:
      case ActionParameterType.Secret:
        return 'secret';
      case ActionParameterType.Choice:
        return 'choice';
      case ActionParameterType.Boolean:
        return 'boolean';
      case ActionParameterType.Number:
        return 'number';
      case ActionParameterType.MultiSelect:
        return 'multiselect';
      case ActionParameterType.WidgetTarget:
        return 'widget-target';
      default:
        return 'text';
    }
  }

  get inputType(): string {
    return this.field.type === ActionParameterType.Url ? 'url' : 'text';
  }

  get stringValue(): string {
    return this.value == null ? '' : String(this.value);
  }

  get booleanValue(): boolean {
    return this.value === true;
  }

  get options(): SelectOption[] {
    return (this.field.options ?? []).map(o => ({ value: o.value, label: this.resolveOptionLabel(o.label, o.value) }));
  }

  get multiSelectOptions(): MultiSelectOption[] {
    return (this.field.options ?? []).map(o => ({ value: o.value, label: this.resolveOptionLabel(o.label, o.value) }));
  }

  private resolveOptionLabel(label: LocalizedText | undefined, fallback: string): string {
    return resolveLocalizedText(label, this.localization) || fallback;
  }

  get arrayValue(): string[] {
    return Array.isArray(this.value) ? this.value : [];
  }

  emit(value: unknown): void {
    this.valueChange.emit(value);
  }

  emitNumber(value: string): void {
    const parsed = Number(value);
    this.valueChange.emit(value === '' || Number.isNaN(parsed) ? null : parsed);
  }

  async loadWidgetOptions(filter?: string): Promise<void> {
    const optionsSourceId = this.field.optionsSourceId;
    if (!optionsSourceId) return;

    this.widgetOptionsLoading.set(true);
    try {
      const response = await this.injector.get(ActionOptionsService).getOptions({
        integrationId: '',
        actionId: '',
        optionsSourceId,
        widgetTypes: this.field.widgetTypes,
        parameterName: this.field.name,
        filter,
      });
      this.widgetOptions.set(
        (response.options ?? []).map(o => ({ value: o.value, label: this.resolveOptionLabel(o.label, o.value) })),
      );
    } catch {
      // The picker shows "no widgets" rather than the dialog dying on an unhandled rejection.
      this.widgetOptions.set([]);
    } finally {
      this.widgetOptionsLoading.set(false);
    }
  }
}
