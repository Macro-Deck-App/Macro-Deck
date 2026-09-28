import { NO_ERRORS_SCHEMA, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { Announcement } from '@macro-deck/runtime';
import { NotificationCenterService } from '../../services/notification-center.service';
import { AnnouncementService } from '../../services/announcement.service';
import { MigrationOfferService } from '../../services/migration-offer.service';
import { OnboardingService } from '../../services/onboarding.service';
import { PostUpdateChangelogService } from '../../services/post-update-changelog.service';
import { SettingsModalService } from '../../services/settings-modal.service';
import { UpdateModalService } from '../../services/update-modal.service';
import { MIN_CONTENT_WIDTH_REM, NavigationService, SIDEBAR_EXPANDED_WIDTH } from '../../services';
import { ModalComponent } from '@shared';
import { ShellComponent } from './shell.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

class FakeResizeObserver implements ResizeObserver {
  static instances: FakeResizeObserver[] = [];
  readonly observedTargets: Element[] = [];

  constructor(private readonly callback: ResizeObserverCallback) {
    FakeResizeObserver.instances.push(this);
  }

  observe(target: Element): void {
    this.observedTargets.push(target);
  }

  unobserve(): void {}

  disconnect(): void {}

  trigger(width: number): void {
    const entry = { contentRect: { width } } as ResizeObserverEntry;
    this.callback([entry], this);
  }
}

const rootFontSizePx = (): number => parseFloat(getComputedStyle(document.documentElement).fontSize);
const narrowWidth = (): number => SIDEBAR_EXPANDED_WIDTH + MIN_CONTENT_WIDTH_REM * rootFontSizePx() - 50;
const wideWidth = (): number => SIDEBAR_EXPANDED_WIDTH + MIN_CONTENT_WIDTH_REM * rootFontSizePx() + 300;

const notificationCenterStub = {
  hasNotifications: signal(false),
  hasActiveProgress: signal(false),
  badgeText: signal('0'),
};

const announcementStub = {
  pending: signal<Announcement | null>(null),
};

describe('ShellComponent', () => {
  let fixture: ComponentFixture<ShellComponent>;
  let navigationService: NavigationService;
  let matchMediaSpy: jasmine.Spy;
  let originalResizeObserver: typeof ResizeObserver;

  beforeEach(async () => {
    matchMediaSpy = spyOn(window, 'matchMedia').and.callThrough();

    originalResizeObserver = window.ResizeObserver;
    FakeResizeObserver.instances = [];
    window.ResizeObserver = FakeResizeObserver as unknown as typeof ResizeObserver;

    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: NotificationCenterService, useValue: notificationCenterStub },
        { provide: AnnouncementService, useValue: announcementStub },
      ],
    })
      .overrideComponent(ShellComponent, { set: { imports: [], schemas: [NO_ERRORS_SCHEMA] } })
      .compileComponents();

    fixture = TestBed.createComponent(ShellComponent);
    navigationService = TestBed.inject(NavigationService);
    fixture.detectChanges();
  });

  afterEach(() => {
    window.ResizeObserver = originalResizeObserver;
  });

  it('never queries a fixed viewport media query for the sidebar breakpoint', () => {
    expect(matchMediaSpy).not.toHaveBeenCalledWith('(max-width: 768px)');
  });

  it('observes .shell-content, not .shell-main, whose width would change once the sidebar collapses', () => {
    const observer = FakeResizeObserver.instances.at(-1);
    expect(observer).toBeTruthy();

    const shellContentEl = fixture.debugElement.query(By.css('.shell-content')).nativeElement;
    const shellMainEl = fixture.debugElement.query(By.css('.shell-main')).nativeElement;

    expect(observer!.observedTargets).toEqual([shellContentEl]);
    expect(observer!.observedTargets).not.toContain(shellMainEl);
  });

  it('collapses the sidebar when the observed container reports a width too narrow for the page', () => {
    const observer = FakeResizeObserver.instances.at(-1)!;
    const sidebarEl = fixture.debugElement.query(By.css('app-sidebar')).nativeElement as { isCollapsed: boolean };
    expect(sidebarEl.isCollapsed).toBeFalse();

    observer.trigger(narrowWidth());
    fixture.detectChanges();

    expect(sidebarEl.isCollapsed).toBeTrue();
    expect(navigationService.isSidebarCollapsed()).toBeTrue();
  });

  it('expands the sidebar again once the observed container reports enough width', () => {
    const observer = FakeResizeObserver.instances.at(-1)!;
    const sidebarEl = fixture.debugElement.query(By.css('app-sidebar')).nativeElement as { isCollapsed: boolean };

    observer.trigger(narrowWidth());
    fixture.detectChanges();
    expect(sidebarEl.isCollapsed).toBeTrue();

    observer.trigger(wideWidth());
    fixture.detectChanges();
    expect(sidebarEl.isCollapsed).toBeFalse();
  });

  it('drives the statusbar collapsed input from the same navigation service state', () => {
    const observer = FakeResizeObserver.instances.at(-1)!;
    const statusbarEl = fixture.debugElement.query(By.css('app-statusbar')).nativeElement as {
      isSidebarCollapsed: boolean;
    };

    observer.trigger(narrowWidth());
    fixture.detectChanges();

    expect(statusbarEl.isSidebarCollapsed).toBeTrue();
  });

  it('does not let an auto-collapse discard a manual expand: toggling while constrained stays expanded', () => {
    const observer = FakeResizeObserver.instances.at(-1)!;
    const sidebarEl = fixture.debugElement.query(By.css('app-sidebar')).nativeElement as { isCollapsed: boolean };

    observer.trigger(narrowWidth());
    fixture.detectChanges();
    expect(sidebarEl.isCollapsed).toBeTrue();

    navigationService.toggleSidebar();
    fixture.detectChanges();
    expect(sidebarEl.isCollapsed).toBeFalse();

    observer.trigger(narrowWidth());
    fixture.detectChanges();
    expect(sidebarEl.isCollapsed).toBeFalse();
  });

  it('opens the notification panel from the sidebar, and marks that item active while it is open', () => {
    expect(navigationService.isNotificationPanelOpen()).toBeFalse();

    fixture.componentInstance.onNavAction('open-notifications');

    expect(navigationService.isNotificationPanelOpen()).toBeTrue();
  });

  it('toggles the notification panel shut on a second press of the same item', () => {
    fixture.componentInstance.onNavAction('open-notifications');
    fixture.componentInstance.onNavAction('open-notifications');

    expect(navigationService.isNotificationPanelOpen()).toBeFalse();
  });

  // The bar has to span the whole shell and stay put when the sidebar collapses, which it can only
  // do from outside .shell-content - the row the sidebar shares with the page. Nesting it inside
  // that row is the regression this guards; mere presence cannot catch it, because the element is
  // in no @if and so never disappears either way.
  it('puts the footer bar outside the row the sidebar shares, so a collapse cannot reach it', () => {
    const footer = fixture.debugElement.query(By.css('app-footer-bar'));
    expect(footer).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.shell-content app-footer-bar'))).toBeNull();
    expect((footer.nativeElement as HTMLElement).parentElement).toBe(
      fixture.debugElement.query(By.css('.shell')).nativeElement);

    const observer = FakeResizeObserver.instances.at(-1)!;
    observer.trigger(narrowWidth());
    fixture.detectChanges();
    expect(navigationService.isSidebarCollapsed()).toBeTrue();
    expect(fixture.debugElement.query(By.css('.shell > app-footer-bar'))).toBeTruthy();
  });

  it('keeps the side panels inside the positioned content row, so their scrim never dims the statusbar or footer', () => {
    const content = fixture.debugElement.query(By.css('.shell-content')).nativeElement as HTMLElement;

    expect(getComputedStyle(content).position).toBe('relative');
    expect(content.querySelector('app-notification-panel')).not.toBeNull();
    expect(content.querySelector('app-connection-panel')).not.toBeNull();
    expect(content.querySelector('app-statusbar')).toBeNull();
    expect(content.querySelector('app-footer-bar')).toBeNull();
  });
});

describe('ShellComponent announcement', () => {
  const announcement: Announcement = {
    number: 4,
    title: 'Macro Deck 3 is here',
    content: 'Body',
    publishedAt: '2026-09-28T15:58:49Z',
    updatedAt: '2026-09-28T15:58:49Z',
  };

  let fixture: ComponentFixture<ShellComponent>;
  let resolveChangelog: (changelog: ShellPostUpdateChangelog | null) => void;
  let originalResizeObserver: typeof ResizeObserver;

  beforeEach(async () => {
    originalResizeObserver = window.ResizeObserver;
    window.ResizeObserver = FakeResizeObserver as unknown as typeof ResizeObserver;
    (window as { macroDeckShell?: unknown }).macroDeckShell = {
      getPostUpdateChangelog: () => new Promise(resolve => (resolveChangelog = resolve)),
      dismissPostUpdateChangelog: () => Promise.resolve(),
    };

    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: NotificationCenterService, useValue: notificationCenterStub },
        { provide: AnnouncementService, useValue: announcementStub },
      ],
    })
      .overrideComponent(ShellComponent, { set: { imports: [], schemas: [NO_ERRORS_SCHEMA] } })
      .compileComponents();

    TestBed.inject(OnboardingService).state.set('done');
    TestBed.inject(MigrationOfferService).pending.set(false);
    fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    window.ResizeObserver = originalResizeObserver;
    announcementStub.pending.set(null);
    delete (window as { macroDeckShell?: unknown }).macroDeckShell;
  });

  const shown = (selector = 'app-announcement-modal'): boolean => {
    fixture.detectChanges();
    return !!fixture.nativeElement.querySelector(selector);
  };

  async function settle(changelog: ShellPostUpdateChangelog | null): Promise<void> {
    resolveChangelog(changelog);
    await new Promise(resolve => setTimeout(resolve));
  }

  it('does not appear before the What\'s New check has finished', async () => {
    announcementStub.pending.set(announcement);

    expect(shown()).toBeFalse();

    await settle(null);
    expect(shown()).toBeTrue();
  });

  it('waits for the What\'s New modal of an installed update and follows it', async () => {
    announcementStub.pending.set(announcement);
    await settle({ version: '3.2.0', notes: 'notes', notesUrl: null, publishedAt: null });

    expect(shown()).toBeFalse();
    expect(shown('app-whats-new-modal')).toBeTrue();

    TestBed.inject(PostUpdateChangelogService).dismiss();

    expect(shown()).toBeTrue();
    expect(shown('app-whats-new-modal')).toBeFalse();
  });

  it('never covers an update or settings modal the user opened, and appears once it is closed', async () => {
    await settle(null);
    const updateModal = TestBed.inject(UpdateModalService);
    const settingsModal = TestBed.inject(SettingsModalService);
    updateModal.open();
    announcementStub.pending.set(announcement);

    expect(shown()).toBeFalse();
    expect(shown('app-update-modal')).toBeTrue();

    updateModal.close();
    settingsModal.open();
    expect(shown()).toBeFalse();

    settingsModal.close();
    expect(shown()).toBeTrue();
  });

  it('waits while a page has a dialog open, and stays once shown when another dialog opens over it', async () => {
    await settle(null);
    const pageDialog = TestBed.createComponent(ModalComponent);
    pageDialog.detectChanges();
    announcementStub.pending.set(announcement);

    expect(shown()).toBeFalse();

    pageDialog.destroy();
    expect(shown()).toBeTrue();

    const laterDialog = TestBed.createComponent(ModalComponent);
    laterDialog.detectChanges();
    expect(shown()).toBeTrue();
    laterDialog.destroy();
  });

  it('waits for onboarding and the migration offer', async () => {
    await settle(null);
    const onboarding = TestBed.inject(OnboardingService);
    const migrationOffer = TestBed.inject(MigrationOfferService);
    onboarding.state.set('unknown');
    migrationOffer.pending.set(true);
    announcementStub.pending.set(announcement);

    expect(shown()).toBeFalse();
    onboarding.state.set('done');
    expect(shown()).toBeFalse();
    migrationOffer.pending.set(false);
    expect(shown()).toBeTrue();
  });
});
