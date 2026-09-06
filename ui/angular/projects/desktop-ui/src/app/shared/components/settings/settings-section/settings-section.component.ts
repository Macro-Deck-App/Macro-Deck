import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'shared-settings-section',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="settings-section">
      <header class="settings-section__header">
        <div class="settings-section__heading-row">
          <h2 class="settings-section__title">{{ heading }}</h2>
          <ng-content select="[settingsSectionAction]"></ng-content>
        </div>
        @if (description) {
          <p class="settings-section__desc">{{ description }}</p>
        }
      </header>
      <div class="settings-section__body">
        <ng-content></ng-content>
      </div>
    </section>
  `,
  styleUrls: ['./settings-section.component.scss'],
})
export class SettingsSectionComponent {
  @Input() heading = '';
  @Input() description?: string;
}
