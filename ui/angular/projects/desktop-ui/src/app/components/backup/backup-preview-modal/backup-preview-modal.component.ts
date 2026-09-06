import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { AppStrings, BackupComponentGroup, BackupSummary, BackupTrigger, backupComponentGroupLabel } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { formatBytes } from '../../../util/format-bytes';

const TRIGGER_LABEL_KEYS: Record<BackupTrigger, string> = {
  Manual: AppStrings.Backup.Trigger.Manual,
  Scheduled: AppStrings.Backup.Trigger.Scheduled,
  BeforeHostUpdate: AppStrings.Backup.Trigger.BeforeHostUpdate,
  BeforePluginUpdate: AppStrings.Backup.Trigger.BeforePluginUpdate,
  BeforeRestore: AppStrings.Backup.Trigger.BeforeRestore,
  Imported: AppStrings.Backup.Trigger.Imported,
};

@Component({
  selector: 'shared-backup-preview-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './backup-preview-modal.component.html',
  styleUrls: ['./backup-preview-modal.component.scss'],
})
export class BackupPreviewModalComponent {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true })
  set backup(value: BackupSummary) {
    this.backupState.set(value);
  }

  @Output() closed = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected groupLabel(id: BackupComponentGroup): string {
    return backupComponentGroupLabel(id, key => this.localization.translateKey(key));
  }
  protected readonly backupState = signal<BackupSummary | null>(null);

  protected readonly createdLabel = computed(() => {
    const backup = this.backupState();
    return backup ? new Date(backup.createdAt).toLocaleString() : '';
  });

  protected readonly sizeLabel = computed(() => formatBytes(this.backupState()?.sizeBytes));

  protected readonly triggerLabel = computed(() => {
    const backup = this.backupState();
    if (!backup) {
      return '';
    }
    const key = TRIGGER_LABEL_KEYS[backup.trigger];
    return key ? this.localization.translateKey(key) : backup.trigger;
  });

  protected readonly decryptLabel = computed(() =>
    this.localization.translateKey(
      this.backupState()?.decryptableLocally
        ? AppStrings.Backup.Preview.CanDecrypt
        : AppStrings.Backup.Preview.CannotDecrypt));

  onClose(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }
}
