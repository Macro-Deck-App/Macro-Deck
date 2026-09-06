import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { AppStrings, BackupComponentCatalogEntry, BackupComponentGroupInfo, BackupDependencyWarningDto, BackupListChangedEvent, BackupOperationProgressEvent, BackupOperationStage, BackupRecoveryKeyStateChangedEvent, BackupSummary, GetBackupRecoveryKeyStateResponse, GetBackupSettingsResponse, GetBackupStatusResponse, PendingRestoreSummary, RestorePendingEvent, UpdateBackupSettingsRequest } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import type { BackupComponentGroup } from '@macro-deck/runtime';
import { downloadFile } from '../util/download-file';

const TERMINAL_STAGES: ReadonlySet<string> = new Set(['Completed', 'Failed']);

function sortNewestFirst(backups: readonly BackupSummary[]): BackupSummary[] {
  return [...backups].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt));
}

const ACTIVE_STAGES: ReadonlySet<BackupOperationStage> = new Set<BackupOperationStage>([
  'Preparing',
  'CreatingSnapshot',
  'Encrypting',
  'Saving',
  'Validating',
  'Restoring',
]);

export type BackupOperationResult = { ok: true } | { ok: false; error: string };

export type BackupRecoveryKeyOutcome =
  | { status: 'success'; key: string; state: GetBackupRecoveryKeyStateResponse }
  | { status: 'error'; message: string };

export type BackupInspectOutcome =
  | {
      status: 'success';
      backup: BackupSummary;
      components: BackupComponentGroupInfo[];
      catalog: BackupComponentCatalogEntry[];
    }
  | { status: 'recoveryKeyRequired' }
  | { status: 'recoveryKeyInvalid' }
  | { status: 'error'; message: string };

export type PrepareRestoreOutcome =
  | {
      status: 'staged';
      restoreId: string;
      backupId: string;
      effective: BackupComponentGroup[];
      warnings: BackupDependencyWarningDto[];
      safetyBackupCreated: boolean;
    }
  | { status: 'recoveryKeyRequired' }
  | { status: 'recoveryKeyInvalid' }
  | { status: 'error'; message: string };

export type CommitRestoreOutcome =
  | { status: 'committed'; restartRequested: boolean; restartSupported: boolean; restartUnavailableReason?: string }
  | { status: 'error'; message: string };

@Injectable({ providedIn: 'root' })
export class BackupService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly backups = signal<BackupSummary[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly retentionCount = signal(0);
  readonly pendingRestore = signal<PendingRestoreSummary | null>(null);
  readonly operation = signal<GetBackupStatusResponse | null>(null);
  readonly settings = signal<GetBackupSettingsResponse | null>(null);
  readonly recoveryKey = signal<GetBackupRecoveryKeyStateResponse | null>(null);

  readonly running: Signal<boolean> = computed(() => {
    const op = this.operation();
    return !!op && ACTIVE_STAGES.has(op.stage);
  });

  readonly canMutate: Signal<boolean> = computed(() => !this.running() && !this.pendingRestore());

  constructor() {
    this.subscribeToEvents();
  }

  dismissOperation(): void {
    this.operation.set(null);
  }

  async load(): Promise<void> {
    await Promise.all([this.loadBackups(), this.loadSettings(), this.loadRecoveryKeyState(), this.loadStatus()]);
  }

  async loadBackups(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const response = await this.api.getBackups();
      this.backups.set(sortNewestFirst(response.backups));
      this.retentionCount.set(response.retentionKeepLatest);
      this.pendingRestore.set(response.pendingRestore ?? null);
    } catch (error) {
      console.error('Failed to load backups:', error);
      this.loadError.set(this.localization.translateKey(AppStrings.Errors.Backup.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  async loadSettings(): Promise<void> {
    try {
      this.settings.set(await this.api.getBackupSettings());
    } catch (error) {
      console.error('Failed to load backup settings:', error);
    }
  }

  async loadRecoveryKeyState(): Promise<boolean> {
    try {
      this.recoveryKey.set(await this.api.getBackupRecoveryKeyState());
      return true;
    } catch (error) {
      console.error('Failed to load the backup recovery key state:', error);
      return false;
    }
  }

  async loadStatus(): Promise<void> {
    try {
      this.applyOperation(await this.api.getBackupStatus(), false);
    } catch (error) {
      console.error('Failed to load the backup operation status:', error);
    }
  }

  async createBackup(note?: string, protect = false): Promise<BackupOperationResult> {
    try {
      const response = await this.api.createBackup({ note, protected: protect });
      if (!response.success) {
        return { ok: false, error: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.CreateFailed) };
      }
      return { ok: true };
    } catch (error) {
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async deleteBackup(backupId: string): Promise<BackupOperationResult> {
    const previous = this.backups();
    this.backups.set(previous.filter(backup => backup.id !== backupId));
    try {
      const response = await this.api.deleteBackup(backupId);
      if (!response.success) {
        this.backups.set(previous);
        return { ok: false, error: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.DeleteFailed) };
      }
      return { ok: true };
    } catch (error) {
      this.backups.set(previous);
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async downloadBackup(backupId: string): Promise<BackupOperationResult> {
    try {
      const { blob, fileName } = await this.api.downloadBackup(backupId);
      // Not FileSaveService: see ApiService.downloadBackup - this must stay off the Tauri IPC bridge.
      downloadFile(blob, fileName);
      return { ok: true };
    } catch (error) {
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async inspectBackup(backupId: string, recoveryKey?: string): Promise<BackupInspectOutcome> {
    try {
      const response = await this.api.inspectBackup(backupId, recoveryKey);
      if (response.success && response.backup) {
        return { status: 'success', backup: response.backup, components: response.components, catalog: response.catalog };
      }
      return mapRecoveryKeyError(response.error?.code) ?? {
        status: 'error',
        message: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.InspectFailed),
      };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async prepareRestore(
    backupId: string,
    components: BackupComponentGroup[],
    recoveryKey?: string,
  ): Promise<PrepareRestoreOutcome> {
    try {
      const response = await this.api.prepareRestore({ backupId, components, recoveryKey });
      if (!response.success) {
        return mapRecoveryKeyError(response.error?.code) ?? {
          status: 'error',
          message: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.RestoreStartFailed),
        };
      }

      // Re-read rather than synthesising the staged state: whether the host can restart itself is part
      // of it, and guessing that here is what produced a Restart button that only failed once pressed.
      await this.loadBackups();

      return {
        status: 'staged',
        restoreId: response.restoreId,
        backupId: response.backupId,
        effective: response.effective,
        warnings: response.warnings,
        safetyBackupCreated: response.safetyBackupCreated,
      };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async commitRestore(restoreId: string): Promise<CommitRestoreOutcome> {
    try {
      const response = await this.api.commitRestore({ restoreId });
      if (!response.success) {
        return { status: 'error', message: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.RestoreApplyFailed) };
      }

      this.pendingRestore.set(null);
      if (response.restartRequested && response.restartSupported) {
        await this.api.restartApplication('backup-restore');
      }

      return {
        status: 'committed',
        restartRequested: response.restartRequested,
        restartSupported: response.restartSupported,
        restartUnavailableReason: response.restartUnavailableReason,
      };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async cancelRestore(restoreId: string): Promise<BackupOperationResult> {
    try {
      const response = await this.api.cancelRestore({ restoreId });
      if (!response.success) {
        return { ok: false, error: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.RestoreCancelFailed) };
      }
      this.pendingRestore.set(null);
      return { ok: true };
    } catch (error) {
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async updateSettings(request: UpdateBackupSettingsRequest): Promise<BackupOperationResult> {
    const previous = this.settings();
    try {
      const response = await this.api.updateBackupSettings(request);
      this.settings.set(response);
      return { ok: true };
    } catch (error) {
      this.settings.set(previous);
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async createRecoveryKey(): Promise<BackupRecoveryKeyOutcome> {
    try {
      const response = await this.api.createBackupRecoveryKey();
      if (!response.success || !response.key) {
        return { status: 'error', message: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.RecoveryKeyCreateFailed) };
      }
      const state = this.applyRecoveryKeyResponse(response);
      return { status: 'success', key: response.key, state };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async acknowledgeRecoveryKey(): Promise<BackupOperationResult> {
    try {
      const response = await this.api.acknowledgeBackupRecoveryKey();
      if (!response.success) {
        return { ok: false, error: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.RecoveryKeyAckFailed) };
      }
      this.applyRecoveryKeyResponse(response);
      return { ok: true };
    } catch (error) {
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async revealRecoveryKey(): Promise<BackupRecoveryKeyOutcome> {
    try {
      const response = await this.api.revealBackupRecoveryKey({ confirm: true });
      if (!response.success || !response.key) {
        return { status: 'error', message: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.RecoveryKeyRevealFailed) };
      }
      const state = this.applyRecoveryKeyResponse(response);
      return { status: 'success', key: response.key, state };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  private applyRecoveryKeyResponse(response: GetBackupRecoveryKeyStateResponse): GetBackupRecoveryKeyStateResponse {
    const state: GetBackupRecoveryKeyStateResponse = {
      availability: response.availability,
      keyId: response.keyId,
      createdAt: response.createdAt,
      exportedAt: response.exportedAt,
    };
    this.recoveryKey.set(state);
    return state;
  }

  private applyOperation(status: GetBackupStatusResponse, fromPush: boolean): void {
    if (status.stage === 'Idle' || (!fromPush && TERMINAL_STAGES.has(status.stage))) {
      this.operation.set(null);
      return;
    }

    this.operation.set(status);
  }

  private subscribeToEvents(): void {
    this.api.onNotification<BackupOperationProgressEvent>('BackupOperationProgressEvent').subscribe(event => {
      this.applyOperation(event, true);
    });

    this.api.onNotification<BackupListChangedEvent>('BackupListChangedEvent').subscribe(() => {
      void this.loadBackups();
    });

    this.api.onNotification<BackupRecoveryKeyStateChangedEvent>('BackupRecoveryKeyStateChangedEvent').subscribe(() => {
      void this.loadRecoveryKeyState();
    });

    this.api.onNotification<RestorePendingEvent>('RestorePendingEvent').subscribe(event => {
      if (event.pending) {
        void this.loadBackups();
      } else {
        this.pendingRestore.set(null);
      }
    });
  }
}

function mapRecoveryKeyError(
  code: string | undefined,
): { status: 'recoveryKeyRequired' } | { status: 'recoveryKeyInvalid' } | null {
  if (code === 'RecoveryKeyRequired') {
    return { status: 'recoveryKeyRequired' };
  }
  if (code === 'RecoveryKeyInvalid') {
    return { status: 'recoveryKeyInvalid' };
  }
  return null;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}
