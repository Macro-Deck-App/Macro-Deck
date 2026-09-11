import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, UpdateBackupSettingsRequest } from '@macro-deck/runtime';
import { LocalizationService, SettingsRowComponent, SettingsSectionComponent, ToastService, ToggleSwitchComponent, TranslatePipe } from '@shared';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { BackupService } from '../../../../services/backup.service';

const DAY_OF_WEEK_KEYS: readonly string[] = [
  AppStrings.Settings.Backups.Sunday,
  AppStrings.Settings.Backups.Monday,
  AppStrings.Settings.Backups.Tuesday,
  AppStrings.Settings.Backups.Wednesday,
  AppStrings.Settings.Backups.Thursday,
  AppStrings.Settings.Backups.Friday,
  AppStrings.Settings.Backups.Saturday,
];

const DAY_VALUES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

const DAYS_OF_MONTH: SelectOption[] =
  Array.from({ length: 31 }, (_, i) => ({ value: String(i + 1), label: String(i + 1) }));

// The "before Macro Deck updates" row is a hard platform limitation, not a preference: the host reports
// it unsupported on Linux, and the row stays visible-but-disabled with that reason rather than
// disappearing, so the gap does not read as a bug.
@Component({
  selector: 'app-backup-schedule-settings',
  standalone: true,
  imports: [
    FormsModule, SettingsSectionComponent, SettingsRowComponent, SelectComponent,
    ToggleSwitchComponent, LoadingStateComponent, TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './backup-schedule-settings.component.html',
  styleUrls: ['./backup-schedule-settings.component.scss'],
})
export class BackupScheduleSettingsComponent {
  private readonly backupService = inject(BackupService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  protected readonly settings = this.backupService.settings;
  protected readonly saving = signal(false);

  protected readonly frequencyOptions = computed<SelectOption[]>(() => [
    { value: 'daily', label: this.localization.translateKey(AppStrings.Settings.Backups.Daily) },
    { value: 'weekly', label: this.localization.translateKey(AppStrings.Settings.Backups.Weekly) },
    { value: 'monthly', label: this.localization.translateKey(AppStrings.Settings.Backups.Monthly) },
  ]);
  protected readonly dayOfWeekOptions = computed<SelectOption[]>(() =>
    DAY_VALUES.map((value, i) => ({ value, label: this.localization.translateKey(DAY_OF_WEEK_KEYS[i]) })));
  protected readonly dayOfMonthOptions = DAYS_OF_MONTH;
  protected readonly timeOptions = computed<SelectOption[]>(() => {
    const { locale, hourCycle } = this.localization.timeLocale();
    const format = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hourCycle, timeZone: 'UTC' });
    return Array.from({ length: 24 }, (_, i) => ({
      value: `${String(i).padStart(2, '0')}:00`,
      label: format.format(Date.UTC(2000, 0, 1, i)),
    }));
  });

  protected dateTimeLabel(iso: string): string {
    const { locale, hourCycle } = this.localization.timeLocale();
    return new Date(iso).toLocaleString(locale, { dateStyle: 'medium', timeStyle: 'medium', hourCycle });
  }

  protected readonly retentionOptions = computed<SelectOption[]>(() => {
    const state = this.settings();
    const min = state?.minimumRetentionCount ?? 1;
    const max = state?.maximumRetentionCount ?? 100;
    const options: SelectOption[] = [];
    for (let n = min; n <= max; n++) {
      options.push({ value: String(n), label: String(n) });
    }
    return options;
  });

  protected numberToString(value: number): string {
    return String(value);
  }

  protected setScheduledEnabled(enabled: boolean): Promise<void> {
    const current = this.settings()?.scheduleFrequency;
    return this.save({ scheduleFrequency: enabled ? (current && current !== 'off' ? current : 'daily') : 'off' });
  }

  protected setFrequency(value: string): Promise<void> {
    return this.save({ scheduleFrequency: value });
  }

  protected setDayOfWeek(value: string): Promise<void> {
    return this.save({ scheduleDayOfWeek: value });
  }

  protected setDayOfMonth(value: string): Promise<void> {
    return this.save({ scheduleDayOfMonth: Number(value) });
  }

  protected setTimeOfDay(value: string): Promise<void> {
    return this.save({ scheduleTimeOfDay: value });
  }

  protected setBeforeHostUpdate(value: boolean): Promise<void> {
    return this.save({ beforeHostUpdate: value });
  }

  protected setBeforePluginUpdate(value: boolean): Promise<void> {
    return this.save({ beforePluginUpdate: value });
  }

  protected setRetentionKeepLatest(value: string): Promise<void> {
    return this.save({ retentionKeepLatest: Number(value) });
  }

  private async save(patch: UpdateBackupSettingsRequest): Promise<void> {
    this.saving.set(true);
    try {
      const result = await this.backupService.updateSettings(patch);
      if (!result.ok) {
        this.toastService.show(result.error, { variant: 'error' });
      }
    } finally {
      this.saving.set(false);
    }
  }
}
