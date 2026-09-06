import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  GetAutostartSettingsResponse,
} from '@macro-deck/runtime';
import {
  ApiService,
  SettingsRowComponent,
  SettingsSectionComponent,
  ToggleSwitchComponent,
  TranslatePipe,
} from '@shared';
import { DockIconSettingsComponent } from './dock-icon-settings.component';

@Component({
  selector: 'app-startup-settings',
  standalone: true,
  imports: [
    SettingsSectionComponent, SettingsRowComponent, ToggleSwitchComponent, DockIconSettingsComponent, TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './startup-settings.component.html',
  styleUrls: ['./startup-settings.component.scss'],
})
export class StartupSettingsComponent {
  private readonly api = inject(ApiService);

  readonly supported = signal(false);
  readonly enabled = signal(false);
  readonly openMinimized = signal(false);
  readonly loaded = signal(false);
  readonly enabledBusy = signal(false);
  readonly openMinimizedBusy = signal(false);

  constructor() {
    void this.load();
  }

  async setEnabled(value: boolean): Promise<void> {
    if (!this.supported() || this.enabledBusy()) {
      return;
    }
    this.enabledBusy.set(true);
    this.enabled.set(value);
    try {
      await this.apply(value, this.openMinimized());
    } finally {
      this.enabledBusy.set(false);
    }
  }

  async setOpenMinimized(value: boolean): Promise<void> {
    if (!this.supported() || !this.enabled() || this.openMinimizedBusy()) {
      return;
    }
    this.openMinimizedBusy.set(true);
    this.openMinimized.set(value);
    try {
      await this.apply(true, value);
    } finally {
      this.openMinimizedBusy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.applyState(await this.api.getAutostartSettings());
    } catch {
    } finally {
      this.loaded.set(true);
    }
  }

  private async apply(enabled: boolean, openMinimized: boolean): Promise<void> {
    try {
      this.applyState(await this.api.updateAutostartSettings({ enabled, openMinimized }));
    } catch {
      await this.load();
    }
  }

  private applyState(state: GetAutostartSettingsResponse): void {
    this.supported.set(state.supported);
    this.enabled.set(state.enabled);
    this.openMinimized.set(state.openMinimized);
  }
}
