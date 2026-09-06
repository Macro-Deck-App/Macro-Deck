import { computed, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { UserNotification, UserNotificationAction } from '@macro-deck/runtime';
import { IconPackService } from '../../../services/icon-pack.service';
import { NotificationCenterService } from '../../../services/notification-center.service';
import { RestartNoticeService, SettingsModalService, UpdateModalService, UpdateService } from '../../../services';
import { NotificationPanelComponent } from './notification-panel.component';
import { LIBRARY_CONTENT_TYPES } from '../../../domain/library-content-type';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('NotificationPanelComponent', () => {
  let fixture: ComponentFixture<NotificationPanelComponent>;
  let notifications: ReturnType<typeof signal<UserNotification[]>>;
  let dismissAllSpy: jasmine.Spy;
  let dismissSpy: jasmine.Spy;
  let navigateSpy: jasmine.Spy;
  let settingsOpenSpy: jasmine.Spy;
  let cancelBatchSpy: jasmine.Spy;
  let restartSpy: jasmine.Spy;
  let refreshSpy: jasmine.Spy;
  let restartRequired: ReturnType<typeof signal<boolean>>;
  let updateModalOpenSpy: jasmine.Spy;
  let updateInstallSpy: jasmine.Spy;
  let updateCancelSpy: jasmine.Spy;

  function fireAction(notification: UserNotification, action: UserNotificationAction): void {
    fixture.componentInstance.onAction({ notification, action });
  }

  function notification(overrides: Partial<UserNotification> = {}): UserNotification {
    return {
      id: 'n1',
      sequence: 1,
      timestamp: '2026-07-28T10:00:00+00:00',
      severity: 'Info',
      kind: 'General',
      title: 'Something happened',
      ...overrides,
    };
  }

  async function open(): Promise<void> {
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    notifications = signal<UserNotification[]>([]);
    restartRequired = signal(true);
    dismissAllSpy = jasmine.createSpy('dismissAll');
    dismissSpy = jasmine.createSpy('dismiss');
    navigateSpy = jasmine.createSpy('navigate').and.resolveTo(true);
    settingsOpenSpy = jasmine.createSpy('open');
    cancelBatchSpy = jasmine.createSpy('cancelBatch').and.resolveTo(true);
    restartSpy = jasmine.createSpy('restartNow').and.resolveTo(undefined);
    refreshSpy = jasmine.createSpy('refresh').and.resolveTo(undefined);
    updateModalOpenSpy = jasmine.createSpy('open');
    updateInstallSpy = jasmine.createSpy('install').and.returnValue(new Promise<void>(() => {}));
    updateCancelSpy = jasmine.createSpy('cancelDownload').and.resolveTo(undefined);

    await TestBed.configureTestingModule({
      imports: [NotificationPanelComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: NotificationCenterService,
          useValue: {
            notifications,
            count: computed(() => notifications().length),
            hasNotifications: computed(() => notifications().length > 0),
            hasActiveProgress: computed(() => notifications().some(n => !!n.progress)),
            hasDismissable: computed(() => notifications().some(n => !n.progress)),
            badgeText: computed(() => `${notifications().length}`),
            dismissAll: dismissAllSpy,
            dismiss: dismissSpy,
          },
        },
        { provide: Router, useValue: { navigate: navigateSpy } },
        { provide: SettingsModalService, useValue: { open: settingsOpenSpy } },
        { provide: UpdateModalService, useValue: { open: updateModalOpenSpy } },
        { provide: UpdateService, useValue: { install: updateInstallSpy, cancelDownload: updateCancelSpy } },
        { provide: IconPackService, useValue: { cancelBatch: cancelBatchSpy } },
        {
          provide: RestartNoticeService,
          useValue: { required: restartRequired, restartNow: restartSpy, refresh: refreshSpy },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationPanelComponent);
    fixture.detectChanges();
  });

  it('stays mounted while closed so the shell can animate it shut', () => {
    const panel = fixture.nativeElement.querySelector('.np-panel') as HTMLElement;
    expect(panel).not.toBeNull();
    expect(panel.classList).not.toContain('np-panel-open');
    expect(panel.hasAttribute('inert')).toBeTrue();
  });

  it('shows the empty state when there is nothing to report', async () => {
    await open();

    expect(fixture.nativeElement.querySelector('shared-empty-state')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-notification-item')).toBeNull();
  });

  it('keeps row order stable across successive progress-only snapshots', async () => {
    notifications.set([
      notification({ id: 'a', title: 'First', progress: { processed: 1, total: 10 } }),
      notification({ id: 'b', title: 'Second' }),
    ]);
    await open();

    notifications.set([
      notification({ id: 'a', title: 'First', progress: { processed: 5, total: 10 } }),
      notification({ id: 'b', title: 'Second' }),
    ]);
    fixture.detectChanges();

    const titles = Array.from(fixture.nativeElement.querySelectorAll('.ni-title')).map(
      element => (element as HTMLElement).textContent?.trim(),
    );
    expect(titles).toEqual(['First', 'Second']);
  });

  it('lists one row per notification', async () => {
    notifications.set([notification({ id: 'a' }), notification({ id: 'b' })]);
    await open();

    expect(fixture.nativeElement.querySelectorAll('app-notification-item').length).toBe(2);
  });

  it('offers no clear-all while the only entry is still running', async () => {
    notifications.set([notification({ progress: { processed: 1, total: 10 } })]);
    await open();

    expect(fixture.nativeElement.querySelector('.np-header-btn')).toBeNull();
  });

  it('clears everything on clear-all', async () => {
    notifications.set([notification()]);
    await open();

    (fixture.nativeElement.querySelector('.np-header-btn') as HTMLButtonElement).click();

    expect(dismissAllSpy).toHaveBeenCalled();
  });

  it('closes on the close button', async () => {
    await open();
    const closedSpy = jasmine.createSpy('closed');
    fixture.componentInstance.closed.subscribe(closedSpy);

    (fixture.nativeElement.querySelector('.np-close') as HTMLButtonElement).click();

    expect(closedSpy).toHaveBeenCalled();
  });

  it('sends OpenLogs to the Developer page Logs tab, not a standalone route', () => {
    fireAction(notification(), { kind: 'OpenLogs' });

    expect(navigateSpy).toHaveBeenCalledWith(['/developer'], { queryParams: { tab: 'logs' } });
  });

  it('navigates to the integration an integration notification came from', () => {
    fireAction(notification(), { kind: 'OpenIntegration', target: 'obs' });

    expect(navigateSpy).toHaveBeenCalledWith(['/integrations', 'obs']);
  });

  it('opens the About settings section for an update notification', () => {
    fireAction(notification(), { kind: 'OpenUpdateSettings' });

    expect(settingsOpenSpy).toHaveBeenCalledWith('about');
  });

  it('restarts through the shared notice', async () => {
    fireAction(notification(), { kind: 'RestartApplication' });
    await fixture.whenStable();

    expect(restartSpy).toHaveBeenCalled();
  });

  it('refreshes the restart notice first when it has not seen the host state', async () => {
    restartRequired.set(false);

    fireAction(notification(), { kind: 'RestartApplication' });
    await fixture.whenStable();

    expect(refreshSpy).toHaveBeenCalled();
    expect(restartSpy).toHaveBeenCalled();
  });

  it('cancels the import batch an icon-import notification points at', () => {
    fixture.componentInstance.onCancel(notification({ kind: 'IconImport', cancelKey: 'batch-1' }));

    expect(cancelBatchSpy).toHaveBeenCalledWith('batch-1');
  });

  it('ignores a cancel for a kind that has no batch behind it', () => {
    fixture.componentInstance.onCancel(notification({ kind: 'General', cancelKey: 'batch-1' }));

    expect(cancelBatchSpy).not.toHaveBeenCalled();
  });

  it('cancels the download behind an update notification', () => {
    fixture.componentInstance.onCancel(notification({ kind: 'Update', cancelKey: 'update-download' }));

    expect(updateCancelSpy).toHaveBeenCalled();
  });

  it('ignores a cancel on an update notification with no cancelKey', () => {
    fixture.componentInstance.onCancel(notification({ kind: 'Update', cancelKey: undefined }));

    expect(updateCancelSpy).not.toHaveBeenCalled();
  });

  // Asserted against the registry rather than a literal path: the requirement is that the action
  // lands on icon pack management, wherever the library keeps it (issue #845 moved it).
  it('closes itself once an action has taken the user somewhere', () => {
    const closedSpy = jasmine.createSpy('closed');
    fixture.componentInstance.closed.subscribe(closedSpy);
    const iconPacks = TestBed.inject(LIBRARY_CONTENT_TYPES).find(type => type.id === 'icon-packs');

    fireAction(notification(), { kind: 'OpenIconPacks' });

    expect(navigateSpy).toHaveBeenCalledWith([iconPacks!.route]);
    expect(closedSpy).toHaveBeenCalled();
  });

  describe('update notification actions (issue #249)', () => {
    it('opens the update modal for OpenUpdateDetails, not the settings modal', () => {
      const closedSpy = jasmine.createSpy('closed');
      fixture.componentInstance.closed.subscribe(closedSpy);

      fireAction(notification({ kind: 'Update' }), { kind: 'OpenUpdateDetails' });

      expect(updateModalOpenSpy).toHaveBeenCalled();
      expect(settingsOpenSpy).not.toHaveBeenCalled();
      expect(closedSpy).toHaveBeenCalled();
    });

    it('installs through the update service for InstallUpdate', () => {
      fireAction(notification({ kind: 'Update' }), { kind: 'InstallUpdate' });

      expect(updateInstallSpy).toHaveBeenCalled();
    });

    it('dismisses one notification for DismissNotification without closing the panel', () => {
      notifications.set([notification({ id: 'update-1', kind: 'Update' })]);
      const closedSpy = jasmine.createSpy('closed');
      fixture.componentInstance.closed.subscribe(closedSpy);

      fireAction(notification({ id: 'update-1', kind: 'Update' }), { kind: 'DismissNotification' });

      expect(dismissSpy).toHaveBeenCalledWith('update-1');
      expect(closedSpy).not.toHaveBeenCalled();
    });
  });
});
