import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  forwardRef,
  signal,
} from '@angular/core';
import {
  ControlValueAccessor,
  FormsModule,
  NG_VALUE_ACCESSOR,
} from '@angular/forms';

import { InputComponent } from '@shared';
import type { Variable, VariableScope, VariableType } from '@macro-deck/runtime';
import { TemplateBuilderComponent } from '../../template-builder/template-builder.component';
import { VariableTextInputComponent } from '../variable-text-input/variable-text-input.component';

@Component({
  selector: 'shared-param-input',
  standalone: true,
  imports: [FormsModule, TemplateBuilderComponent, InputComponent, VariableTextInputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => ParamInputComponent),
      multi: true,
    },
  ],
  template: `
    <div class="pi-shell" [class.pi-multiline]="multiline" [class.pi-has-liquid]="showLiquid">
      @if (usesVariableEditor) {
        <shared-variable-text-input
          class="pi-field"
          [value]="valueState()"
          [multiline]="multiline"
          [rows]="rows"
          [placeholder]="placeholder"
          [ariaLabel]="ariaLabel ?? ''"
          [disabled]="disabledState()"
          [variables]="variables"
          [acceptedTypes]="acceptedVariableTypes"
          (valueChange)="onInput($event)"
          (touched)="onBlur()" />
      } @else if (multiline) {
        <shared-input
          class="pi-field"
          [multiline]="true"
          [rows]="rows"
          [placeholder]="placeholder"
          [ariaLabel]="ariaLabel"
          [disabled]="disabledState()"
          [ngModel]="valueState()"
          (ngModelChange)="onInput($event)"
          (blurred)="onBlur()" />
      } @else {
        <shared-input
          class="pi-field"
          [type]="type"
          [placeholder]="placeholder"
          [ariaLabel]="ariaLabel"
          [disabled]="disabledState()"
          [ngModel]="valueState()"
          (ngModelChange)="onInput($event)"
          (blurred)="onBlur()" />
      }

      @if (showLiquid) {
        <shared-template-builder
          class="pi-liquid"
          [variables]="variables"
          [scope]="scope"
          [scopeRefId]="scopeRefId"
          [disabled]="disabledState()"
          [value]="valueState()"
          (apply$)="onTemplateApply($event)">
        </shared-template-builder>
      }
    </div>
  `,
  styleUrls: ['./param-input.component.scss'],
})
export class ParamInputComponent implements ControlValueAccessor {
  @Input() set value(value: string) {
    this.valueState.set(value ?? '');
  }
  get value(): string {
    return this.valueState();
  }

  @Input() placeholder = '';
  @Input() ariaLabel: string | null = null;
  @Input() type: 'text' | 'number' = 'text';
  @Input() multiline = false;
  @Input() rows = 4;

  @Input() variables: Variable[] = [];
  @Input() acceptedVariableTypes?: VariableType[];

  @Input() showLiquid = true;
  @Input() allowReferences = true;
  @Input() scope: VariableScope = 'global';
  @Input() scopeRefId?: string;

  @Output() valueChange = new EventEmitter<string>();
  @Output() valueBlur = new EventEmitter<string>();

  readonly valueState = signal('');
  readonly disabledState = signal(false);

  private onChange: (v: string) => void = () => {};
  onTouched: () => void = () => {};

  get usesVariableEditor(): boolean {
    return this.allowReferences && this.type === 'text';
  }

  onInput(v: string): void {
    this.valueState.set(v);
    this.onChange(v);
    this.valueChange.emit(v);
  }

  onTemplateApply(template: string): void {
    this.onInput(template);
  }

  onBlur(): void {
    this.onTouched();
    this.valueBlur.emit(this.valueState());
  }

  writeValue(v: string): void { this.valueState.set(v ?? ''); }
  registerOnChange(fn: (v: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(disabled: boolean): void { this.disabledState.set(disabled); }
}
