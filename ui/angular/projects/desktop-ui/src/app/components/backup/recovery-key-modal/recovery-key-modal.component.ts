import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, CheckboxComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { downloadFile } from '../../../util/download-file';
import { CopyValueComponent } from '../../copy-value/copy-value.component';

const FILE_NAME = 'macro-deck-recovery-key.txt';

@Component({
  selector: 'shared-recovery-key-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, CheckboxComponent, CopyValueComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recovery-key-modal.component.html',
  styleUrls: ['./recovery-key-modal.component.scss'],
})
export class RecoveryKeyModalComponent {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true }) mode: 'created' | 'revealed' = 'revealed';
  @Input({ required: true }) key = '';
  @Input() requireAcknowledgement = false;

  @Output() acknowledged = new EventEmitter<void>();
  @Output() closed = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly stored = signal(false);

  protected readonly heading = computed(() =>
    this.localization.translateKey(
      this.mode === 'created' ? AppStrings.Backup.RecoveryKey.CreatedTitle : AppStrings.Backup.RecoveryKey.RevealedTitle));

  protected get showAcknowledgementCheckbox(): boolean {
    return this.mode === 'created' || this.requireAcknowledgement;
  }

  protected readonly canConfirm = computed(() => !this.showAcknowledgementCheckbox || this.stored());

  protected downloadKey(): void {
    downloadFile(new Blob([this.key], { type: 'text/plain' }), FILE_NAME);
  }

  onConfirm(): void {
    if (!this.canConfirm()) {
      return;
    }
    if (this.mode === 'created' || this.requireAcknowledgement) {
      dismissModal(this.modal, () => this.acknowledged.emit());
    } else {
      dismissModal(this.modal, () => this.closed.emit());
    }
  }

  onClose(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }
}
