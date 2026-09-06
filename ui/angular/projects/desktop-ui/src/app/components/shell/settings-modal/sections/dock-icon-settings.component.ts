import { ApplicationRef, ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { SettingsRowComponent, SettingsSectionComponent, ToggleSwitchComponent, TranslatePipe } from '@shared';

@Component({
  selector: 'app-dock-icon-settings',
  standalone: true,
  imports: [SettingsSectionComponent, SettingsRowComponent, ToggleSwitchComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dock-icon-settings.component.html',
})
export class DockIconSettingsComponent {
  private readonly appRef = inject(ApplicationRef);

  readonly supported = signal(false);
  readonly enabled = signal(false);
  readonly busy = signal(false);

  constructor() {
    void this.load();
  }

  async setEnabled(value: boolean): Promise<void> {
    const bridge = window.macroDeckShell;
    if (this.busy() || typeof bridge?.setHideDockIcon !== 'function') {
      return;
    }
    const previous = this.enabled();
    this.busy.set(true);
    this.enabled.set(value);
    try {
      await this.appRef.whenStable();
      const status = await bridge.setHideDockIcon(value);
      this.enabled.set(status.enabled);
    } catch {
      this.enabled.set(previous);
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    const bridge = window.macroDeckShell;
    if (typeof bridge?.getHideDockIcon !== 'function') {
      return;
    }
    try {
      const status = await bridge.getHideDockIcon();
      this.supported.set(status.supported);
      this.enabled.set(status.enabled);
    } catch {
    }
  }
}
