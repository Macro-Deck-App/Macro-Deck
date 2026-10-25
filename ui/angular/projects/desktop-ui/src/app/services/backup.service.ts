import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { AppStrings, BackupComponentCatalogEntry, BackupComponentGroupInfo, BackupDependencyWarningDto, BackupListChangedEvent, BackupOperationProgressEvent, BackupRecoveryKeyStateChangedEvent, BackupSummary, GetBackupRecoveryKeyStateResponse, GetBackupSettingsResponse, GetBackupStatusResponse, LocalizedText, PendingRestoreSummary, RestorePendingEvent, UpdateBackupSettingsRequest, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import type { BackupComponentGroup } from '@macro-deck/runtime';
import { isActiveBackupStage, isTerminalBackupStage } from '../util/backup-progress-display';
import { downloadFile } from '../util/download-file';
import { shellBridge } from '../util/shell-bridge';

const CANCELLED_ERROR = 'Cancelled';

function sortNewestFirst(backups: readonly BackupSummary[]): BackupSummary[] {
  return [...backups].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt));
}

export type BackupOperationResult = { ok: true } | { ok: false; error: string; cancelled?: boolean };

export type BackupRecoveryKeyOutcome =
  | { status: 'success'; key: string; state: GetBackupRecoveryKeyStateResponse }
  | { status: 'error'; message: string };

export type BackupDownloadOutcome =
  | { status: 'saved'; path?: string }
  | { status: 'canceled' }
  | { status: 'error'; message: string };

export type BackupImportOutcome = { ok: true; backup: BackupSummary } | { ok: false; error: string };

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
  | { status: 'cancelled' }
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
  readonly downloading = signal<ReadonlySet<string>>(new Set());
  private restoreOperationId: string | null = null;
  readonly progressDialogHidden = signal(false);

  readonly running: Signal<boolean> = computed(() => {
    const op = this.operation();
    return !!op && isActiveBackupStage(op.stage);
  });

  readonly canMutate: Signal<boolean> = computed(() => !this.running() && !this.pendingRestore());

  constructor() {
    this.subscribeToEvents();
  }

  dismissOperation(): void {
    this.operation.set(null);
    this.progressDialogHidden.set(false);
  }

  hideProgressDialog(): void {
    this.progressDialogHidden.set(true);
  }

  showProgressDialog(): void {
    this.progressDialogHidden.set(false);
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
        return {
          ok: false,
          error: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Backup.CreateFailed),
          cancelled: response.error?.code === CANCELLED_ERROR,
        };
      }
      return { ok: true };
    } catch (error) {
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async cancelOperation(operationId: string): Promise<void> {
    try {
      await this.api.cancelBackupOperation(operationId);
    } catch (error) {
      console.error('Failed to cancel the backup operation:', error);
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

  async downloadBackup(backup: BackupSummary): Promise<BackupDownloadOutcome> {
    if (this.downloading().has(backup.id)) {
      return { status: 'canceled' };
    }
    this.setDownloading(backup.id, true);
    try {
      const shell = shellBridge();
      if (shell?.saveBackup) {
        const result = await shell.saveBackup({ backupId: backup.id, fileName: backup.name });
        if (result.saved) {
          return { status: 'saved', path: result.path ?? undefined };
        }
        if (result.canceled) {
          return { status: 'canceled' };
        }
        if (!result.unavailable) {
          console.error('Failed to save the backup:', result.error);
          return { status: 'error', message: this.localization.translateKey(AppStrings.Errors.FileSave.WriteFailed) };
        }
      }
      // A backup can be gigabytes, so it never goes through FileSaveService: that sends the whole file over IPC.
      const { blob, fileName } = await this.api.downloadBackup(backup.id);
      downloadFile(blob, fileName);
      return { status: 'saved' };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    } finally {
      this.setDownloading(backup.id, false);
    }
  }

  private setDownloading(backupId: string, active: boolean): void {
    const next = new Set(this.downloading());
    if (active) {
      next.add(backupId);
    } else {
      next.delete(backupId);
    }
    this.downloading.set(next);
  }

  async inspectBackup(backupId: string, recoveryKey?: string): Promise<BackupInspectOutcome> {
    try {
      const response = await this.api.inspectBackup(backupId, recoveryKey);
      if (response.success && response.backup) {
        if (response.recoveryKeyRequired && !recoveryKey) {
          return { status: 'recoveryKeyRequired' };
        }
        return { status: 'success', backup: response.backup, components: response.components, catalog: response.catalog };
      }
      return mapRecoveryKeyError(response.error?.code) ?? {
        status: 'error',
        message: this.hostMessage(response.error?.message, AppStrings.Errors.Backup.InspectFailed),
      };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Backup.OperationFailed)) };
    }
  }

  async importBackup(file: File): Promise<BackupImportOutcome> {
    try {
      const response = await this.api.importBackup(file);
      if (!response.success || !response.backup) {
        return { ok: false, error: this.hostMessage(response.error?.message, AppStrings.Errors.Backup.ImportFailed) };
      }
      await this.loadBackups();
      return { ok: true, backup: response.backup };
    } catch (error) {
      console.error('Failed to import a backup:', error);
      return { ok: false, error: this.localization.translateKey(AppStrings.Errors.Backup.ImportFailed) };
    }
  }

  async prepareRestore(
    backupId: string,
    components: BackupComponentGroup[],
    recoveryKey?: string,
  ): Promise<PrepareRestoreOutcome> {
    try {
      const response = await this.api.prepareRestore({ backupId, components, recoveryKey });
      if (!response.success && response.error?.code === CANCELLED_ERROR) {
        return { status: 'cancelled' };
      }
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
    if (status.stage === 'Idle' || (!fromPush && isTerminalBackupStage(status.stage))) {
      this.progressDialogHidden.set(false);
      this.operation.set(null);
      return;
    }

    if (status.kind === 'Restore') {
      this.restoreOperationId = isActiveBackupStage(status.stage) ? status.operationId ?? null : null;
    } else if (this.restoreOperationId && status.trigger === 'BeforeRestore' && isTerminalBackupStage(status.stage)) {
      // The safety backup runs inside a restore: its end is not the end of what the user started.
      return;
    }

    if (!isActiveBackupStage(status.stage)) {
      this.progressDialogHidden.set(false);
    }
    this.operation.set(status);
  }

  private hostMessage(message: LocalizedText | undefined, fallback: string): string {
    return resolveLocalizedText(message, this.localization) || this.localization.translateKey(fallback);
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
