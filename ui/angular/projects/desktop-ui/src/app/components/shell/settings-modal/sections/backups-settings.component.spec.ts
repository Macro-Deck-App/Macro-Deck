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
      'getBackupStatus', 'inspectBackup', 'importBackup', 'prepareRestore', 'commitRestore', 'cancelRestore',
      'restartApplication',
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

  async function settle(): Promise<void> {
    for (let round = 0; round < 5; round++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await Promise.resolve();
    }
    fixture.detectChanges();
  }

  function recoveryKeyPrompt(): HTMLElement | null {
    return fixture.nativeElement.querySelector('shared-recovery-key-prompt-modal .modal-overlay:not(.closing)');
  }

  async function submitRecoveryKey(key: string): Promise<void> {
    const input = recoveryKeyPrompt()!.querySelector('input') as HTMLInputElement;
    input.value = key;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const continueButton = Array.from(recoveryKeyPrompt()!.querySelectorAll('shared-button button'))
      .find(el => (el as HTMLElement).textContent?.trim() === 'Continue') as HTMLButtonElement;
    expect(continueButton.disabled).withContext('Continue enabled after typing').toBeFalse();
    continueButton.click();
    await settle();
  }

  async function chooseImportFile(file: File): Promise<void> {
    const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
    await settle();
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
  describe('restore from another installation', () => {
    const foreign = backup('f', { decryptableLocally: false, imported: true });
    const catalog = [
      { id: 'Integrations' as const, requires: [] },
      { id: 'Plugins' as const, requires: ['Integrations' as const] },
      { id: 'Profiles' as const, requires: [] },
    ];

    function answerInspect(): void {
      api.inspectBackup.and.callFake(async (_id: string, key?: string) => {
        if (!key) {
          return { success: true, backup: foreign, recoveryKeyRequired: true, components: [], catalog };
        }
        if (key !== 'the-right-key') {
          return {
            success: false,
            error: { code: 'RecoveryKeyInvalid', message: 'x' },
            recoveryKeyRequired: false,
            components: [],
            catalog: [],
          };
        }
        return { success: true, backup: foreign, recoveryKeyRequired: true, components: [], catalog };
      });
    }

    it('asks for the recovery key, keeps asking after a wrong one, and restores with the right one', async () => {
      fixture = await create();
      answerInspect();
      api.prepareRestore.and.resolveTo({
        success: true, restoreId: 'r1', backupId: 'f', effective: ['Plugins', 'Integrations'], autoSelected: [],
        warnings: [], recoveryKeyRequired: true, safetyBackupCreated: true, catalog: [],
      });

      await fixture.componentInstance.onRestoreRequested(foreign);
      await settle();

      expect(recoveryKeyPrompt()).withContext('the prompt is the only way in').toBeTruthy();
      expect(fixture.debugElement.query(By.directive(BackupRestoreModalComponent))).toBeNull();

      await submitRecoveryKey('a-wrong-key');

      expect(recoveryKeyPrompt()).withContext('a rejected key must leave the prompt on screen').toBeTruthy();
      expect(recoveryKeyPrompt()!.textContent).toContain('That recovery key was not accepted');

      await submitRecoveryKey('the-right-key');

      const pickModal = fixture.debugElement.query(By.directive(BackupRestoreModalComponent));
      expect(pickModal).toBeTruthy();
      (pickModal.componentInstance as BackupRestoreModalComponent).confirmed.emit(['Plugins']);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal').textContent)
        .toContain('Macro Deck uses the recovery key you just entered');

      confirmationButton('Restore').click();
      fixture.detectChanges();
      jasmine.clock().tick(150);
      await fixture.whenStable();

      expect(api.prepareRestore).toHaveBeenCalledOnceWith({
        backupId: 'f', components: ['Plugins'], recoveryKey: 'the-right-key',
      });
    });

    it('does not claim the recovery key changes when the restore leaves the secrets alone', async () => {
      fixture = await create();
      answerInspect();

      await fixture.componentInstance.onRestoreRequested(foreign);
      await settle();
      await submitRecoveryKey('the-right-key');

      const pickModal = fixture.debugElement.query(By.directive(BackupRestoreModalComponent));
      (pickModal.componentInstance as BackupRestoreModalComponent).confirmed.emit(['Profiles']);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal').textContent)
        .not.toContain('Macro Deck uses the recovery key you just entered');
    });

    it('imports a chosen backup file and goes straight on to restoring it', async () => {
      fixture = await create();
      answerInspect();
      api.importBackup.and.resolveTo({ success: true, backup: foreign });

      await chooseImportFile(new File(['archive'], 'other.macroDeckBackup'));

      expect(api.importBackup).toHaveBeenCalledTimes(1);
      expect(toastSpy.show).toHaveBeenCalledWith('Backup imported', jasmine.objectContaining({ variant: 'success' }));
      expect(api.inspectBackup).toHaveBeenCalledWith('f', undefined);
      expect(recoveryKeyPrompt()).toBeTruthy();
    });

    it('explains a rejected import in the user\'s language', async () => {
      fixture = await create();
      api.importBackup.and.resolveTo({
        success: false,
        error: {
          code: 'InvalidArchive',
          message: { $localized: { scope: 'macrodeck.app', key: 'Errors.Backup.InvalidArchive' } } as never,
        },
      });

      await chooseImportFile(new File(['not a backup'], 'notes.macroDeckBackup'));

      expect(toastSpy.show).toHaveBeenCalledWith(
        'The file is not a valid Macro Deck backup', jasmine.objectContaining({ variant: 'error' }));
      expect(api.inspectBackup).not.toHaveBeenCalled();
    });

    it('reports an import that never reached the host without leaking transport details', async () => {
      fixture = await create();
      api.importBackup.and.rejectWith(new Error('Payload Too Large'));

      await chooseImportFile(new File(['archive'], 'big.macroDeckBackup'));

      expect(toastSpy.show).toHaveBeenCalledWith(
        'The backup could not be imported', jasmine.objectContaining({ variant: 'error' }));
    });
  });
});
