import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, LocalizationTimeFormat } from '@macro-deck/runtime';
import { LocalizationService, SettingsRowComponent, SettingsSectionComponent, TranslatePipe } from '@shared';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { cultureDisplayName } from '../../../../localization/culture-display.util';

const SYSTEM_CULTURE = 'system';

@Component({
  selector: 'app-language-settings',
  standalone: true,
  imports: [FormsModule, SelectComponent, SettingsSectionComponent, SettingsRowComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './language-settings.component.html',
  styleUrls: ['./language-settings.component.scss'],
})
export class LanguageSettingsComponent implements OnInit {
  private readonly localization = inject(LocalizationService);

  protected readonly culture = this.localization.culture;
  protected readonly applying = signal(false);

  protected readonly selected = computed(() =>
    this.localization.followsSystem() ? SYSTEM_CULTURE : this.culture(),
  );

  protected readonly timeFormat = this.localization.timeFormat;

  protected readonly timeFormatOptions = computed<SelectOption[]>(() => [
    { value: 'system', label: this.localization.translateKey(AppStrings.Settings.Language.SystemOption) },
    { value: '12h', label: this.localization.translateKey(AppStrings.Widgets.Clock.HourCycle12) },
    { value: '24h', label: this.localization.translateKey(AppStrings.Widgets.Clock.HourCycle24) },
  ]);

  protected readonly cultureOptions = computed<SelectOption[]>(() => [
    {
      value: SYSTEM_CULTURE,
      label: this.localization.translateKey(AppStrings.Settings.Language.SystemOption),
    },
    ...this.localization.availableCultures()
      .map(culture => ({ value: culture, label: cultureDisplayName(culture) }))
      .sort((left, right) => left.label.localeCompare(right.label, this.culture())),
  ]);

  ngOnInit(): void {
    void this.localization.loadFromHost();
  }

  async selectCulture(culture: string): Promise<void> {
    if (culture === this.selected() || this.applying()) return;

    this.applying.set(true);
    try {
      await (culture === SYSTEM_CULTURE
        ? this.localization.followSystemCulture()
        : this.localization.setCulture(culture));
    } finally {
      this.applying.set(false);
    }
  }

  async selectTimeFormat(timeFormat: LocalizationTimeFormat): Promise<void> {
    if (timeFormat === this.timeFormat() || this.applying()) return;

    this.applying.set(true);
    try {
      await this.localization.setTimeFormat(timeFormat);
    } finally {
      this.applying.set(false);
    }
  }
}
