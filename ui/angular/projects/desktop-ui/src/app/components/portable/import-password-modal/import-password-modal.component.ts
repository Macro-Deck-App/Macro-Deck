import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';

@Component({
  selector: 'shared-import-password-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, InputComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-modal [heading]="heading" size="small" (close)="onCancel()">
      <div class="import-password">
        <p class="import-password-desc">{{ 'macrodeck.app:Portable.ImportPassword.Description' | translate }}</p>
        <shared-input
          type="password"
          [ngModel]="password()"
          (ngModelChange)="password.set($event)"
          (keydown.enter)="onSubmit()"
          [placeholder]="'macrodeck.app:Portable.ImportPassword.PasswordPlaceholder' | translate"
          [invalid]="invalid"
          [autofocus]="true" />
        @if (invalid) {
          <p class="import-password-error">{{ 'macrodeck.app:Portable.ImportPassword.Error' | translate }}</p>
        }
      </div>
      <div modal-footer>
        <shared-button variant="secondary" (click)="onCancel()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
        <shared-button variant="primary" [disabled]="password().length === 0" (click)="onSubmit()">{{ 'macrodeck:Common.Import' | translate }}</shared-button>
      </div>
    </shared-modal>
  `,
  styleUrls: ['./import-password-modal.component.scss']
})
export class ImportPasswordModalComponent {
  private readonly localization = inject(LocalizationService);

  @Input() heading = this.localization.translateKey(AppStrings.Portable.ImportPassword.DefaultTitle);
  @Input() invalid = false;

  @Output() submitted = new EventEmitter<string>();
  @Output() cancelled = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly password = signal('');

  protected onSubmit(): void {
    if (this.password().length === 0) {
      return;
    }

    dismissModal(this.modal, () => this.submitted.emit(this.password()));
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.cancelled.emit());
  }
}
