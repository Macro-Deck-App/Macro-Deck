import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  forwardRef,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'shared-toggle-switch',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => ToggleSwitchComponent),
    multi: true,
  }],
  template: `
    <label class="ts" [class.ts-checked]="checkedState()" [class.ts-disabled]="disabledState()"
      [class.ts-busy]="busyState()">
      <input
        type="checkbox"
        class="ts-input"
        [checked]="checkedState()"
        [disabled]="disabledState()"
        [attr.aria-busy]="busyState() ? true : null"
        [attr.aria-label]="ariaLabel || null"
        (change)="toggle($event)">
      <span class="ts-track">
        <span class="ts-thumb">
          @if (busyState()) {
            <span class="ts-spinner" aria-hidden="true"></span>
          }
        </span>
      </span>
      @if (label) {
        <span class="ts-label">{{ label }}</span>
      }
    </label>
  `,
  styleUrls: ['./toggle-switch.component.scss'],
})
export class ToggleSwitchComponent implements ControlValueAccessor {
  @Input() label = '';

  @Input() ariaLabel = '';

  @Input()
  set disabled(value: boolean) {
    this.disabledState.set(value);
  }

  @Input()
  set busy(value: boolean) {
    this.busyState.set(value);
  }

  @Input()
  set checked(value: boolean) {
    this.checkedState.set(value);
  }

  @Output() changed = new EventEmitter<boolean>();

  readonly checkedState = signal(false);
  readonly disabledState = signal(false);
  readonly busyState = signal(false);

  private onChange: (value: boolean) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: boolean | null): void {
    this.checkedState.set(value === true);
  }

  registerOnChange(fn: (value: boolean) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabledState.set(disabled);
  }

  toggle(event?: Event): void {
    if (this.disabledState() || this.busyState()) {
      // The native checkbox already flipped; snap it back so it stays in
      // sync with the ignored component state.
      if (event?.target instanceof HTMLInputElement) {
        event.target.checked = this.checkedState();
      }
      return;
    }
    const next = !this.checkedState();
    this.checkedState.set(next);
    this.onChange(next);
    this.onTouched();
    this.changed.emit(next);
  }
}
