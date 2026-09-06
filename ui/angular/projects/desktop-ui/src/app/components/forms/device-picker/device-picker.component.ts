import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { DeviceService } from '../../../services/device.service';
import { SelectComponent, SelectOption } from '../select/select.component';

@Component({
  selector: 'shared-device-picker',
  standalone: true,
  imports: [FormsModule, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-select
      [options]="options()"
      [placeholder]="placeholder()"
      [disabled]="disabled()"
      [ngModel]="value()"
      (ngModelChange)="valueChange.emit($event)" />
  `,
})
export class DevicePickerComponent {
  private readonly deviceService = inject(DeviceService);

  readonly value = input('');
  readonly placeholder = input('');
  readonly disabled = input(false);

  readonly valueChange = output<string>();

  protected readonly options = computed<SelectOption[]>(() =>
    [...this.deviceService.devices()]
      .sort((a, b) => a.name.localeCompare(b.name))
      .map(device => ({ value: device.id, label: device.name })));

  constructor() {
    void this.deviceService.load();
  }
}
