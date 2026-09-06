import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'shared-datetime-picker',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <input
      class="dt-input"
      type="datetime-local"
      [ngModel]="localValue"
      (ngModelChange)="onInput($event)">
  `,
  styleUrls: ['./datetime-picker.component.scss'],
})
export class DateTimePickerComponent {
  localValue = '';

  @Input()
  set value(iso: string | null) {
    if (!iso) {
      this.localValue = '';
      return;
    }
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      this.localValue = '';
      return;
    }
    const pad = (n: number) => `${n}`.padStart(2, '0');
    this.localValue = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
      + `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  @Output() valueChange = new EventEmitter<string>();

  onInput(local: string): void {
    this.localValue = local;
    if (!local) {
      this.valueChange.emit('');
      return;
    }
    const date = new Date(local);
    this.valueChange.emit(Number.isNaN(date.getTime()) ? '' : date.toISOString());
  }
}
