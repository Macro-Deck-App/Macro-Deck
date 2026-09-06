import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { FontService } from '../../services/font.service';
import { SelectComponent, SelectOption } from '../forms/select/select.component';

@Component({
  selector: 'shared-widget-font-face-control',
  standalone: true,
  imports: [FormsModule, SelectComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-group form-group--dense face-grow"><label>{{ 'macrodeck.app:Widgets.Appearance.Font.FontLabel' | translate }}</label>
      <shared-select class="face-select" [options]="familyOptions" [ngModel]="selectedFamily"
        (ngModelChange)="onFamilyChange($event)" />
    </div>
    <div class="form-group form-group--dense face-grow"><label>{{ 'macrodeck.app:Widgets.Appearance.Font.StyleLabel' | translate }}</label>
      <shared-select class="face-select" [options]="styleOptions" [ngModel]="fontFaceId ?? ''"
        (ngModelChange)="emit($event)" />
    </div>
  `,
  styles: `
    :host { display: flex; min-width: 0; flex: 1 1 auto; gap: var(--space-3); }
    .face-grow { flex: 1 1 9.75rem; min-width: 0; }
    .face-select { width: 100%; }
  `,
})
export class WidgetFontFaceControlComponent implements OnInit {
  private readonly fontService = inject(FontService);
  private readonly localization = inject(LocalizationService);

  @Input() actionMode = false;
  @Input() fontFaceId: string | undefined;
  @Output() readonly fontFaceIdChange = new EventEmitter<string>();

  ngOnInit(): void { if (this.fontService.families().length === 0) void this.fontService.loadSystemFonts(); }

  protected get familyOptions(): SelectOption[] {
    const S = AppStrings.Widgets.Appearance.Font;
    const defaultLabel = this.localization.translateKey(this.actionMode ? S.Unchanged : S.Default);
    return [{ value: '', label: defaultLabel },
      ...this.fontService.families().map(f => ({ value: f.family, label: f.family }))];
  }

  protected get selectedFamily(): string {
    return this.fontService.faceById(this.fontFaceId)?.family ?? '';
  }

  protected get styleOptions(): SelectOption[] {
    const family = this.selectedFamily;
    if (!family) {
      const S = AppStrings.Widgets.Appearance.Font;
      const defaultLabel = this.localization.translateKey(this.actionMode ? S.Unchanged : S.Default);
      return [{ value: '', label: defaultLabel }];
    }
    return this.fontService.facesForFamily(family).map(face => ({ value: face.faceId, label: face.styleName }));
  }

  protected onFamilyChange(family: string): void {
    const faces = this.fontService.facesForFamily(family);
    this.emit(faces[0]?.faceId ?? '');
  }

  protected emit(value: string): void {
    this.fontFaceIdChange.emit(value);
  }
}
