import { ResultResponse } from './common';
import type { BackupComponentGroup } from '../../domain/backup-component-groups';

export type BackupOperationKind = 'Create' | 'Restore' | 'Import' | 'Export' | 'Delete';

export type BackupOperationStage =
  | 'Idle'
  | 'Preparing'
  | 'CreatingSnapshot'
  | 'Encrypting'
  | 'Saving'
  | 'Validating'
  | 'Restoring'
  | 'Completed'
  | 'Failed';

export type BackupTrigger =
  | 'Manual'
  | 'Scheduled'
  | 'BeforeHostUpdate'
  | 'BeforePluginUpdate'
  | 'BeforeRestore'
  | 'Imported';

export type BackupRecoveryKeyAvailability = 'None' | 'Available' | 'Missing';

export interface BackupSummary {
  id: string;
  providerId: string;
  providerDisplayName: string;
  name: string;
  createdAt: string;
  trigger: BackupTrigger;
  macroDeckVersion: string;
  sizeBytes: number;
  formatVersion: number;
  isRemote: boolean;
  decryptableLocally: boolean;
  imported: boolean;
  note?: string;
  components: BackupComponentGroup[];
}

export interface PendingRestoreSummary {
  restoreId: string;
  backupId: string;
  stagedAt: string;
  components: BackupComponentGroup[];
  restartSupported: boolean;
  restartUnavailableReason?: string | null;
}

export interface BackupComponentCatalogEntry {
  id: BackupComponentGroup;
  requires: BackupComponentGroup[];
}

export interface BackupComponentGroupInfo {
  id: BackupComponentGroup;
  entryCount: number;
  byteSize: number;
  requires: BackupComponentGroup[];
}

export interface BackupDependencyWarningDto {
  group: BackupComponentGroup;
  missingDependency: BackupComponentGroup;
}

export interface GetBackupsResponse {
  backups: BackupSummary[];
  retentionKeepLatest: number;
  pendingRestore?: PendingRestoreSummary;
}

export interface CreateBackupRequest {
  note?: string;
  protected?: boolean;
}

export interface CreateBackupResponse extends ResultResponse {
  backup?: BackupSummary;
}

export interface DeleteBackupResponse extends ResultResponse {}

export interface GetBackupSettingsResponse {
  scheduleFrequency: string;
  scheduleTimeOfDay: string;
  scheduleDayOfWeek: string;
  scheduleDayOfMonth: number;
  retentionPolicy: string;
  retentionKeepLatest: number;
  beforeHostUpdate: boolean;
  beforePluginUpdate: boolean;
  preUpdateBackupSupported: boolean;
  preUpdateBackupUnsupportedReason?: string;
  minimumRetentionCount: number;
  maximumRetentionCount: number;
  nextScheduledAt?: string;
}

export interface UpdateBackupSettingsRequest {
  scheduleFrequency?: string;
  scheduleTimeOfDay?: string;
  scheduleDayOfWeek?: string;
  scheduleDayOfMonth?: number;
  retentionPolicy?: string;
  retentionKeepLatest?: number;
  beforeHostUpdate?: boolean;
  beforePluginUpdate?: boolean;
}

export type UpdateBackupSettingsResponse = GetBackupSettingsResponse;

export interface GetBackupRecoveryKeyStateResponse {
  availability: BackupRecoveryKeyAvailability;
  keyId?: string;
  createdAt?: string;
  exportedAt?: string;
}

export interface RevealBackupRecoveryKeyRequest {
  confirm: boolean;
}

export interface BackupRecoveryKeyResponse extends ResultResponse {
  availability: BackupRecoveryKeyAvailability;
  keyId?: string;
  createdAt?: string;
  exportedAt?: string;
  key?: string;
}

export interface GetBackupStatusResponse {
  operationId?: string;
  kind: BackupOperationKind;
  stage: BackupOperationStage;
  trigger: BackupTrigger;
  percentComplete?: number;
  bytesProcessed?: number;
  totalBytes?: number;
  backupId?: string;
  error?: string;
  errorMessage?: string;
  updatedAt: string;
}

export interface InspectBackupRequest {
  recoveryKey?: string;
}

export interface InspectBackupResponse extends ResultResponse {
  backup?: BackupSummary;
  recoveryKeyRequired: boolean;
  components: BackupComponentGroupInfo[];
  catalog: BackupComponentCatalogEntry[];
}

export interface PrepareRestoreRequest {
  backupId: string;
  components: BackupComponentGroup[];
  recoveryKey?: string;
  proceedWithoutSafetyBackup?: boolean;
}

export interface PrepareRestoreResponse extends ResultResponse {
  restoreId: string;
  backupId: string;
  effective: BackupComponentGroup[];
  autoSelected: BackupComponentGroup[];
  warnings: BackupDependencyWarningDto[];
  recoveryKeyRequired: boolean;
  safetyBackupId?: string;
  safetyBackupCreated: boolean;
  catalog: BackupComponentCatalogEntry[];
}

export interface CommitRestoreRequest {
  restoreId: string;
}

export interface CommitRestoreResponse extends ResultResponse {
  restartRequested: boolean;
  restartSupported: boolean;
  restartUnavailableReason?: string;
}

export interface CancelRestoreRequest {
  restoreId: string;
}

export interface CancelRestoreResponse extends ResultResponse {}

export type BackupOperationProgressEvent = GetBackupStatusResponse;

export interface BackupListChangedEvent {
  reason: string;
}

export interface BackupRecoveryKeyStateChangedEvent {
  state: BackupRecoveryKeyAvailability;
  keyId?: string;
  exportedAt?: string;
}

export interface RestorePendingEvent {
  pending: boolean;
  restoreId?: string;
  backupId?: string;
  restartSupported: boolean;
  components: BackupComponentGroup[];
}
