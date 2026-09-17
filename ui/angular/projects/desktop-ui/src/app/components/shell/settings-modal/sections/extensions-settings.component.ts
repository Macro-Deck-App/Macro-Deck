import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ExtensionSettingsBody, UpdateExtensionSettingsRequest } from '@macro-deck/runtime';
import { ApiService, SettingsRowComponent, SettingsSectionComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';

@Component({
  selector: 'app-extensions-settings',
  standalone: true,
  imports: [SettingsSectionComponent, SettingsRowComponent, ToggleSwitchComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './extensions-settings.component.html',
  styleUrls: ['./extensions-settings.component.scss'],
})
export class ExtensionsSettingsComponent {
  private readonly api = inject(ApiService);

  readonly settings = signal<ExtensionSettingsBody | null>(null);
  readonly busy = signal(false);

  constructor() {
    void this.load();
  }

  setNotifyOnUpdates(value: boolean): void {
    void this.apply({ notifyOnUpdates: value });
  }

  setAutoUpdate(value: boolean): void {
    void this.apply({ autoUpdate: value });
  }

  private async apply(request: UpdateExtensionSettingsRequest): Promise<void> {
    const previous = this.settings();
    if (!previous || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.settings.set({ ...previous, ...request });
    try {
      this.settings.set(await this.api.updateExtensionSettings(request));
    } catch {
      this.settings.set(previous);
      await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.settings.set(await this.api.getExtensionSettings());
    } catch {
    }
  }
}
