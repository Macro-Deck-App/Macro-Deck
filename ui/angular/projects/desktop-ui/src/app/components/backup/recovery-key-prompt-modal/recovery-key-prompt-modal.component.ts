import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, InputComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';

@Component({
  selector: 'shared-recovery-key-prompt-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, ErrorBannerComponent, InputComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recovery-key-prompt-modal.component.html',
  styleUrls: ['./recovery-key-prompt-modal.component.scss'],
})
export class RecoveryKeyPromptModalComponent {
  private readonly localization = inject(LocalizationService);

  @Input() heading = '';
  @Input() description = '';
  @Input() confirmLabel = '';
  @Input() invalidMessage = '';
  @Input() invalid = false;
  @Input() submitting = false;
  @Input() bannerMessage: string | null = null;

  private placeholderOverride?: string;
  @Input() set placeholder(value: string) {
    this.placeholderOverride = value;
  }
  get placeholder(): string {
    return this.placeholderOverride ?? this.localization.translateKey(AppStrings.Backup.RecoveryKeyPrompt.Placeholder);
  }
  @Input() showCancel = true;
  @Input() closeOnOverlay = true;
  @Input() closeOnSubmit = true;

  @Output() submitted = new EventEmitter<string>();
  @Output() cancelled = new EventEmitter<void>();
  @Output() bannerDismissed = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly value = signal('');

  protected canSubmit(): boolean {
    return this.value().trim().length > 0 && !this.submitting;
  }

  onSubmit(): void {
    if (!this.canSubmit()) {
      return;
    }
    const key = this.value().trim();
    if (!this.closeOnOverlay || !this.closeOnSubmit) {
      this.submitted.emit(key);
      return;
    }
    dismissModal(this.modal, () => this.submitted.emit(key));
  }

  onCancel(): void {
    dismissModal(this.modal, () => this.cancelled.emit());
  }
}
