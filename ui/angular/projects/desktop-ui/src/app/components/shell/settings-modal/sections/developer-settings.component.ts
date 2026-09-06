import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiService, SettingsRowComponent, SettingsSectionComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';

@Component({
  selector: 'app-developer-settings',
  standalone: true,
  imports: [
    ConfirmationModalComponent,
    SettingsSectionComponent,
    SettingsRowComponent,
    ToggleSwitchComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './developer-settings.component.html',
  styleUrls: ['./developer-settings.component.scss'],
})
export class DeveloperSettingsComponent {
  private readonly api = inject(ApiService);

  readonly enabled = signal(false);
  readonly busy = signal(false);
  readonly loaded = signal(false);
  readonly enableConfirmOpen = signal(false);

  constructor() {
    void this.load();
  }

  requestSetEnabled(value: boolean): void {
    if (this.busy() || !this.loaded()) {
      return;
    }
    if (value) {
      this.enableConfirmOpen.set(true);
      return;
    }
    void this.apply(false);
  }

  cancelEnable(): void {
    this.enableConfirmOpen.set(false);
  }

  confirmEnable(): void {
    this.enableConfirmOpen.set(false);
    void this.apply(true);
  }

  private async apply(value: boolean): Promise<void> {
    const previous = this.enabled();
    this.busy.set(true);
    this.enabled.set(value);
    try {
      const applied = await this.api.updateDeveloperSettings({ enabled: value });
      this.enabled.set(applied.enabled);
    } catch {
      this.enabled.set(previous);
      // Roll back from the host's own state rather than trusting the pre-click value alone - see
      // logging-settings.component.ts for why a plain revert can race a concurrent external change.
      await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const settings = await this.api.getDeveloperSettings();
      this.enabled.set(settings.enabled);
    } catch {
    } finally {
      this.loaded.set(true);
    }
  }
}
