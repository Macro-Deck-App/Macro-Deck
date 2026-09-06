import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, LogEntryLevel } from '@macro-deck/runtime';
import { ApiService, LocalizationService, SettingsRowComponent, SettingsSectionComponent, TranslatePipe } from '@shared';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';

const LEVEL_LABEL_KEYS: ReadonlyArray<readonly [LogEntryLevel, string]> = [
  [LogEntryLevel.Fatal, AppStrings.Settings.Logging.Level.Fatal],
  [LogEntryLevel.Error, AppStrings.Settings.Logging.Level.Error],
  [LogEntryLevel.Warning, AppStrings.Settings.Logging.Level.Warning],
  [LogEntryLevel.Information, AppStrings.Settings.Logging.Level.Information],
  [LogEntryLevel.Debug, AppStrings.Settings.Logging.Level.Debug],
  [LogEntryLevel.Verbose, AppStrings.Settings.Logging.Level.Verbose],
];

@Component({
  selector: 'app-logging-settings',
  standalone: true,
  imports: [FormsModule, SettingsSectionComponent, SettingsRowComponent, SelectComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './logging-settings.component.html',
  styleUrls: ['./logging-settings.component.scss'],
})
export class LoggingSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly minimumLevel = signal<LogEntryLevel>(LogEntryLevel.Information);
  readonly defaultLevel = signal<LogEntryLevel>(LogEntryLevel.Information);
  readonly loaded = signal(false);

  readonly levelOptions = computed<SelectOption[]>(() =>
    LEVEL_LABEL_KEYS.map(([value, key]) => {
      const label = this.localization.translateKey(key);
      return {
        value,
        label: value === this.defaultLevel()
          ? this.localization.translateKey(AppStrings.Settings.Logging.LevelDefault, { level: label })
          : label,
      };
    })
  );

  constructor() {
    void this.load();
  }

  async setLevel(value: string): Promise<void> {
    // Nothing to overwrite yet: a pick made before the load resolves would race its response.
    if (!this.loaded()) {
      return;
    }

    this.minimumLevel.set(value as LogEntryLevel);
    try {
      const applied = await this.api.updateLoggingSettings({ minimumLevel: value as LogEntryLevel });
      this.apply(applied.minimumLevel, applied.defaultMinimumLevel);
    } catch {
      await this.load();
    }
  }

  private async load(): Promise<void> {
    try {
      const settings = await this.api.getLoggingSettings();
      this.apply(settings.minimumLevel, settings.defaultMinimumLevel);
    } catch {
    } finally {
      this.loaded.set(true);
    }
  }

  private apply(minimum: LogEntryLevel, fallback: LogEntryLevel): void {
    this.minimumLevel.set(minimum);
    this.defaultLevel.set(fallback);
  }
}
