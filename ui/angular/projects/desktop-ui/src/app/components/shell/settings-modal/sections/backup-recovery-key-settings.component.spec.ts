import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetBackupRecoveryKeyStateResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
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
      'BackupService', ['createRecoveryKey', 'revealRecoveryKey', 'acknowledgeRecoveryKey']);
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

  it('offers a create action, not a reveal, when no key exists yet', () => {
    configure(state({ availability: 'None', keyId: undefined, createdAt: undefined, exportedAt: undefined }));
    fixture = create();

    expect(showKeyButton()).toBeUndefined();
    expect(fixture.nativeElement.textContent as string).toContain('No recovery key exists yet');
  });
});
