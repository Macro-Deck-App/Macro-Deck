import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { WIDGET_TARGET_SELF } from '@macro-deck/runtime';
import { TranslatePipe } from '@shared';
import { ComboboxComponent, ComboboxOption } from '../combobox/combobox.component';

export { WIDGET_TARGET_SELF };

@Component({
  selector: 'shared-widget-target-picker',
  standalone: true,
  imports: [ComboboxComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-target-picker.component.html',
  styleUrls: ['./widget-target-picker.component.scss'],
})
export class WidgetTargetPickerComponent {
  @Input() value = '';
  @Input() options: ComboboxOption[] = [];
  @Input() loading = false;

  @Input() hasOwnerWidget = false;

  @Input() valueLabel?: string;

  @Output() valueChange = new EventEmitter<string>();
  @Output() filterChange = new EventEmitter<string>();
  @Output() opened = new EventEmitter<void>();

  get isSelf(): boolean {
    return this.hasOwnerWidget && this.value === WIDGET_TARGET_SELF;
  }

  get pickerValue(): string {
    return this.value === WIDGET_TARGET_SELF ? '' : this.value;
  }

  get displayLabel(): string {
    return this.valueLabel ?? '';
  }

  get isMissingWidget(): boolean {
    if (!this.pickerValue || this.loading || this.options.length === 0) {
      return false;
    }
    return !this.options.some(option => option.value === this.pickerValue);
  }

  onClearSelf(): void {
    this.valueChange.emit('');
  }

  onUseThisWidget(): void {
    this.valueChange.emit(WIDGET_TARGET_SELF);
  }
}
