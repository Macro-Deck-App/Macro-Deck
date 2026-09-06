import { NO_ERRORS_SCHEMA, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslatePipe } from '@shared';
import { NotificationCenterService } from '../../../services/notification-center.service';
import { NavigationService } from '../../../services';
import { StatusbarComponent } from './statusbar.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

const notificationCenterStub = {
  hasNotifications: signal(false),
  hasActiveProgress: signal(false),
  badgeText: signal('0'),
};

describe('StatusbarComponent', () => {
  let fixture: ComponentFixture<StatusbarComponent>;
  let component: StatusbarComponent;
  let navigationService: NavigationService;

  beforeEach(async () => {
    // The layout tests below measure rem lengths against the root font size, and
    // other specs set it on the shared document. Clearing it here pins these
    // measurements to the browser default (issue #306).
    document.documentElement.style.removeProperty('font-size');

    await TestBed.configureTestingModule({
      imports: [StatusbarComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: NotificationCenterService, useValue: notificationCenterStub },
      ],
    })
      .overrideComponent(StatusbarComponent, { set: { imports: [TranslatePipe], schemas: [NO_ERRORS_SCHEMA] } })
      .compileComponents();

    fixture = TestBed.createComponent(StatusbarComponent);
    component = fixture.componentInstance;
    navigationService = TestBed.inject(NavigationService);
    fixture.detectChanges();
  });

  it('renders the sidebar toggle as the first control on the left', () => {
    const left = fixture.nativeElement.querySelector('.statusbar-left') as HTMLElement;
    const toggle = left.querySelector('.toggle-btn');
    expect(toggle).toBeTruthy();
    expect(toggle?.querySelector('.icon-sidebar')).toBeTruthy();
  });

  it('shows no product name next to the sidebar toggle', () => {
    const left = fixture.nativeElement.querySelector('.statusbar-left') as HTMLElement;
    expect(left.querySelector('.logo')).toBeNull();
  });

  it('shows a Development Build badge for a locally compiled Development UI', () => {
    navigationService.setAppVersion('3.0.0', false, true, 'abc1234');
    const shellFixture = TestBed.createComponent(StatusbarComponent);
    shellFixture.detectChanges();

    const badge = shellFixture.nativeElement.querySelector('.build-channel-badge') as HTMLElement;
    expect(badge).toBeTruthy();
    expect(badge.textContent?.trim()).toBe('Development Build');
  });

  it('does not render a Development Build badge for a Production UI', () => {
    navigationService.setAppVersion('3.0.0', false, false);
    const shellFixture = TestBed.createComponent(StatusbarComponent);
    shellFixture.detectChanges();

    expect(shellFixture.nativeElement.querySelector('.build-channel-badge')).toBeNull();
  });

  it('emits toggleSidebar when the toggle is clicked', () => {
    const emitted = jasmine.createSpy('toggleSidebar');
    component.toggleSidebar.subscribe(emitted);

    (fixture.nativeElement.querySelector('.toggle-btn') as HTMLElement).click();

    expect(emitted).toHaveBeenCalledTimes(1);
  });

  // The bar is the window's title bar inside the macOS shell, and the
  // bootstrapper centers the native traffic lights against exactly this height
  // (STATUS_BAR_HEIGHT in ui/bootstrapper/src/window.rs, issue #158). The
  // 3.25rem resolves against the 16px root, so the root size is asserted too:
  // either one changing here without changing it there puts the buttons off
  // centre again. The reset in beforeEach is what makes the fixed pixels
  // meaningful - it pins the root the shell centres against.
  it('is 52px tall, the height the macOS shell centers the traffic lights against', () => {
    const statusbar = fixture.nativeElement.querySelector('.statusbar') as HTMLElement;

    expect(getComputedStyle(document.documentElement).fontSize).toBe('16px');
    expect(statusbar.offsetHeight).toBe(52);
  });

  it('reserves a spacer that clears the traffic lights at their shell inset', () => {
    const shellFixture = TestBed.createComponent(StatusbarComponent);
    (shellFixture.componentInstance as unknown as { isShellMacOS: boolean }).isShellMacOS = true;
    shellFixture.detectChanges();

    const spacer = shellFixture.nativeElement.querySelector('.traffic-light-spacer') as HTMLElement;
    expect(spacer.offsetWidth).toBe(82);
    expect(spacer.offsetWidth).toBeGreaterThan(79);
  });

  it('keeps the Development Build badge after the macOS traffic-light clearance and toggle', () => {
    navigationService.setAppVersion('3.0.0', false, true, 'abc1234');
    const shellFixture = TestBed.createComponent(StatusbarComponent);
    (shellFixture.componentInstance as unknown as { isShellMacOS: boolean }).isShellMacOS = true;
    shellFixture.detectChanges();

    const left = shellFixture.nativeElement.querySelector('.statusbar-left') as HTMLElement;
    expect(Array.from(left.children, child => child.className)).toEqual([
      'traffic-light-spacer',
      'toggle-btn',
      'build-channel-badge',
    ]);
  });

  it('renders the network control as a bare toggle button', () => {
    const right = fixture.nativeElement.querySelector('.statusbar-right') as HTMLElement;
    const network = right.querySelector('.toggle-btn');

    expect(network?.querySelector('.icon-wifi')).toBeTruthy();
    expect(right.querySelector('shared-dropdown-menu')).toBeNull();
  });

  it('emits toggleConnectionPanel when the network toggle is clicked', () => {
    const emitted = jasmine.createSpy('toggleConnectionPanel');
    component.toggleConnectionPanel.subscribe(emitted);

    const right = fixture.nativeElement.querySelector('.statusbar-right') as HTMLElement;
    (right.querySelector('.toggle-btn') as HTMLElement).click();

    expect(emitted).toHaveBeenCalledTimes(1);
  });

  it('marks the network toggle as pressed while the panel is open', () => {
    fixture.componentRef.setInput('isConnectionPanelOpen', true);
    fixture.detectChanges();

    const network = fixture.nativeElement.querySelector('.statusbar-right .toggle-btn') as HTMLElement;
    expect(network.classList).toContain('active');
    expect(network.getAttribute('aria-pressed')).toBe('true');
  });

  it('rotates the toggle icon while the sidebar is collapsed', () => {
    fixture.componentRef.setInput('isSidebarCollapsed', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.toggle-btn .icon')?.classList).toContain('rotated');
  });
});
