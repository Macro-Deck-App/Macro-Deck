import {
  ChangeDetectionStrategy,
  Component,
  Input,
  forwardRef,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'shared-checkbox',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => CheckboxComponent),
    multi: true,
  }],
  template: `
    <label class="cb-wrap">
      <input
        class="cb-box"
        type="checkbox"
        [checked]="checked()"
        [disabled]="disabledState()"
        (change)="onToggle($event)"
        (blur)="onTouched()">
      @if (label) {
        <span class="cb-label">{{ label }}</span>
      }
    </label>
  `,
  styleUrls: ['./checkbox.component.scss'],
})
export class CheckboxComponent implements ControlValueAccessor {
  @Input() label = '';

  @Input()
  set disabled(value: boolean) {
    this.disabledState.set(value);
  }

  readonly checked = signal(false);
  readonly disabledState = signal(false);

  onChange: (value: boolean) => void = () => {};
  onTouched: () => void = () => {};

  writeValue(value: boolean | null): void {
    this.checked.set(value === true);
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

  onToggle(event: Event): void {
    this.checked.set((event.target as HTMLInputElement).checked);
    this.onChange(this.checked());
  }
}
