import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, CheckboxComponent, InputComponent, LocalizationKey, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { PortableExportOptions } from '../../../services/portability.service';
import { assessExportPassword, generateExportPassword } from '../../../domain/export-password.util';

export type ExportToggleKey = 'includeIcons' | 'includeSubfolders';

export interface ExportOptionToggle {
  key: ExportToggleKey;
  labelKey: LocalizationKey;
  uncheckedHintKey?: LocalizationKey;
  checked?: boolean;
}

type ToggleState = Partial<Record<ExportToggleKey, boolean>>;

export const EXPORT_ICONS_TOGGLE: ExportOptionToggle = {
  key: 'includeIcons',
  labelKey: AppStrings.Portable.Export.IncludeIcons,
  uncheckedHintKey: AppStrings.Portable.Export.IncludeIconsHint
};

export const EXPORT_SUBFOLDERS_TOGGLE: ExportOptionToggle = {
  key: 'includeSubfolders',
  labelKey: AppStrings.Portable.Export.IncludeSubfolders,
  uncheckedHintKey: AppStrings.Portable.Export.IncludeSubfoldersHint
};

@Component({
  selector: 'shared-export-options-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, CheckboxComponent, InputComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-modal [heading]="heading" size="small" (close)="onCancel()">
      <div class="export-options">
        <p class="export-options-desc">{{ description }}</p>
        @for (toggle of toggles(); track toggle.key) {
          <shared-checkbox
            [label]="toggle.labelKey | translate"
            [ngModel]="isChecked(toggle.key)"
            (ngModelChange)="setToggle(toggle.key, $event)" />
          @if (toggle.uncheckedHintKey && !isChecked(toggle.key)) {
            <p class="export-options-hint">{{ toggle.uncheckedHintKey | translate }}</p>
          }
        }
        <shared-checkbox
          [label]="'macrodeck.app:Portable.ExportOptions.IncludeSecretsLabel' | translate"
          [ngModel]="includeSecrets()"
          (ngModelChange)="setIncludeSecrets($event)" />
        @if (includeSecrets()) {
          <div class="export-options-secret">
            <p class="export-options-warning">
              <span class="icon icon-lock icon-xs"></span>
              <span>{{ 'macrodeck.app:Portable.ExportOptions.PasswordWarning' | translate }}</span>
            </p>
            <div class="export-options-password">
              <shared-input
                [type]="revealed() ? 'text' : 'password'"
                [ngModel]="password()"
                (ngModelChange)="password.set($event)"
                (keydown.enter)="onExport()"
                [invalid]="password().length > 0 && !assessment().acceptable"
                [placeholder]="'macrodeck.app:Portable.ExportOptions.PasswordPlaceholder' | translate"
                [autofocus]="true" />
              <shared-button
                variant="secondary"
                [title]="(revealed() ? 'macrodeck.app:Portable.ExportOptions.HidePasswordTitle' : 'macrodeck.app:Portable.ExportOptions.ShowPasswordTitle') | translate"
                (click)="revealed.set(!revealed())">
                {{ (revealed() ? 'macrodeck.app:Portable.ExportOptions.HideLabel' : 'macrodeck.app:Portable.ExportOptions.ShowLabel') | translate }}
              </shared-button>
              <shared-button
                variant="secondary"
                [title]="'macrodeck.app:Portable.ExportOptions.GeneratePasswordTitle' | translate"
                (click)="regenerate()">
                {{ 'macrodeck.app:Portable.ExportOptions.GenerateLabel' | translate }}
              </shared-button>
            </div>
            <p class="export-options-strength" [class]="'is-' + assessment().strength">{{ assessment().label }}</p>
          </div>
        }
      </div>
      <div modal-footer>
        <shared-button variant="secondary" (click)="onCancel()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
        <shared-button variant="primary" [disabled]="!canExport()" (click)="onExport()">{{ 'macrodeck:Common.Export' | translate }}</shared-button>
      </div>
    </shared-modal>
  `,
  styleUrls: ['./export-options-modal.component.scss']
})
export class ExportOptionsModalComponent {
  private readonly localization = inject(LocalizationService);

  @Input() heading = this.localization.translateKey(AppStrings.Portable.ExportOptions.DefaultTitle);
  @Input() description = this.localization.translateKey(AppStrings.Portable.ExportOptions.DefaultDescription);

  @Input()
  set options(value: ExportOptionToggle[]) {
    this.toggles.set(value);
    this.toggleState.update(state => {
      const seeded: ToggleState = {};
      for (const toggle of value) {
        seeded[toggle.key] = state[toggle.key] ?? toggle.checked ?? true;
      }
      return seeded;
    });
  }

  @Output() confirmed = new EventEmitter<PortableExportOptions>();
  @Output() cancelled = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly toggles = signal<ExportOptionToggle[]>([]);
  protected readonly includeSecrets = signal(false);
  protected readonly password = signal('');
  protected readonly revealed = signal(false);
  protected readonly assessment = computed(() => assessExportPassword(this.password(), (key, args) => this.localization.translateKey(key, args)));
  protected readonly canExport = computed(() => !this.includeSecrets() || this.assessment().acceptable);

  private readonly toggleState = signal<ToggleState>({});

  protected isChecked(key: ExportToggleKey): boolean {
    return this.toggleState()[key] ?? true;
  }

  protected setToggle(key: ExportToggleKey, value: boolean): void {
    this.toggleState.update(state => ({ ...state, [key]: value }));
  }

  protected setIncludeSecrets(value: boolean): void {
    this.includeSecrets.set(value);
    if (value) {
      this.regenerate();
      this.revealed.set(true);
      return;
    }

    this.password.set('');
    this.revealed.set(false);
  }

  protected regenerate(): void {
    this.password.set(generateExportPassword());
  }

  protected onExport(): void {
    if (!this.canExport()) {
      return;
    }

    const options: PortableExportOptions = {
      ...this.toggleState(),
      includeSecrets: this.includeSecrets(),
      password: this.includeSecrets() ? this.password() : undefined
    };
    dismissModal(this.modal, () => this.confirmed.emit(options));
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.cancelled.emit());
  }
}
