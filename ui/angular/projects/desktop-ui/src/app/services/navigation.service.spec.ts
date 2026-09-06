import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { NotificationCenterService } from './notification-center.service';

import {
  isWorkspaceConstrained,
  MIN_CONTENT_WIDTH_REM,
  NavigationService,
  SIDEBAR_COLLAPSED_KEY,
  SIDEBAR_EXPANDED_WIDTH,
} from './navigation.service';
import { provideLocalizationTesting } from '../../testing/localization-test-support';

function isolateSidebarPreference(): void {
  let storedValue: string | null = null;

  beforeEach(() => {
    storedValue = localStorage.getItem(SIDEBAR_COLLAPSED_KEY);
    localStorage.removeItem(SIDEBAR_COLLAPSED_KEY);
  });

  afterEach(() => {
    if (storedValue === null) {
      localStorage.removeItem(SIDEBAR_COLLAPSED_KEY);
    } else {
      localStorage.setItem(SIDEBAR_COLLAPSED_KEY, storedValue);
    }
  });
}

describe('isWorkspaceConstrained', () => {
  const threshold = (rootFontSizePx: number): number => SIDEBAR_EXPANDED_WIDTH + MIN_CONTENT_WIDTH_REM * rootFontSizePx;

  it('is not constrained just above the threshold at the default 16px root', () => {
    const availableWidth = threshold(16) + 1;
    expect(isWorkspaceConstrained(availableWidth, 16)).toBeFalse();
  });

  it('is constrained just below the threshold at the default 16px root', () => {
    const availableWidth = threshold(16) - 1;
    expect(isWorkspaceConstrained(availableWidth, 16)).toBeTrue();
  });

  it('is not constrained exactly at the threshold (a strict less-than comparison)', () => {
    expect(isWorkspaceConstrained(threshold(16), 16)).toBeFalse();
  });

  it('scales with the root font size: the same width unconstrained at 16px is constrained at 20px', () => {
    const width = threshold(16) + 1;
    expect(isWorkspaceConstrained(width, 16)).toBeFalse();
    expect(isWorkspaceConstrained(width, 20)).toBeTrue();
  });

  it('matches the issue repro: a 900px viewport auto-collapses at the default 16px root', () => {
    const shellContentWidth = 900;
    expect(isWorkspaceConstrained(shellContentWidth, 16)).toBeTrue();
  });
});

const notificationCenterStub = {
  hasNotifications: signal(false),
  hasActiveProgress: signal(false),
  badgeText: signal('0'),
};

describe('NavigationService', () => {
  let service: NavigationService;

  isolateSidebarPreference();

  beforeEach(() => {
    notificationCenterStub.hasNotifications.set(false);
    notificationCenterStub.hasActiveProgress.set(false);
    notificationCenterStub.badgeText.set('0');

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: NotificationCenterService, useValue: notificationCenterStub },
      ],
    });
    service = TestBed.inject(NavigationService);
  });

  it('is expanded by default when wide', () => {
    expect(service.isSidebarCollapsed()).toBeFalse();
  });

  it('collapses automatically when narrowing past the threshold', () => {
    service.setSpaceConstrained(true);
    expect(service.isSidebarCollapsed()).toBeTrue();
  });

  it('expands again when widening back out', () => {
    service.setSpaceConstrained(true);
    service.setSpaceConstrained(false);
    expect(service.isSidebarCollapsed()).toBeFalse();
  });

  it('keeps a manual collapse across a narrow-then-wide resize cycle', () => {
    service.toggleSidebar();
    expect(service.isSidebarCollapsed()).toBeTrue();

    service.setSpaceConstrained(true);
    expect(service.isSidebarCollapsed()).toBeTrue();

    service.setSpaceConstrained(false);
    expect(service.isSidebarCollapsed()).toBeTrue();
  });

  it('lets a manual toggle expand the sidebar while constrained, and it stays expanded', () => {
    service.setSpaceConstrained(true);
    expect(service.isSidebarCollapsed()).toBeTrue();

    service.toggleSidebar();
    expect(service.isSidebarCollapsed()).toBeFalse();

    service.setSpaceConstrained(true);
    service.setSpaceConstrained(false);
    expect(service.isSidebarCollapsed()).toBeFalse();
  });

  it('clears the constraint override on a genuine constraint transition', () => {
    service.setSpaceConstrained(true);
    service.toggleSidebar();
    expect(service.isSidebarCollapsed()).toBeFalse();

    service.setSpaceConstrained(false);
    service.setSpaceConstrained(true);
    expect(service.isSidebarCollapsed()).toBeTrue();
  });

  it('ignores a redundant setSpaceConstrained call with the same value', () => {
    service.setSpaceConstrained(true);
    service.toggleSidebar();
    expect(service.isSidebarCollapsed()).toBeFalse();

    service.setSpaceConstrained(true);
    expect(service.isSidebarCollapsed()).toBeFalse();
  });

  it('lets a manual collapse while constrained survive the constraint clearing', () => {
    service.setSpaceConstrained(true);
    service.toggleSidebar();
    service.toggleSidebar();
    expect(service.isSidebarCollapsed()).toBeTrue();

    service.setSpaceConstrained(false);
    expect(service.isSidebarCollapsed()).toBeTrue();
  });

  it('has no standalone Logs entry - the log viewer is a Developer tab', () => {
    expect(service.bottomNavItems().map(i => i.id)).toEqual(['notifications', 'developer', 'settings']);
  });

  it('shows no notification indicator while the center is empty', () => {
    const item = service.bottomNavItems().find(i => i.id === 'notifications');
    expect(item?.indicator).toBeNull();
    expect(item?.activity).toBeFalse();
  });

  it('carries the live count and the running-work ring on the notifications entry', () => {
    notificationCenterStub.hasNotifications.set(true);
    notificationCenterStub.badgeText.set('3');
    notificationCenterStub.hasActiveProgress.set(true);

    const item = service.bottomNavItems().find(i => i.id === 'notifications');
    expect(item?.indicator).toBe('3');
    expect(item?.activity).toBeTrue();
  });

  it('closes the network panel when the notification panel opens', () => {
    service.toggleConnectionPanel();
    service.toggleNotificationPanel();

    expect(service.isNotificationPanelOpen()).toBeTrue();
    expect(service.isConnectionPanelOpen()).toBeFalse();
  });

  it('closes the notification panel when the network panel opens', () => {
    service.toggleNotificationPanel();
    service.toggleConnectionPanel();

    expect(service.isConnectionPanelOpen()).toBeTrue();
    expect(service.isNotificationPanelOpen()).toBeFalse();
  });

  it('closing a panel leaves the other one alone', () => {
    service.toggleNotificationPanel();
    service.toggleNotificationPanel();

    expect(service.isNotificationPanelOpen()).toBeFalse();
    expect(service.isConnectionPanelOpen()).toBeFalse();
  });
});

describe('NavigationService sidebar collapse persistence', () => {
  isolateSidebarPreference();

  function freshService(): NavigationService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: NotificationCenterService, useValue: notificationCenterStub },
      ],
    });
    return TestBed.inject(NavigationService);
  }

  it('a manual collapse survives a restart', () => {
    let service = freshService();
    expect(service.isSidebarCollapsed()).toBeFalse();

    service.toggleSidebar();
    expect(service.isSidebarCollapsed()).toBeTrue();

    service = freshService();
    expect(service.isSidebarCollapsed()).toBeTrue();
  });

  it('a manual expand survives a restart (guards the missing "0" write)', () => {
    let service = freshService();
    service.toggleSidebar();
    expect(service.isSidebarCollapsed()).toBeTrue();

    service = freshService();
    expect(service.isSidebarCollapsed()).toBeTrue();

    service.toggleSidebar();
    expect(service.isSidebarCollapsed()).toBeFalse();

    service = freshService();
    expect(service.isSidebarCollapsed()).toBeFalse();
  });

  it('responsive auto-collapse alone is never persisted', () => {
    let service = freshService();
    service.setSpaceConstrained(true);
    expect(service.isSidebarCollapsed()).toBeTrue();

    service = freshService();
    expect(service.isSidebarCollapsed()).toBeFalse();
  });

  it('a manual choice is not overwritten by a constraint cycle', () => {
    let service = freshService();
    service.toggleSidebar();
    service.setSpaceConstrained(true);
    service.setSpaceConstrained(false);
    expect(service.isSidebarCollapsed()).toBeTrue();

    service = freshService();
    expect(service.isSidebarCollapsed()).toBeTrue();
  });

  it('an unreadable persisted value falls back to expanded and does not throw', () => {
    localStorage.setItem(SIDEBAR_COLLAPSED_KEY, 'not-a-valid-value');

    let service: NavigationService | undefined;
    expect(() => (service = freshService())).not.toThrow();
    expect(service!.isSidebarCollapsed()).toBeFalse();
  });

  it('auto-collapse still works after a persisted expand', () => {
    localStorage.setItem(SIDEBAR_COLLAPSED_KEY, '0');

    const service = freshService();
    service.setSpaceConstrained(true);
    expect(service.isSidebarCollapsed()).toBeTrue();
  });
});
