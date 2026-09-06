import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'shared-settings-row',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="settings-row">
      <div class="settings-row__info">
        <span class="settings-row__label">{{ label }}</span>
        @if (description) {
          <span class="settings-row__desc">{{ description }}</span>
        }
      </div>
      <div class="settings-row__control">
        <ng-content></ng-content>
      </div>
    </div>
  `,
  styleUrls: ['./settings-row.component.scss'],
})
export class SettingsRowComponent {
  @Input() label = '';
  @Input() description?: string;
}
