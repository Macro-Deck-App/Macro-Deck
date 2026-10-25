import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';
import { GetBackupStatusResponse, PrepareRestoreResponse } from '@macro-deck/runtime';
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
  describe('downloading a backup', () => {
    const shellWindow = window as { macroDeckShell?: unknown };
    let saveBackup: jasmine.Spy;

    afterEach(() => delete shellWindow.macroDeckShell);

    function withShell(result: object): void {
      saveBackup = jasmine.createSpy('saveBackup').and.resolveTo(result);
      shellWindow.macroDeckShell = { saveBackup };
    }

    it('asks the app where to save it and reports the chosen path', async () => {
      withShell({ saved: true, canceled: false, path: '/Users/me/b1.macroDeckBackup', error: null });

      const outcome = await service.downloadBackup(summary('b1', '2026-08-17T03:00:00Z') as never);

      expect(saveBackup).toHaveBeenCalledOnceWith({ backupId: 'b1', fileName: 'Backup b1' });
      expect(outcome).toEqual({ status: 'saved', path: '/Users/me/b1.macroDeckBackup' });
      expect(apiSpy.downloadBackup).not.toHaveBeenCalled();
      expect(service.downloading().size).toBe(0);
    });

    it('does nothing more when the save dialog is cancelled', async () => {
      withShell({ saved: false, canceled: true, path: null, error: null });

      const outcome = await service.downloadBackup(summary('b1', '2026-08-17T03:00:00Z') as never);

      expect(outcome).toEqual({ status: 'canceled' });
      expect(apiSpy.downloadBackup).not.toHaveBeenCalled();
    });

    it('reports a failed save in the user\'s language rather than the app\'s diagnostic text', async () => {
      withShell({ saved: false, canceled: false, path: null, error: 'Permission denied (os error 13)' });

      const outcome = await service.downloadBackup(summary('b1', '2026-08-17T03:00:00Z') as never);

      expect(outcome).toEqual({ status: 'error', message: 'The file could not be written' });
      expect(service.downloading().size).withContext('Download is usable again').toBe(0);
    });

    it('falls back to a browser download when the app cannot reach the host itself', async () => {
      withShell({ saved: false, canceled: false, unavailable: true, path: null, error: null });
      apiSpy.downloadBackup.and.resolveTo({ blob: new Blob(['x']), fileName: 'b1.macroDeckBackup' });
      spyOn(HTMLAnchorElement.prototype, 'click');

      const outcome = await service.downloadBackup(summary('b1', '2026-08-17T03:00:00Z') as never);

      expect(apiSpy.downloadBackup).toHaveBeenCalledOnceWith('b1');
      expect(outcome).toEqual({ status: 'saved' });
    });

    it('downloads in the browser when there is no app around it', async () => {
      apiSpy.downloadBackup.and.resolveTo({ blob: new Blob(['x']), fileName: 'b1.macroDeckBackup' });
      spyOn(HTMLAnchorElement.prototype, 'click');

      const outcome = await service.downloadBackup(summary('b1', '2026-08-17T03:00:00Z') as never);

      expect(apiSpy.downloadBackup).toHaveBeenCalledOnceWith('b1');
      expect(outcome).toEqual({ status: 'saved' });
    });
  });
});

describe('BackupService during a restore', () => {
  let progress: Subject<GetBackupStatusResponse>;
  let service: BackupService;

  beforeEach(() => {
    progress = new Subject<GetBackupStatusResponse>();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.callFake(((type: string) =>
      type === 'BackupOperationProgressEvent' ? progress.asObservable() : EMPTY) as never);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), BackupService, { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(BackupService);
  });

  function status(overrides: Partial<GetBackupStatusResponse>): GetBackupStatusResponse {
    return { kind: 'Create', stage: 'Saving', trigger: 'BeforeRestore', updatedAt: '2026-01-01T00:00:00Z', ...overrides };
  }

  it('keeps the restore running when its safety backup finishes', () => {
    progress.next(status({ operationId: 'restore', kind: 'Restore', stage: 'Restoring' }));
    progress.next(status({ operationId: 'safety', stage: 'Saving', percentComplete: 90 }));

    progress.next(status({ operationId: 'safety', stage: 'Completed', percentComplete: 100 }));

    expect(service.running()).toBeTrue();
    expect(service.operation()?.stage).not.toBe('Completed');
  });

  it('keeps the restore running when it goes on after a failed safety backup', () => {
    progress.next(status({ operationId: 'restore', kind: 'Restore', stage: 'Restoring' }));
    progress.next(status({ operationId: 'safety', stage: 'Saving' }));

    progress.next(status({ operationId: 'safety', stage: 'Failed', errorMessage: 'Disk full' }));

    expect(service.operation()?.stage).not.toBe('Failed');
  });

  it('ends with the restore itself', () => {
    progress.next(status({ operationId: 'restore', kind: 'Restore', stage: 'Restoring' }));
    progress.next(status({ operationId: 'safety', stage: 'Completed', percentComplete: 100 }));

    progress.next(status({ operationId: 'restore', kind: 'Restore', stage: 'Completed' }));

    expect(service.operation()?.kind).toBe('Restore');
    expect(service.operation()?.stage).toBe('Completed');
    expect(service.running()).toBeFalse();
  });

  it('shows the next backup\'s dialog again when the previous one ended unseen', () => {
    progress.next(status({ operationId: 'a', trigger: 'Manual', stage: 'Saving' }));
    service.hideProgressDialog();

    progress.next(status({ operationId: 'a', trigger: 'Manual', stage: 'Idle' }));

    expect(service.progressDialogHidden()).toBeFalse();
  });

  it('still reports a finished backup that is not part of a restore', () => {
    progress.next(status({ operationId: 'manual', trigger: 'Manual', stage: 'Completed' }));

    expect(service.operation()?.stage).toBe('Completed');
  });
});
