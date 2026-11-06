import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetBackupRecoveryKeyStateResponse } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { BackupService } from '../../../../services/backup.service';
import { BackupRecoveryKeySettingsComponent } from './backup-recovery-key-settings.component';

function state(overrides: Partial<GetBackupRecoveryKeyStateResponse> = {}): GetBackupRecoveryKeyStateResponse {
  return {
    availability: 'Available',
    keyId: 'k1',
    createdAt: '2026-08-01T00:00:00Z',
    exportedAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

describe('BackupRecoveryKeySettingsComponent', () => {
  let fixture: ComponentFixture<BackupRecoveryKeySettingsComponent>;
  let backupServiceSpy: jasmine.SpyObj<BackupService>;
  let stateSignal: WritableSignal<GetBackupRecoveryKeyStateResponse | null>;

  beforeEach(() => jasmine.clock().install());
  afterEach(() => jasmine.clock().uninstall());

  function configure(initial: GetBackupRecoveryKeyStateResponse | null): void {
    backupServiceSpy = jasmine.createSpyObj<BackupService>(
      'BackupService',
      ['createRecoveryKey', 'revealRecoveryKey', 'acknowledgeRecoveryKey']);
    stateSignal = signal(initial);
    Object.defineProperty(backupServiceSpy, 'recoveryKey', { value: stateSignal });

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.returnValue(EMPTY);

    TestBed.configureTestingModule({
      imports: [BackupRecoveryKeySettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: BackupService, useValue: backupServiceSpy },
        { provide: ApiService, useValue: apiSpy },
      ],
    });
  }

  function create(): ComponentFixture<BackupRecoveryKeySettingsComponent> {
    const f = TestBed.createComponent(BackupRecoveryKeySettingsComponent);
    f.detectChanges();
    return f;
  }

  function showKeyButton(): HTMLButtonElement {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-button button'))
      .find(el => (el as HTMLElement).textContent?.trim() === 'Show recovery key') as HTMLButtonElement;
  }

  function confirmRevealButton(): HTMLButtonElement {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-confirmation-modal shared-button button'))
      .find(el => (el as HTMLElement).textContent?.trim() === 'Show key') as HTMLButtonElement;
  }

  it('does not fetch or render the key until the reveal confirmation is accepted (issue #36)', () => {
    configure(state());
    fixture = create();

    expect(fixture.nativeElement.querySelector('shared-recovery-key-modal')).toBeNull();

    showKeyButton().click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('shared-recovery-key-modal')).toBeNull();
    expect(backupServiceSpy.revealRecoveryKey).not.toHaveBeenCalled();
  });

  it('fetches and shows the key only after the reveal is confirmed', async () => {
    configure(state());
    backupServiceSpy.revealRecoveryKey.and.resolveTo({
      status: 'success', key: 'MDBK-REVEALED-0000', state: state(),
    });
    fixture = create();

    showKeyButton().click();
    fixture.detectChanges();
    confirmRevealButton().click();
    fixture.detectChanges();
    jasmine.clock().tick(150);

    expect(backupServiceSpy.revealRecoveryKey).toHaveBeenCalledTimes(1);

    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent as string).toContain('MDBK-REVEALED-0000');
  });

  it('cancelling the confirmation never fetches the key', () => {
    configure(state());
    fixture = create();

    showKeyButton().click();
    fixture.detectChanges();

    const cancelButton = Array.from(fixture.nativeElement.querySelectorAll('shared-confirmation-modal shared-button button'))
      .find(el => (el as HTMLElement).textContent?.trim() === 'Cancel') as HTMLButtonElement;
    cancelButton.click();
    fixture.detectChanges();
    jasmine.clock().tick(150);

    expect(backupServiceSpy.revealRecoveryKey).not.toHaveBeenCalled();
  });

  it('shows a prominent warning while the key has never been shown', () => {
    configure(state({ exportedAt: undefined }));
    fixture = create();

    expect(fixture.nativeElement.textContent as string).toContain('never been shown');
  });

  describe('when the stored key cannot be read', () => {
    const missing = () => state({ availability: 'Missing', keyId: undefined });

    function button(selector: string, text: string): HTMLButtonElement | undefined {
      return Array.from(fixture.nativeElement.querySelectorAll(`${selector} shared-button button`))
        .find(el => (el as HTMLElement).textContent?.trim() === text) as HTMLButtonElement | undefined;
    }

    async function confirmRegenerate(): Promise<void> {
      button('', 'Create new recovery key')!.click();
      fixture.detectChanges();
      button('shared-confirmation-modal', 'Create new recovery key')!.click();
      fixture.detectChanges();
      jasmine.clock().tick(150);
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('offers to create a new key and asks before replacing anything', () => {
      configure(missing());
      fixture = create();

      button('', 'Create new recovery key')!.click();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('shared-confirmation-modal').textContent as string)
        .toContain('Backups made before still need the recovery key you saved earlier');
      expect(backupServiceSpy.createRecoveryKey).not.toHaveBeenCalled();
    });

    it('shows the new key to be stored once confirmed', async () => {
      configure(missing());
      backupServiceSpy.createRecoveryKey.and.resolveTo({
        status: 'success', key: 'MDBK-NEW-0000', state: state({ exportedAt: undefined }),
      });
      fixture = create();

      await confirmRegenerate();

      expect(backupServiceSpy.createRecoveryKey).toHaveBeenCalledTimes(1);
      const modal = fixture.nativeElement.querySelector('shared-recovery-key-modal');
      expect(modal.textContent as string).toContain('MDBK-NEW-0000');
      expect(modal.textContent as string).toContain('I have stored this key in a safe place');
    });

    it('cancelling leaves the key alone', () => {
      configure(missing());
      fixture = create();

      button('', 'Create new recovery key')!.click();
      fixture.detectChanges();
      button('shared-confirmation-modal', 'Cancel')!.click();
      fixture.detectChanges();
      jasmine.clock().tick(150);

      expect(backupServiceSpy.createRecoveryKey).not.toHaveBeenCalled();
    });

    it('shows a key that a backup created in the meantime, without reporting a failure', async () => {
      configure(missing());
      backupServiceSpy.createRecoveryKey.and.callFake(async () => {
        const fromBackup = state({ keyId: 'k-from-backup', exportedAt: undefined });
        stateSignal.set(fromBackup);
        return { status: 'exists', state: fromBackup };
      });
      fixture = create();
      const toasts = TestBed.inject(ToastService);
      spyOn(toasts, 'show');

      await confirmRegenerate();

      expect(toasts.show).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('shared-recovery-key-modal')).toBeNull();
      expect(button('', 'Create new recovery key')).toBeUndefined();
      expect(showKeyButton()).toBeDefined();
    });

    it('reports a refused key and shows none', async () => {
      configure(missing());
      backupServiceSpy.createRecoveryKey.and.resolveTo({ status: 'error', message: 'Refused' });
      fixture = create();
      const toasts = TestBed.inject(ToastService);
      spyOn(toasts, 'show');

      await confirmRegenerate();

      expect(toasts.show).toHaveBeenCalledWith('Refused', jasmine.objectContaining({ variant: 'error' }));
      expect(fixture.nativeElement.querySelector('shared-recovery-key-modal')).toBeNull();
    });
  });

  it('offers a create action, not a reveal, when no key exists yet', () => {
    configure(state({ availability: 'None', keyId: undefined, createdAt: undefined, exportedAt: undefined }));
    fixture = create();

    expect(showKeyButton()).toBeUndefined();
    expect(fixture.nativeElement.textContent as string).toContain('No recovery key exists yet');
  });
});
