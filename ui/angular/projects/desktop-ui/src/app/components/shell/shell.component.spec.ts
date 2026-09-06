import { NO_ERRORS_SCHEMA, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { NotificationCenterService } from '../../services/notification-center.service';
import { MIN_CONTENT_WIDTH_REM, NavigationService, SIDEBAR_EXPANDED_WIDTH } from '../../services';
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
});
