import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { PrepareRestoreResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { BackupService } from './backup.service';

function prepareFailure(code: string, message: string): PrepareRestoreResponse {
  return {
    success: false,
    error: { code, message },
    restoreId: '',
    backupId: '',
    effective: [],
    autoSelected: [],
    warnings: [],
    recoveryKeyRequired: code === 'RecoveryKeyRequired' || code === 'RecoveryKeyInvalid',
    safetyBackupCreated: false,
    catalog: [],
  };
}

describe('BackupService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let service: BackupService;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getBackups', 'createBackup', 'deleteBackup', 'downloadBackup',
      'getBackupSettings', 'updateBackupSettings',
      'getBackupRecoveryKeyState', 'createBackupRecoveryKey', 'acknowledgeBackupRecoveryKey', 'revealBackupRecoveryKey',
      'getBackupStatus', 'inspectBackup',
      'prepareRestore', 'commitRestore', 'cancelRestore', 'restartApplication',
      'onNotification',
    ]);
    apiSpy.onNotification.and.returnValue(EMPTY);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        BackupService,
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(BackupService);
  });

  function summary(id: string, createdAt: string) {
    return {
      id,
      providerId: 'local',
      providerDisplayName: 'This computer',
      name: `Backup ${id}`,
      createdAt,
      trigger: 'Manual',
      macroDeckVersion: '3.0.0',
      sizeBytes: 1024,
      formatVersion: 1,
      isRemote: false,
      decryptableLocally: true,
      imported: false,
      components: ['Profiles'],
    } as never;
  }

  describe('loaded state', () => {
    // Opening the pane polls the last status. Surfacing a finished one would announce a backup the user
    // completed long ago every single time they navigate here.
    it('does not surface an operation that had already finished before the pane was opened', async () => {
      apiSpy.getBackupStatus.and.resolveTo({
        operationId: 'op1',
        kind: 'Create',
        stage: 'Completed',
        trigger: 'Manual',
        updatedAt: '2026-08-17T03:00:00Z',
      } as never);

      await service.loadStatus();

      expect(service.operation()).toBeNull();
    });

    it('does surface an operation that is still running when the pane is opened', async () => {
      apiSpy.getBackupStatus.and.resolveTo({
        operationId: 'op1',
        kind: 'Create',
        stage: 'Encrypting',
        trigger: 'Manual',
        updatedAt: '2026-08-17T03:00:00Z',
      } as never);

      await service.loadStatus();

      expect(service.operation()?.stage).toBe('Encrypting');
    });

    it('orders backups newest first regardless of the order the host returned them in', async () => {
      apiSpy.getBackups.and.resolveTo({
        backups: [
          summary('middle', '2026-08-10T00:00:00Z'),
          summary('oldest', '2026-01-01T00:00:00Z'),
          summary('newest', '2026-08-17T00:00:00Z'),
        ],
        retentionKeepLatest: 7,
      } as never);

      await service.loadBackups();

      expect(service.backups().map(backup => backup.id)).toEqual(['newest', 'middle', 'oldest']);
    });
  });

  describe('prepareRestore recovery-key mapping (issue #36)', () => {
    it('maps a rejected recovery key to its own outcome, distinct from a generic error', async () => {
      apiSpy.prepareRestore.and.resolveTo(prepareFailure('RecoveryKeyInvalid', 'The recovery key is incorrect'));

      const outcome = await service.prepareRestore('b1', ['Profiles'], 'wrong-key');

      expect(outcome).toEqual({ status: 'recoveryKeyInvalid' });
    });

    it('maps a missing recovery key to its own outcome, distinct from an invalid one', async () => {
      apiSpy.prepareRestore.and.resolveTo(prepareFailure('RecoveryKeyRequired', 'A recovery key is required'));

      const outcome = await service.prepareRestore('b1', ['Profiles']);

      expect(outcome).toEqual({ status: 'recoveryKeyRequired' });
    });

    it('falls back to a generic error outcome for anything else', async () => {
      apiSpy.prepareRestore.and.resolveTo(prepareFailure('Busy', 'Another backup operation is already in progress'));

      const outcome = await service.prepareRestore('b1', ['Profiles']);

      expect(outcome).toEqual({ status: 'error', message: 'Another backup operation is already in progress' });
    });

    // Taken from the host rather than assembled here: whether the host can restart itself is part of the
    // staged state, and a client that invents it offers a Restart button that only fails once pressed.
    it('takes the staged state from the host, including whether it can restart itself', async () => {
      apiSpy.prepareRestore.and.resolveTo({
        success: true,
        restoreId: 'r1',
        backupId: 'b1',
        effective: ['Profiles', 'Icons'],
        autoSelected: ['Icons'],
        warnings: [],
        recoveryKeyRequired: false,
        safetyBackupCreated: true,
        catalog: [],
      });
      apiSpy.getBackups.and.resolveTo({
        backups: [],
        retentionKeepLatest: 7,
        pendingRestore: {
          restoreId: 'r1',
          backupId: 'b1',
          stagedAt: '2026-08-17T03:00:00Z',
          components: ['Profiles', 'Icons'],
          restartSupported: false,
          restartUnavailableReason: 'Restarting is only available in the installed desktop app',
        },
      } as never);

      const outcome = await service.prepareRestore('b1', ['Profiles', 'Icons']);

      expect(outcome.status).toBe('staged');
      expect(service.pendingRestore()?.restoreId).toBe('r1');
      expect(service.pendingRestore()?.restartSupported).toBeFalse();
    });
  });
});
