import { ChangeDetectionStrategy, Component, Input, computed, inject, signal } from '@angular/core';
import { AppStrings, BackupOperationStage, GetBackupStatusResponse } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { formatBytes } from '../../../util/format-bytes';

const PHASE_LABEL_KEYS: Partial<Record<BackupOperationStage, string>> = {
  Preparing: AppStrings.Backup.Phase.Preparing,
  CreatingSnapshot: AppStrings.Backup.Phase.CreatingSnapshot,
  Encrypting: AppStrings.Backup.Phase.Encrypting,
  Saving: AppStrings.Backup.Phase.Saving,
  Validating: AppStrings.Backup.Phase.Validating,
  Restoring: AppStrings.Backup.Phase.Restoring,
  Completed: AppStrings.Backup.Phase.Completed,
  Failed: AppStrings.Backup.Phase.Failed,
};

@Component({
  selector: 'shared-backup-progress-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './backup-progress-card.component.html',
  styleUrls: ['./backup-progress-card.component.scss'],
})
export class BackupProgressCardComponent {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true })
  set operation(value: GetBackupStatusResponse) {
    this.status.set(value);
  }

  protected readonly status = signal<GetBackupStatusResponse | null>(null);

  protected readonly phaseText = computed(() => {
    const op = this.status();
    if (!op || op.stage === 'Idle') {
      return '';
    }
    const key = PHASE_LABEL_KEYS[op.stage];
    return key ? this.localization.translateKey(key) : op.stage;
  });

  protected readonly isFailed = computed(() => this.status()?.stage === 'Failed');
  protected readonly isDone = computed(() => this.status()?.stage === 'Completed');

  protected readonly percent = computed<number | null>(() => {
    const op = this.status();
    if (!op) {
      return null;
    }
    if (op.percentComplete !== undefined && op.percentComplete !== null) {
      return clampPercent(op.percentComplete);
    }
    if (op.bytesProcessed != null && op.totalBytes) {
      return clampPercent(Math.round((op.bytesProcessed / op.totalBytes) * 100));
    }
    return null;
  });

  protected readonly byteDetail = computed<string | null>(() => {
    const op = this.status();
    if (!op || op.bytesProcessed == null) {
      return null;
    }
    const processed = formatBytes(op.bytesProcessed);
    return op.totalBytes
      ? this.localization.translateKey(AppStrings.Backup.Progress.ByteDetail, { processed, total: formatBytes(op.totalBytes) })
      : processed;
  });
}

function clampPercent(value: number): number {
  return Math.min(100, Math.max(0, Math.round(value)));
}
