import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings, COLOR_WIDGET_BORDER_STYLES, DEFAULT_WIDGET_BORDER_COLOR, WidgetBorder, WidgetBorderStyle } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { ColorPickerComponent } from '../forms/color-picker/color-picker.component';
import { SelectComponent, SelectOption } from '../forms/select/select.component';

@Component({
  selector: 'shared-widget-border-control',
  standalone: true,
  imports: [FormsModule, ColorPickerComponent, SelectComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="border-form">
      <div class="form-group form-group--dense">
        <label>{{ 'macrodeck.app:Widgets.Appearance.Border.StyleLabel' | translate }}</label>
        <shared-select class="border-select" [options]="styleOptions" [ngModel]="border?.style ?? defaultStyle"
          (ngModelChange)="onStyleChange($event)" />
      </div>
      @if (showsColor) {
        <div class="form-group form-group--dense">
          <label>{{ 'macrodeck.app:Widgets.Appearance.Border.ColorLabel' | translate }}</label>
          <shared-color-picker [defaultColor]="defaultColor" [resetValue]="resetColor"
            [ngModel]="border?.color" (ngModelChange)="onColorChange($event)" />
        </div>
      }
    </div>
  `,
  styles: `
    .border-form { display: flex; flex-direction: column; min-width: 0; gap: var(--space-4); }
    .border-select { width: 100%; }
  `,
})
export class WidgetBorderControlComponent {
  @Input() border: WidgetBorder | undefined;
  @Input() actionMode = false;
  @Input() resetColor: string | undefined;
  @Output() borderChange = new EventEmitter<WidgetBorder>();

  private readonly localization = inject(LocalizationService);

  protected readonly defaultColor = DEFAULT_WIDGET_BORDER_COLOR;

  protected get defaultStyle(): string {
    return this.actionMode ? '' : 'off';
  }

  protected get styleOptions(): SelectOption[] {
    const t = (key: string) => this.localization.translateKey(key);
    const S = AppStrings.Widgets.Appearance.Border;
    const styles: SelectOption[] = [
      { value: 'off', label: t(S.Off) }, { value: 'static', label: t(S.Static) },
      { value: 'heartbeat', label: t(S.Heartbeat) }, { value: 'breathing', label: t(S.Breathing) },
      { value: 'blink', label: t(S.Blink) }, { value: 'comet', label: t(S.Comet) },
      { value: 'ants', label: t(S.MarchingAnts) }, { value: 'hue-shift', label: t(S.HueShift) },
      { value: 'rgb', label: t(S.Rgb) },
    ];
    return this.actionMode ? [{ value: '', label: t(S.Unchanged) }, ...styles] : styles;
  }

  protected get showsColor(): boolean {
    const style = this.border?.style as string | undefined;
    return (this.actionMode && style === '')
      || (style !== undefined && COLOR_WIDGET_BORDER_STYLES.includes(style as WidgetBorderStyle));
  }

  protected onStyleChange(style: string): void {
    this.borderChange.emit({ ...this.border, style: style as WidgetBorderStyle });
  }

  protected onColorChange(color: string): void {
    this.borderChange.emit({ ...this.border, color });
  }
}
