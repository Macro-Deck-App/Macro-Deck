import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { BackupSummary, GetBackupStatusResponse, PendingRestoreSummary } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { BackupService } from '../../../../services/backup.service';
import { BackupListComponent } from './backup-list.component';

function backup(id: string, overrides: Partial<BackupSummary> = {}): BackupSummary {
  return {
    id,
    providerId: 'local',
    providerDisplayName: 'This computer',
    name: `Backup ${id}`,
    createdAt: '2026-08-17T03:00:00Z',
    trigger: 'Manual',
    macroDeckVersion: '3.0.0',
    sizeBytes: 1024 * 1024,
    formatVersion: 1,
    isRemote: false,
    decryptableLocally: true,
    imported: false,
    components: ['Profiles'],
    ...overrides,
  };
}

describe('BackupListComponent', () => {
  let fixture: ComponentFixture<BackupListComponent>;
  let backupServiceSpy: jasmine.SpyObj<BackupService>;
  let backupsSignal: WritableSignal<BackupSummary[]>;
  let operationSignal: WritableSignal<GetBackupStatusResponse | null>;
  let pendingRestoreSignal: WritableSignal<PendingRestoreSummary | null>;
  let toastSpy: jasmine.SpyObj<ToastService>;

  function configure(
    backups: BackupSummary[],
    operation: GetBackupStatusResponse | null = null,
    pendingRestore: PendingRestoreSummary | null = null,
  ): void {
    backupServiceSpy = jasmine.createSpyObj<BackupService>(
      'BackupService', ['loadBackups', 'downloadBackup', 'commitRestore', 'cancelRestore']);
    backupsSignal = signal(backups);
    operationSignal = signal(operation);
    pendingRestoreSignal = signal(pendingRestore);
    Object.defineProperty(backupServiceSpy, 'backups', { value: backupsSignal });
    Object.defineProperty(backupServiceSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(backupServiceSpy, 'loadError', { value: signal(null) });
    Object.defineProperty(backupServiceSpy, 'operation', { value: operationSignal });
    Object.defineProperty(backupServiceSpy, 'pendingRestore', { value: pendingRestoreSignal });
    Object.defineProperty(backupServiceSpy, 'canMutate', { value: signal(true) });

    toastSpy = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.returnValue(EMPTY);

    TestBed.configureTestingModule({
      imports: [BackupListComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: BackupService, useValue: backupServiceSpy },
        { provide: ToastService, useValue: toastSpy },
        { provide: ApiService, useValue: apiSpy },
      ],
    });
  }

  function create(): ComponentFixture<BackupListComponent> {
    const f = TestBed.createComponent(BackupListComponent);
    f.detectChanges();
    return f;
  }

  it('shows the empty state when there are no backups', () => {
    configure([]);
    fixture = create();

    expect(fixture.nativeElement.querySelector('.bl-empty')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.bl-row')).toBeNull();
  });

  it('renders one row per backup with its trigger, chip and size', () => {
    configure([backup('a', { trigger: 'Scheduled', sizeBytes: 2 * 1024 * 1024 }), backup('b', { isRemote: true })]);
    fixture = create();

    const rows = fixture.nativeElement.querySelectorAll('.bl-row');
    expect(rows.length).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Scheduled');
    expect(text).toContain('2.0 MB');
    expect(text).toContain('Remote');
  });

  it('shows a "Needs recovery key" chip for a backup this installation cannot decrypt', () => {
    configure([backup('a', { decryptableLocally: false })]);
    fixture = create();

    expect(fixture.nativeElement.textContent as string).toContain('Needs recovery key');
  });

  describe('a running operation (issue #36)', () => {
    it('does not render a running operation as a list row', () => {
      configure([], {
        operationId: 'op1',
        kind: 'Create',
        stage: 'CreatingSnapshot',
        trigger: 'Manual',
        updatedAt: '2026-08-17T03:00:00Z',
      });
      fixture = create();

      expect(fixture.nativeElement.querySelector('.bl-row')).toBeNull();
    });

    it('renders finished backups as rows alongside an unrelated later operation', () => {
      configure([backup('a')], {
        operationId: 'op2',
        kind: 'Restore',
        stage: 'Restoring',
        trigger: 'Manual',
        updatedAt: '2026-08-17T03:00:00Z',
      });
      fixture = create();

      expect(fixture.nativeElement.querySelectorAll('.bl-row').length).toBe(1);
    });
  });

  // The complaint that produced this: the banner offered a Restart button, it vanished on click, and a
  // toast explained the restart was never possible. The capability is static, so it is said up front.
  it('explains that Macro Deck must be restarted by hand instead of offering a dead button', () => {
    configure([], null, {
      restoreId: 'r1', backupId: 'b1', stagedAt: '2026-08-17T03:00:00Z', components: ['Profiles'],
      restartSupported: false, restartUnavailableReason: 'Restarting is only available in the installed desktop app',
    });
    fixture = create();

    const banner = fixture.nativeElement.querySelector('.bl-pending');
    expect(banner.textContent).toContain('Quit Macro Deck and start it again');
    expect(banner.textContent).toContain('installed desktop app');
    expect(banner.querySelector('.bl-pending-restart')).toBeNull();
    expect(banner.querySelector('.bl-pending-cancel')).toBeTruthy();
  });

  it('offers the restart button when the host can restart itself', () => {
    configure([], null, {
      restoreId: 'r1', backupId: 'b1', stagedAt: '2026-08-17T03:00:00Z', components: ['Profiles'],
      restartSupported: true,
    });
    fixture = create();

    const banner = fixture.nativeElement.querySelector('.bl-pending');
    expect(banner.querySelector('.bl-pending-restart')).toBeTruthy();
    expect(banner.textContent).not.toContain('Quit Macro Deck');
  });

  // Nothing was applied and the user has to act, so announcing it as a success is the wrong signal.
  it('reports an unsupported restart as a failure, not a success', async () => {
    configure([], null, {
      restoreId: 'r1', backupId: 'b1', stagedAt: '2026-08-17T03:00:00Z', components: ['Profiles'],
      restartSupported: false, restartUnavailableReason: 'Restarting is only available in the installed desktop app',
    });
    fixture = create();
    backupServiceSpy.commitRestore.and.resolveTo({
      status: 'ok',
      restartRequested: false,
      restartSupported: false,
      restartUnavailableReason: 'Restarting is only available in the installed desktop app',
    } as never);

    await fixture.componentInstance.restartNow();

    expect(toastSpy.show).toHaveBeenCalled();
    const [, options] = toastSpy.show.calls.mostRecent().args;
    expect(options?.variant).toBe('error');
    expect(options?.detail).toContain('installed desktop app');
  });

  describe('pagination', () => {
    function many(count: number): BackupSummary[] {
      return Array.from({ length: count }, (_, i) => backup(`b${i}`));
    }

    it('shows at most ten entries per page and pages through the rest', async () => {
      configure(many(23));
      fixture = create();

      expect(fixture.nativeElement.querySelectorAll('.bl-row').length).toBe(10);
      expect(fixture.nativeElement.querySelector('.bl-pager-position').textContent).toContain('1 of 3');

      fixture.componentInstance.goToPage(2);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelectorAll('.bl-row').length).toBe(3);
    });

    it('renders no pager when everything fits on one page', () => {
      configure(many(10));
      fixture = create();

      expect(fixture.nativeElement.querySelectorAll('.bl-row').length).toBe(10);
      expect(fixture.nativeElement.querySelector('.bl-pager')).toBeNull();
    });

    // Deleting the last entry of the final page must not leave the user staring at an empty list.
    it('falls back to a page that still exists when the list shrinks', async () => {
      configure(many(11));
      fixture = create();
      fixture.componentInstance.goToPage(1);
      fixture.detectChanges();
      await fixture.whenStable();

      backupsSignal.set(many(4));
      fixture.detectChanges();
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelectorAll('.bl-row').length).toBe(4);
    });
  });

  it('shows the staged-restore banner with Restart now / Cancel restore actions', () => {
    configure([], null, {
      restoreId: 'r1', backupId: 'b1', stagedAt: '2026-08-17T03:00:00Z', components: ['Profiles'],
      restartSupported: true,
    });
    fixture = create();

    const banner = fixture.nativeElement.querySelector('.bl-pending');
    expect(banner).toBeTruthy();
    expect(banner.textContent).toContain('Restart now');
    expect(banner.querySelector('.bl-pending-restart')).toBeTruthy();
    expect(banner.querySelector('.bl-pending-cancel')).toBeTruthy();
  });

  it('emits restoreRequested only for a backup that can be decrypted here', () => {
    configure([backup('a', { decryptableLocally: false }), backup('b', { decryptableLocally: true })]);
    fixture = create();
    const spy = jasmine.createSpy('restoreRequested');
    fixture.componentInstance.restoreRequested.subscribe(spy);

    fixture.componentInstance.restore(fixture.componentInstance.backups()[0]);
    expect(spy).not.toHaveBeenCalled();

    fixture.componentInstance.restore(fixture.componentInstance.backups()[1]);
    expect(spy).toHaveBeenCalledWith(fixture.componentInstance.backups()[1]);
  });

  it('emits deleteRequested without calling the API itself', () => {
    configure([backup('a')]);
    fixture = create();
    const spy = jasmine.createSpy('deleteRequested');
    fixture.componentInstance.deleteRequested.subscribe(spy);

    fixture.componentInstance.requestDelete(fixture.componentInstance.backups()[0]);

    expect(spy).toHaveBeenCalledWith(fixture.componentInstance.backups()[0]);
  });
});
