import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, ThemeMode } from '@macro-deck/runtime';
import { DEFAULT_ACCENT_COLOR, LocalizationService, SegmentedControlComponent, SegmentedOption, SettingsRowComponent, SettingsSectionComponent, ThemeService, TranslatePipe } from '@shared';
import { FontService } from '../../../../services/font.service';
import { ColorPickerComponent } from '../../../forms/color-picker/color-picker.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';

@Component({
  selector: 'app-appearance-settings',
  standalone: true,
  imports: [FormsModule, ColorPickerComponent, SegmentedControlComponent, SelectComponent,
    SettingsSectionComponent, SettingsRowComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './appearance-settings.component.html',
  styleUrls: ['./appearance-settings.component.scss'],
})
export class AppearanceSettingsComponent {
  private readonly themeService = inject(ThemeService);
  private readonly localization = inject(LocalizationService);
  private readonly fonts = inject(FontService);

  readonly themeMode = this.themeService.themeMode;
  readonly accentColor = this.themeService.accentColor;
  readonly fontFamily = this.themeService.fontFamily;
  readonly defaultAccent = DEFAULT_ACCENT_COLOR;

  readonly themeOptions = computed<SegmentedOption[]>(() => [
    { value: 'light', label: this.localization.translateKey(AppStrings.Settings.Appearance.Light), icon: 'sun' },
    { value: 'dark', label: this.localization.translateKey(AppStrings.Settings.Appearance.Dark), icon: 'moon' },
    { value: 'system', label: this.localization.translateKey(AppStrings.Settings.Appearance.System) },
  ]);

  readonly fontOptions = computed<SelectOption[]>(() => [
    { value: '', label: this.localization.translateKey(AppStrings.Settings.Appearance.FontSystemDefault) },
    ...this.fonts.families().map(({ family }) => ({ value: family, label: family })),
  ]);

  constructor() {
    void this.fonts.loadSystemFonts();
  }

  selectMode(mode: string): void {
    this.themeService.setThemeMode(mode as ThemeMode);
  }

  onAccentChange(color: string): void {
    this.themeService.setAccentColor(color);
  }

  onFontChange(family: string): void {
    this.themeService.setFontFamily(family ?? '');
  }
}
