import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings } from '@macro-deck/runtime';
import { InputComponent, LocalizationService, TranslatePipe } from '@shared';
import { WidgetFontFaceControlComponent } from './widget-font-face-control.component';

export type WidgetFontAppearanceField = 'fontFaceId' | 'fontSize' | 'textAlign' | 'labelPosition';

export interface WidgetFontAppearanceChange {
  field: WidgetFontAppearanceField;
  value: string | number | undefined;
}

@Component({
  selector: 'shared-widget-font-appearance-control',
  standalone: true,
  imports: [FormsModule, InputComponent, WidgetFontFaceControlComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="font-row">
      <shared-widget-font-face-control class="font-face" [actionMode]="actionMode" [fontFaceId]="fontFaceId"
        (fontFaceIdChange)="emit('fontFaceId', $event)" />
      <div class="form-group form-group--dense font-size"><label>{{ 'macrodeck.app:Widgets.Appearance.Font.SizeLabel' | translate }}</label>
        <shared-input class="font-input" type="number" [min]="1" [max]="100"
          [placeholder]="actionMode ? unchangedLabel : ''" [ngModel]="fontSizeValue"
          (ngModelChange)="emit('fontSize', $event === '' ? (actionMode ? 0 : 14) : +$event)" />
      </div>
    </div>
    <div class="font-row font-segments">
      <div class="form-group form-group--dense font-align"><label>{{ 'macrodeck.app:Widgets.Appearance.Font.AlignLabel' | translate }}</label><div class="font-button-group">
        @if (actionMode) {
          <button type="button" class="font-toggle" [class.active]="!textAlign" [title]="leaveAlignmentUnchangedLabel"
            [attr.aria-label]="leaveAlignmentUnchangedLabel" (click)="emit('textAlign', '')">
            <span class="icon icon-minus icon-sm" aria-hidden="true"></span>
          </button>
        }
        @for (alignment of horizontalOptions; track alignment.value) {
          <button type="button" class="font-toggle" [class.active]="currentTextAlign === alignment.value"
            [title]="alignment.label" [attr.aria-label]="alignment.label" (click)="emit('textAlign', alignment.value)">
            <span class="icon icon-sm" [class]="'icon-' + alignment.icon"></span>
          </button>
        }
      </div></div>
      <div class="form-group form-group--dense font-position"><label>{{ 'macrodeck.app:Widgets.Appearance.Font.PositionLabel' | translate }}</label><div class="font-button-group">
        @if (actionMode) {
          <button type="button" class="font-toggle" [class.active]="!labelPosition" [title]="leavePositionUnchangedLabel"
            [attr.aria-label]="leavePositionUnchangedLabel" (click)="emit('labelPosition', '')">
            <span class="icon icon-minus icon-sm" aria-hidden="true"></span>
          </button>
        }
        @for (position of verticalOptions; track position.value) {
          <button type="button" class="font-toggle" [class.active]="currentLabelPosition === position.value"
            [title]="position.label" [attr.aria-label]="position.label" (click)="emit('labelPosition', position.value)">
            <span class="icon icon-sm" [class]="'icon-' + position.icon"></span>
          </button>
        }
      </div></div>
    </div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-width: 0; gap: var(--space-3); }
    .font-row { display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: flex-end; }
    .font-face { flex: 2 1 20.25rem; min-width: 0; }
    .font-size { flex: 0 1 4.5rem; } .font-input { width: 100%; }
    .font-segments { gap: var(--space-2); } .font-align, .font-position { flex: 1 1 7.3125rem; }
    .font-button-group { display: flex; padding: 2px; gap: 2px; background: var(--color-bg-primary); border: 1px solid var(--color-border); border-radius: var(--radius-md); }
    .font-toggle { flex: 1 1 0; min-width: 0; height: 1.4375rem; display: inline-flex; align-items: center; justify-content: center; padding: 0; background: transparent; color: var(--color-text-secondary); border: none; border-radius: var(--radius-sm); font-size: var(--text-sm); cursor: pointer; transition: background-color var(--transition-fast), color var(--transition-fast); }
    .font-toggle:hover:not(:disabled):not(.active) { background: var(--color-bg-hover); color: var(--color-text-primary); }
    .font-toggle .icon { background-color: currentColor; } .font-toggle.active { background: var(--color-accent); color: #fff; }
  `,
})
export class WidgetFontAppearanceControlComponent {
  private readonly localization = inject(LocalizationService);

  @Input() actionMode = false;
  @Input() fontFaceId: string | undefined;
  @Input() fontSize: number | undefined;
  @Input() textAlign: 'left' | 'center' | 'right' | undefined;
  @Input() labelPosition: 'top' | 'center' | 'bottom' | undefined;
  @Output() readonly appearanceChange = new EventEmitter<WidgetFontAppearanceChange>();

  protected get horizontalOptions() {
    const t = (key: string) => this.localization.translateKey(key);
    const S = AppStrings.Widgets.Appearance.Font;
    return [
      { value: 'left' as const, label: t(S.AlignLeft), icon: 'align-left' },
      { value: 'center' as const, label: t(S.AlignCenter), icon: 'align-center' },
      { value: 'right' as const, label: t(S.AlignRight), icon: 'align-right' },
    ];
  }
  protected get verticalOptions() {
    const t = (key: string) => this.localization.translateKey(key);
    const S = AppStrings.Widgets.Appearance.Font;
    return [
      { value: 'top' as const, label: t(S.PositionTop), icon: 'align-top' },
      { value: 'center' as const, label: t(S.PositionCenter), icon: 'align-middle' },
      { value: 'bottom' as const, label: t(S.PositionBottom), icon: 'align-bottom' },
    ];
  }

  protected get unchangedLabel(): string {
    return this.localization.translateKey(AppStrings.Widgets.Appearance.Font.Unchanged);
  }
  protected get leaveAlignmentUnchangedLabel(): string {
    return this.localization.translateKey(AppStrings.Widgets.Appearance.Font.LeaveAlignmentUnchanged);
  }
  protected get leavePositionUnchangedLabel(): string {
    return this.localization.translateKey(AppStrings.Widgets.Appearance.Font.LeavePositionUnchanged);
  }

  protected get fontSizeValue(): number | '' {
    return this.fontSize && this.fontSize > 0 ? this.fontSize : (this.actionMode ? '' : 14);
  }
  protected get currentTextAlign(): 'left' | 'center' | 'right' | undefined { return this.actionMode ? this.textAlign : (this.textAlign ?? 'center'); }
  protected get currentLabelPosition(): 'top' | 'center' | 'bottom' | undefined { return this.actionMode ? this.labelPosition : (this.labelPosition ?? 'center'); }

  protected emit(field: WidgetFontAppearanceField, value: string | number | undefined): void { this.appearanceChange.emit({ field, value }); }
}
