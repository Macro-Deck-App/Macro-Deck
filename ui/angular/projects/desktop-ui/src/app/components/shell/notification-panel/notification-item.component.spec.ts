import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UserNotification } from '@macro-deck/runtime';
import { NotificationItemComponent } from './notification-item.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('NotificationItemComponent', () => {
  let fixture: ComponentFixture<NotificationItemComponent>;

  function notification(overrides: Partial<UserNotification> = {}): UserNotification {
    return {
      id: 'n1',
      sequence: 1,
      timestamp: '2026-07-28T10:00:00+00:00',
      severity: 'Info',
      kind: 'IconImport',
      title: 'Importing icons into My Pack',
      ...overrides,
    };
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationItemComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationItemComponent);
  });

  it('labels the action button from the action kind, not from the host', () => {
    fixture.componentRef.setInput('notification', notification({ action: { kind: 'OpenUpdateSettings' } }));
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.ni-action') as HTMLElement;
    expect(button.textContent?.trim()).toBe('View update');
  });

  it('labels every action kind that carries an action', () => {
    const expected: Record<string, string> = {
      OpenIntegration: 'Open integration',
      OpenLogs: 'Open logs',
      OpenIconPacks: 'Open icon packs',
      OpenUpdateSettings: 'View update',
      OpenExtensionStore: 'Open the store',
      RestartApplication: 'Restart now',
    };

    for (const [kind, label] of Object.entries(expected)) {
      fixture.componentRef.setInput('notification', notification({ action: { kind } as UserNotification['action'] }));
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.ni-action') as HTMLElement;
      expect(button.textContent?.trim()).withContext(kind).toBe(label);
    }
  });

  it('renders no action button for the None kind', () => {
    fixture.componentRef.setInput('notification', notification({ action: { kind: 'None' } }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ni-action')).toBeNull();
  });

  it('renders no progress bar when the notification carries no progress', () => {
    fixture.componentRef.setInput('notification', notification());
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ni-progress-bar')).toBeNull();
  });

  it('renders a determinate bar with the right aria-valuenow when total is known', () => {
    fixture.componentRef.setInput(
      'notification',
      notification({ progress: { processed: 1046, total: 2520 } }),
    );
    fixture.detectChanges();

    const bar = fixture.nativeElement.querySelector('.ni-progress-bar') as HTMLElement;
    expect(bar).not.toBeNull();
    expect(bar.classList).not.toContain('indeterminate');
    expect(bar.getAttribute('aria-valuenow')).toBe('42');
    expect(bar.getAttribute('aria-valuemin')).toBe('0');
    expect(bar.getAttribute('aria-valuemax')).toBe('100');

    const label = fixture.nativeElement.querySelector('.ni-progress-label') as HTMLElement;
    expect(label.textContent?.trim()).toBe('Processing 1046 of 2520');
  });

  it('renders an indeterminate bar with no aria-valuenow when total is null', () => {
    fixture.componentRef.setInput(
      'notification',
      notification({ progress: { processed: 12, total: null } }),
    );
    fixture.detectChanges();

    const bar = fixture.nativeElement.querySelector('.ni-progress-bar') as HTMLElement;
    expect(bar).not.toBeNull();
    expect(bar.classList).toContain('indeterminate');
    expect(bar.hasAttribute('aria-valuenow')).toBe(false);

    const label = fixture.nativeElement.querySelector('.ni-progress-label') as HTMLElement;
    expect(label.textContent?.trim()).toBe('Processing 12');
  });

  it('renders three buttons for a notification carrying three actions, each correctly labelled', () => {
    fixture.componentRef.setInput(
      'notification',
      notification({
        actions: [{ kind: 'OpenUpdateDetails' }, { kind: 'InstallUpdate' }, { kind: 'DismissNotification' }],
      }),
    );
    fixture.detectChanges();

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('.ni-action')) as HTMLElement[];
    expect(buttons.map(button => button.textContent?.trim())).toEqual(['Details', 'Install', 'Later']);
  });

  it('renders exactly one button for a notification that only carries the legacy singular action', () => {
    fixture.componentRef.setInput('notification', notification({ action: { kind: 'InstallUpdate' } }));
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('.ni-action');
    expect(buttons.length).toBe(1);
    expect((buttons[0] as HTMLElement).textContent?.trim()).toBe('Install');
  });

  it('emits the notification and the exact action that was clicked', () => {
    fixture.componentRef.setInput(
      'notification',
      notification({ id: 'update-1', actions: [{ kind: 'OpenUpdateDetails' }, { kind: 'InstallUpdate' }] }),
    );
    fixture.detectChanges();
    const actionSpy = jasmine.createSpy('action');
    fixture.componentInstance.action.subscribe(actionSpy);

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('.ni-action')) as HTMLButtonElement[];
    buttons[1].click();

    expect(actionSpy).toHaveBeenCalledWith(
      jasmine.objectContaining({
        notification: jasmine.objectContaining({ id: 'update-1' }),
        action: { kind: 'InstallUpdate' },
      }),
    );
  });

  it('renders no cancel button when the notification carries no cancelKey', () => {
    fixture.componentRef.setInput('notification', notification());
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ni-cancel-action')).toBeNull();
  });

  it('renders a cancel button when the notification carries a cancelKey', () => {
    fixture.componentRef.setInput('notification', notification({ cancelKey: 'batch-1' }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ni-cancel-action')).not.toBeNull();
  });

  it('emits cancel with the notification', () => {
    fixture.componentRef.setInput('notification', notification({ cancelKey: 'batch-1' }));
    fixture.detectChanges();
    const cancelSpy = jasmine.createSpy('cancel');
    fixture.componentInstance.cancel.subscribe(cancelSpy);

    (fixture.nativeElement.querySelector('.ni-cancel-action') as HTMLButtonElement).click();

    expect(cancelSpy).toHaveBeenCalledWith(jasmine.objectContaining({ cancelKey: 'batch-1' }));
  });

  it('offers no dismiss while the work behind it is still running', () => {
    fixture.componentRef.setInput('notification', notification({ progress: { processed: 3, total: 10 } }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ni-dismiss')).toBeNull();
  });

  it('offers dismiss once nothing is running', () => {
    fixture.componentRef.setInput('notification', notification());
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ni-dismiss')).not.toBeNull();
  });

  it('fills the count into the relative time instead of leaving the placeholder', () => {
    const threeHoursAgo = new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString();
    fixture.componentRef.setInput('notification', notification({ timestamp: threeHoursAgo }));
    fixture.detectChanges();

    const time = fixture.nativeElement.querySelector('.ni-time') as HTMLElement;
    expect(time.textContent?.trim()).toBe('3h ago');
  });
});
