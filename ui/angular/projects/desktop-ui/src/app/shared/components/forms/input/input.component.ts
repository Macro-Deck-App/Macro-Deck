import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  forwardRef,
} from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR } from '@angular/forms';

export type InputType = 'text' | 'number' | 'search' | 'email' | 'password' | 'url' | 'color';

@Component({
  selector: 'shared-input',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => InputComponent),
    multi: true,
  }],
  host: {
    '[class.is-invalid]': 'invalid',
  },
  template: `
    @if (multiline) {
      <textarea
        #control
        class="control"
        [rows]="rows"
        [placeholder]="placeholder"
        [disabled]="disabled"
        [attr.id]="inputId"
        [attr.name]="inputName"
        [attr.required]="inputRequired ? '' : null"
        [attr.maxlength]="inputMaxlength"
        [attr.aria-label]="ariaLabel"
        [attr.aria-describedby]="ariaDescribedby"
        [attr.aria-invalid]="ariaInvalid"
        [ngModel]="value"
        (ngModelChange)="onInput($event)"
        (blur)="onBlur()"></textarea>
    } @else {
      <input
        #control
        class="control"
        [type]="type"
        [placeholder]="placeholder"
        [disabled]="disabled"
        [attr.id]="inputId"
        [attr.name]="inputName"
        [attr.required]="inputRequired ? '' : null"
        [attr.maxlength]="inputMaxlength"
        [attr.pattern]="inputPattern"
        [attr.min]="min"
        [attr.max]="max"
        [attr.step]="step"
        [attr.inputmode]="inputmode"
        [attr.aria-label]="ariaLabel"
        [attr.aria-describedby]="ariaDescribedby"
        [attr.aria-invalid]="ariaInvalid"
        [ngModel]="value"
        (ngModelChange)="onInput($event)"
        (blur)="onBlur()">
    }
  `,
  styleUrls: ['./input.component.scss'],
})
export class InputComponent implements ControlValueAccessor, AfterViewInit {
  @Input() type: InputType = 'text';
  @Input() placeholder = '';
  @Input() disabled = false;
  @Input() invalid = false;
  @Input() multiline = false;
  @Input() rows = 3;
  @Input() min: number | null | undefined = null;
  @Input() max: number | null | undefined = null;
  @Input() step: number | string | null | undefined = null;
  @Input() autofocus = false;
  @Input() ariaLabel: string | null = null;
  @Input() inputId: string | null = null;
  @Input() inputName: string | null = null;
  @Input() inputRequired = false;
  @Input() inputMaxlength: number | null = null;
  @Input() inputPattern: string | null = null;
  @Input() ariaDescribedby: string | null = null;
  @Input() ariaInvalid: 'true' | null = null;
  @Output() blurred = new EventEmitter<void>();

  @ViewChild('control') private controlRef?: ElementRef<HTMLInputElement | HTMLTextAreaElement>;

  value: string | number = '';

  onChange: (value: string | number) => void = () => {};
  onTouched: () => void = () => {};

  get inputmode(): string | null {
    return this.type === 'number' ? 'decimal' : null;
  }

  ngAfterViewInit(): void {
    if (this.autofocus) {
      this.controlRef?.nativeElement.focus();
    }
  }

  writeValue(value: string | number | null): void {
    this.value = value ?? '';
  }

  registerOnChange(fn: (value: string | number) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled = disabled;
  }

  onInput(value: string | number): void {
	this.value = value;
	this.onChange(value);
  }

  onBlur(): void {
    this.onTouched();
    this.blurred.emit();
  }
}
