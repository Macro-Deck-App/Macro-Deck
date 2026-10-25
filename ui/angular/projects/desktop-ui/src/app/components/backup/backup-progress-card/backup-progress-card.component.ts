import { ChangeDetectionStrategy, Component, Input, computed, inject, output, signal } from '@angular/core';
import { AppStrings, BackupOperationStage, BackupTrigger, GetBackupStatusResponse } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService } from '@shared';
import { backupEta, backupPercent, isActiveBackupStage } from '../../../util/backup-progress-display';
import { formatBytes } from '../../../util/format-bytes';

type StageState = 'done' | 'current' | 'pending';

interface StageRow {
  label: string;
  state: StageState;
}

const TRIGGER_LABEL_KEYS: Record<BackupTrigger, string> = {
  Manual: AppStrings.Settings.Backups.TriggerManual,
  Scheduled: AppStrings.Settings.Backups.TriggerScheduled,
  BeforeHostUpdate: AppStrings.Settings.Backups.TriggerBeforeHostUpdate,
  BeforePluginUpdate: AppStrings.Settings.Backups.TriggerBeforePluginUpdate,
  BeforeRestore: AppStrings.Settings.Backups.TriggerBeforeRestore,
  Imported: AppStrings.Settings.Backups.TriggerImported,
};

const CREATE_STAGES: readonly { key: string; stages: readonly BackupOperationStage[] }[] = [
  { key: AppStrings.Backup.Progress.StagePreparing, stages: ['Preparing', 'CreatingSnapshot'] },
  { key: AppStrings.Backup.Progress.StageCopying, stages: ['Encrypting', 'Saving'] },
  { key: AppStrings.Backup.Progress.StageVerifying, stages: ['Validating'] },
];

@Component({
  selector: 'shared-backup-progress-card',
  standalone: true,
  imports: [ButtonComponent],
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

  @Input()
  set cancellable(value: boolean) {
    this.canCancel.set(value);
  }

  @Input()
  set compact(value: boolean) {
    this.isCompact.set(value);
  }

  readonly cancel = output<void>();

  protected readonly status = signal<GetBackupStatusResponse | null>(null);
  private readonly canCancel = signal(false);
  protected readonly isCompact = signal(false);

  private readonly restoring = computed(() => {
    const op = this.status();
    return op?.kind === 'Restore' || (op?.kind === 'Create' && op.trigger === 'BeforeRestore');
  });

  protected readonly running = computed(() => {
    const op = this.status();
    return !!op && isActiveBackupStage(op.stage);
  });

  protected readonly isFailed = computed(() => this.status()?.stage === 'Failed');
  protected readonly isDone = computed(() => this.status()?.stage === 'Completed');

  protected readonly title = computed(() => this.localization.translateKey(this.restoring()
    ? AppStrings.Backup.Progress.TitleRestoring
    : AppStrings.Backup.Progress.TitleCreating));

  protected readonly subtitle = computed(() => {
    const op = this.status();
    if (!op || this.restoring()) {
      return null;
    }
    const key = TRIGGER_LABEL_KEYS[op.trigger];
    return key ? this.localization.translateKey(key) : null;
  });

  protected readonly percent = computed(() => this.running() ? backupPercent(this.status()) : null);

  protected readonly percentText = computed(() => {
    const percent = this.percent();
    return percent === null ? null : this.localization.translateKey(AppStrings.Backup.Progress.Percent, { percent });
  });

  protected readonly eta = computed(() => backupEta(this.status(), this.localization));

  protected readonly byteDetail = computed<string | null>(() => {
    const op = this.status();
    if (!op || !this.running() || op.bytesProcessed == null) {
      return null;
    }
    const processed = formatBytes(op.bytesProcessed);
    return op.totalBytes
      ? this.localization.translateKey(AppStrings.Backup.Progress.ByteDetail, { processed, total: formatBytes(op.totalBytes) })
      : processed;
  });

  protected readonly stages = computed<StageRow[]>(() => {
    const op = this.status();
    if (!op || !this.running()) {
      return [];
    }
    if (this.restoring()) {
      const inSafetyBackup = op.kind === 'Create';
      return [
        { label: this.localization.translateKey(AppStrings.Backup.Progress.CheckingBackup), state: inSafetyBackup ? 'done' : 'current' },
        { label: this.localization.translateKey(AppStrings.Backup.Progress.CreatingSafetyBackup), state: inSafetyBackup ? 'current' : 'pending' },
      ];
    }
    const current = CREATE_STAGES.findIndex(stage => stage.stages.includes(op.stage));
    return CREATE_STAGES.map((stage, index) => ({
      label: this.localization.translateKey(stage.key),
      state: index < current ? 'done' : index === current ? 'current' : 'pending',
    }));
  });

  protected readonly cancelLabel = computed(() => this.localization.translateKey(this.restoring()
    ? AppStrings.Backup.Progress.CancelRestore
    : AppStrings.Backup.Progress.Cancel));

  protected readonly showCancel = computed(() => this.canCancel() && this.running() && !!this.status()?.operationId);
}
