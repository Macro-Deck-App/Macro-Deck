import { BackupOperationStage, BackupTrigger, GetBackupStatusResponse, StoreOperationBody } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { formatEta } from './store-operation-display';

export const BACKUP_CANCEL_KEY_PREFIX = 'backup:';

const ACTIVE_STAGES: ReadonlySet<BackupOperationStage> = new Set<BackupOperationStage>([
  'Preparing',
  'CreatingSnapshot',
  'Encrypting',
  'Saving',
  'Validating',
  'Restoring',
]);

export function isActiveBackupStage(stage: BackupOperationStage): boolean {
  return ACTIVE_STAGES.has(stage);
}

export function isTerminalBackupStage(stage: BackupOperationStage): boolean {
  return stage === 'Completed' || stage === 'Failed';
}

export function backupPercent(operation: GetBackupStatusResponse | null | undefined): number | null {
  if (!operation) {
    return null;
  }
  if (operation.percentComplete !== undefined && operation.percentComplete !== null) {
    return clampPercent(operation.percentComplete);
  }
  if (operation.bytesProcessed != null && operation.totalBytes) {
    return clampPercent((operation.bytesProcessed / operation.totalBytes) * 100);
  }
  return null;
}

export function backupEta(operation: GetBackupStatusResponse | null | undefined,
  localization: LocalizationService): string | null {
  return operation && isActiveBackupStage(operation.stage) ? formatEta(operation.etaSeconds, localization) : null;
}

export function isActiveBackupFor(operation: GetBackupStatusResponse | null | undefined,
  trigger: BackupTrigger): operation is GetBackupStatusResponse {
  return !!operation && operation.kind === 'Create' && operation.trigger === trigger
    && isActiveBackupStage(operation.stage);
}

export function storeOperationBackup(storeOperation: StoreOperationBody | null | undefined,
  backup: GetBackupStatusResponse | null | undefined): GetBackupStatusResponse | null {
  return storeOperation?.state === 'BackingUp' && isActiveBackupFor(backup, 'BeforePluginUpdate') ? backup : null;
}

function clampPercent(value: number): number {
  return Math.min(100, Math.max(0, Math.round(value)));
}
