import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { EMPTY } from 'rxjs';
import { BackupSummary } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { BackupRestoreModalComponent } from '../../../backup/backup-restore-modal/backup-restore-modal.component';
import { BackupService } from '../../../../services/backup.service';
import { BackupsSettingsComponent } from './backups-settings.component';

function backup(id: string, overrides: Partial<BackupSummary> = {}): BackupSummary {
  return {
    id,
    providerId: 'local',
    providerDisplayName: 'This computer',
    name: `Backup ${id}`,
    createdAt: '2026-08-17T03:00:00Z',
    trigger: 'Manual',
    macroDeckVersion: '3.0.0',
    sizeBytes: 1024,
    formatVersion: 1,
    isRemote: false,
    decryptableLocally: true,
    imported: false,
    components: ['Profiles'],
    ...overrides,
  };
}

describe('BackupsSettingsComponent', () => {
  let fixture: ComponentFixture<BackupsSettingsComponent>;
  let api: jasmine.SpyObj<ApiService>;
  let toastSpy: jasmine.SpyObj<ToastService>;

  beforeEach(() => jasmine.clock().install());
  afterEach(() => jasmine.clock().uninstall());

  async function create(): Promise<ComponentFixture<BackupsSettingsComponent>> {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getBackups', 'createBackup', 'deleteBackup', 'downloadBackup',
      'getBackupSettings', 'updateBackupSettings',
      'getBackupRecoveryKeyState', 'createBackupRecoveryKey', 'acknowledgeBackupRecoveryKey', 'revealBackupRecoveryKey',
      'getBackupStatus', 'inspectBackup', 'prepareRestore', 'commitRestore', 'cancelRestore', 'restartApplication',
      'onNotification',
    ]);
    api.onNotification.and.returnValue(EMPTY);
    api.getBackups.and.resolveTo({ backups: [backup('a'), backup('b')], retentionKeepLatest: 7 });
    api.getBackupSettings.and.resolveTo({
      scheduleFrequency: 'off',
      scheduleTimeOfDay: '03:00',
      scheduleDayOfWeek: 'Sunday',
      scheduleDayOfMonth: 1,
      retentionPolicy: 'keep-latest',
      retentionKeepLatest: 7,
      beforeHostUpdate: false,
      beforePluginUpdate: false,
      preUpdateBackupSupported: true,
      minimumRetentionCount: 1,
      maximumRetentionCount: 100,
    });
    api.getBackupRecoveryKeyState.and.resolveTo({
      availability: 'Available', keyId: 'k1', createdAt: '2026-08-01T00:00:00Z', exportedAt: '2026-08-01T00:00:00Z',
    });
    api.getBackupStatus.and.resolveTo({ kind: 'Create', stage: 'Idle', trigger: 'Manual', updatedAt: '2026-08-17T03:00:00Z' });

    toastSpy = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      imports: [BackupsSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: toastSpy },
      ],
    });

    const f = TestBed.createComponent(BackupsSettingsComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  function confirmationButton(label: string): HTMLButtonElement {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-confirmation-modal shared-button button'))
      .find(el => (el as HTMLElement).textContent?.trim() === label) as HTMLButtonElement;
  }

  describe('operation feedback', () => {
    async function pushStatus(stage: string): Promise<void> {
      const service = TestBed.inject(BackupService);
      service.operation.set({
        operationId: 'op1',
        kind: 'Create',
        stage: stage as never,
        trigger: 'Manual',
        updatedAt: '2026-08-17T03:00:00Z',
        errorMessage: stage === 'Failed' ? 'The disk is full' : undefined,
      });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('shows a running operation in a modal rather than inline in the pane', async () => {
      fixture = await create();

      await pushStatus('Encrypting');

      const modal = fixture.nativeElement.querySelector('shared-modal');
      expect(modal).toBeTruthy();
      expect(modal.textContent).toContain('Encrypting');
    });

    // The complaint that started this: a finished operation used to leave a large empty box behind.
    it('closes the modal on success and reports it as a toast instead', async () => {
      fixture = await create();
      await pushStatus('Encrypting');

      await pushStatus('Completed');

      expect(fixture.nativeElement.querySelector('shared-modal')).toBeNull();
      expect(toastSpy.show).toHaveBeenCalled();
      expect(TestBed.inject(BackupService).operation()).toBeNull();
    });

    it('keeps a failure on screen with its reason until it is dismissed', async () => {
      fixture = await create();

      await pushStatus('Failed');

      const modal = fixture.nativeElement.querySelector('shared-modal');
      expect(modal).toBeTruthy();
      expect(modal.textContent).toContain('The disk is full');
      expect(TestBed.inject(BackupService).operation()).not.toBeNull();
    });
  });

  describe('delete (issue #36, spec 1)', () => {
    it('calls the API only after confirmation, once with the second backup\'s id, and never after cancelling', async () => {
      fixture = await create();
      api.deleteBackup.and.resolveTo({ success: true });

      const secondBackup = backup('b');
      fixture.componentInstance.onDeleteRequested(secondBackup);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();
      expect(api.deleteBackup).not.toHaveBeenCalled();

      confirmationButton('Delete').click();
      fixture.detectChanges();
      jasmine.clock().tick(150);
      await fixture.whenStable();

      expect(api.deleteBackup).toHaveBeenCalledOnceWith('b');
    });

    it('never calls the API when the confirmation is cancelled', async () => {
      fixture = await create();

      fixture.componentInstance.onDeleteRequested(backup('a'));
      fixture.detectChanges();

      confirmationButton('Cancel').click();
      fixture.detectChanges();
      jasmine.clock().tick(150);
      await fixture.whenStable();

      expect(api.deleteBackup).not.toHaveBeenCalled();
    });
  });

  describe('restore (issue #36, spec 2)', () => {
    it('requires the group modal and then the danger confirmation, and passes only the selected groups', async () => {
      fixture = await create();
      api.inspectBackup.and.resolveTo({
        success: true,
        backup: backup('a'),
        recoveryKeyRequired: false,
        components: [],
        catalog: [
          { id: 'Profiles', requires: [] },
          { id: 'Icons', requires: [] },
        ],
      });
      api.prepareRestore.and.resolveTo({
        success: true,
        restoreId: 'r1',
        backupId: 'a',
        effective: ['Icons'],
        autoSelected: [],
        warnings: [],
        recoveryKeyRequired: false,
        safetyBackupCreated: true,
        catalog: [],
      });

      await fixture.componentInstance.onRestoreRequested(backup('a'));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const pickModal = fixture.debugElement.query(By.directive(BackupRestoreModalComponent));
      expect(pickModal).toBeTruthy();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeNull();
      expect(api.prepareRestore).not.toHaveBeenCalled();

      // Group picker's own "Continue" only advances to the danger confirmation - it never restores by itself.
      (pickModal.componentInstance as BackupRestoreModalComponent).confirmed.emit(['Icons']);
      fixture.detectChanges();

      expect(fixture.debugElement.query(By.directive(BackupRestoreModalComponent))).toBeNull();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();
      expect(confirmationButton('Restore')).toBeTruthy();
      expect(api.prepareRestore).not.toHaveBeenCalled();

      confirmationButton('Restore').click();
      fixture.detectChanges();
      jasmine.clock().tick(150);
      await fixture.whenStable();

      expect(api.prepareRestore).toHaveBeenCalledOnceWith({ backupId: 'a', components: ['Icons'], recoveryKey: undefined });
    });

    it('never calls prepareRestore when the danger confirmation is cancelled', async () => {
      fixture = await create();
      api.inspectBackup.and.resolveTo({
        success: true,
        backup: backup('a'),
        recoveryKeyRequired: false,
        components: [],
        catalog: [{ id: 'Profiles', requires: [] }],
      });

      await fixture.componentInstance.onRestoreRequested(backup('a'));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const pickModal = fixture.debugElement.query(By.directive(BackupRestoreModalComponent));
      (pickModal.componentInstance as BackupRestoreModalComponent).confirmed.emit(['Profiles']);
      fixture.detectChanges();

      confirmationButton('Cancel').click();
      fixture.detectChanges();
      jasmine.clock().tick(150);
      await fixture.whenStable();

      expect(api.prepareRestore).not.toHaveBeenCalled();
    });
  });
});
