import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, parseColor, ThemeMode } from '@macro-deck/runtime';
import { ButtonComponent, ColorPaletteService, OverlayPanelComponent, DEFAULT_ACCENT_COLOR, LocalizationService, MAX_PALETTE_COLORS, SegmentedControlComponent, SegmentedOption, SettingsRowComponent, SettingsSectionComponent, ThemeService, TranslatePipe } from '@shared';
import { FontService } from '../../../../services/font.service';
import { ColorPickerComponent, paletteColorLabel } from '../../../forms/color-picker/color-picker.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';

@Component({
  selector: 'app-appearance-settings',
  standalone: true,
  imports: [FormsModule, ButtonComponent, ColorPickerComponent, ConfirmationModalComponent, EmptyStateComponent, OverlayPanelComponent, SegmentedControlComponent, SelectComponent,
    SettingsSectionComponent, SettingsRowComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './appearance-settings.component.html',
  styleUrls: ['./appearance-settings.component.scss'],
})
export class AppearanceSettingsComponent {
  private readonly themeService = inject(ThemeService);
  private readonly localization = inject(LocalizationService);
  private readonly fonts = inject(FontService);
  private readonly palette = inject(ColorPaletteService);

  readonly themeMode = this.themeService.themeMode;
  readonly accentColor = this.themeService.accentEditorValue;
  readonly fontFamily = this.themeService.fontFamily;
  readonly defaultAccent = DEFAULT_ACCENT_COLOR;
  readonly paletteColors = this.palette.colors;
  readonly paletteDraft = signal('#22c55e');
  readonly isAdding = signal(false);
  readonly restorePromptOpen = signal(false);
  private readonly addAnchor = viewChild<ElementRef<HTMLElement>>('addAnchor');
  private readonly addDialog = viewChild<ElementRef<HTMLElement>>('addDialog');
  readonly paletteFull = this.palette.isFull;

  readonly canAddDraft = computed(() => {
    const draft = this.paletteDraft().toLowerCase();
    return draft.startsWith('#') && parseColor(draft) !== null && !this.palette.isFull()
      && !this.palette.contains(draft);
  });

  readonly addTitle = computed(() => this.palette.isFull()
    ? this.localization.translateKey(AppStrings.Errors.ColorPalette.Full, { max: MAX_PALETTE_COLORS })
    : this.localization.translateKey(AppStrings.Forms.ColorPicker.AddToPalette));

  readonly themeOptions = computed<SegmentedOption[]>(() => [
    { value: 'light', label: this.localization.translateKey(AppStrings.Settings.Appearance.Light), icon: 'sun' },
    { value: 'dark', label: this.localization.translateKey(AppStrings.Settings.Appearance.Dark), icon: 'moon' },
    { value: 'system', label: this.localization.translateKey(AppStrings.Settings.Appearance.System) },
  ]);

  readonly fontOptions = computed<SelectOption[]>(() => [
    { value: '', label: this.localization.translateKey(AppStrings.Settings.Appearance.FontSystemDefault) },
    ...this.fonts.systemFamilies().map(({ family }) => ({ value: family, label: family })),
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

  toggleAdding(): void {
    if (this.isAdding()) {
      this.closeAdding();
      return;
    }

    this.isAdding.set(true);
    requestAnimationFrame(() =>
      this.addDialog()?.nativeElement.querySelector<HTMLElement>('[tabindex="0"], input, button')?.focus());
  }

  closeAdding(): void {
    if (!this.isAdding()) return;
    this.isAdding.set(false);
    this.addAnchor()?.nativeElement.querySelector('button')?.focus();
  }

  addDraft(): void {
    if (!this.canAddDraft()) return;
    void this.palette.add(this.paletteDraft());
    this.closeAdding();
  }

  remove(color: string): void {
    void this.palette.remove(color);
  }

  restoreDefaults(): void {
    this.restorePromptOpen.set(false);
    void this.palette.restoreDefaults();
  }

  colorLabel(color: string): string {
    return paletteColorLabel(color, this.localization);
  }

  removeLabel(color: string): string {
    return this.localization.translateKey(AppStrings.Forms.ColorPicker.RemoveFromPalette, { hex: color });
  }
}
