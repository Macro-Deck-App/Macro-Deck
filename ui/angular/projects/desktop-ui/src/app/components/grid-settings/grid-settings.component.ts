import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, WIDGET_REFERENCE_BORDER_RADIUS } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { ColorPickerComponent, ColorPreset } from '../forms/color-picker/color-picker.component';
import { InheritableSettingComponent } from '../forms/inheritable-setting/inheritable-setting.component';

@Component({
  selector: 'shared-grid-settings',
  standalone: true,
  imports: [FormsModule, ColorPickerComponent, InheritableSettingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './grid-settings.component.html',
  styleUrls: ['./grid-settings.component.scss']
})
export class GridSettingsComponent {
  @Input() cols: number | null = 5;
  @Input() rows: number | null = 3;
  @Input() background = '';
  @Input() minCols = 1;
  @Input() maxCols = 12;
  @Input() minRows = 1;
  @Input() maxRows = 8;
  @Input() colsLocked = false;
  @Input() rowsLocked = false;
  @Input() lockNote = '';
  @Input() spacingHonoured = true;
  @Input() cornerRadiusHonoured = true;
  @Input() noEffectNote = '';
  @Input() effectiveCols = 5;
  @Input() effectiveRows = 3;
  @Input() gridInheritable = true;

  private readonly inheritedLabelOverride = signal<string | null>(null);
  @Input() set inheritedLabel(value: string) { this.inheritedLabelOverride.set(value); }
  readonly inheritedLabelState = computed(() =>
    this.inheritedLabelOverride() ?? this.localization.translateKey(AppStrings.Widgets.GridSettings.Inherited));

  @Input() showWidgetAppearance = false;
  @Input() spacing: number | null = null;
  @Input() borderRadius: number | null = null;
  @Input() effectiveSpacing = 12;
  @Input() effectiveBorderRadius: number | null = null;

  @Output() colsChange = new EventEmitter<number | null>();
  @Output() rowsChange = new EventEmitter<number | null>();
  @Output() backgroundChange = new EventEmitter<string>();
  @Output() spacingChange = new EventEmitter<number | null>();
  @Output() borderRadiusChange = new EventEmitter<number | null>();

  private readonly localization = inject(LocalizationService);

  readonly minSpacing = 0;
  readonly maxSpacing = 40;
  readonly minBorderRadius = 0;
  readonly maxBorderRadius = 60;
  readonly defaultBorderRadius = WIDGET_REFERENCE_BORDER_RADIUS;

  readonly columnsLabel = computed(() => this.localization.translateKey(AppStrings.Widgets.GridSettings.Columns));
  readonly rowsLabel = computed(() => this.localization.translateKey(AppStrings.Widgets.GridSettings.Rows));
  readonly widgetSpacingLabel = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.GridSettings.WidgetSpacing));
  readonly cornerRadiusLabel = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.GridSettings.CornerRadius));
  readonly backgroundLabel = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.GridSettings.Background));

  readonly backgroundPresets = computed<ColorPreset[]>(() => [
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorDark), value: 'rgba(0, 0, 0, 0.7)' },
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorCharcoal), value: 'rgba(26, 26, 26, 0.8)' },
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorForest), value: 'rgba(20, 83, 45, 0.4)' },
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorEmerald), value: 'rgba(4, 120, 87, 0.3)' },
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorOcean), value: 'rgba(30, 58, 138, 0.4)' },
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorViolet), value: 'rgba(88, 28, 135, 0.4)' },
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorRose), value: 'rgba(127, 29, 29, 0.4)' },
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorAmber), value: 'rgba(154, 52, 18, 0.4)' },
    { label: this.localization.translateKey(AppStrings.Widgets.GridSettings.ColorTransparent), value: 'transparent' },
  ]);

  onColsChange(value: number): void {
    this.colsChange.emit(Number(value));
  }

  onColsReset(): void {
    this.colsChange.emit(null);
  }

  onRowsChange(value: number): void {
    this.rowsChange.emit(Number(value));
  }

  onRowsReset(): void {
    this.rowsChange.emit(null);
  }

  onBackgroundChange(value: string): void {
    this.background = value;
    this.backgroundChange.emit(value);
  }

  onSpacingSliderChange(value: number): void {
    this.spacingChange.emit(Number(value));
  }

  onSpacingReset(): void {
    this.spacingChange.emit(null);
  }

  onBorderRadiusSliderChange(value: number): void {
    this.borderRadiusChange.emit(Number(value));
  }

  onBorderRadiusReset(): void {
    this.borderRadiusChange.emit(null);
  }
}
