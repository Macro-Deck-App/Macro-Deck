import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { GetBackupStatusResponse, UserNotification } from '@macro-deck/runtime';
import { BackupService } from '../../../services/backup.service';
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

  it('styles a Success notification as success', () => {
    fixture.componentRef.setInput('notification', notification({ severity: 'Success' }));
    fixture.detectChanges();

    const glyph = fixture.nativeElement.querySelector('.ni-glyph') as HTMLElement;
    expect(glyph.classList).toContain('ni-glyph-success');
    expect(glyph.querySelector('.icon-circle-check')).not.toBeNull();
  });

  it('gives every severity its own icon, so an error never looks like a warning', () => {
    const iconOf = (severity: UserNotification['severity']): string => {
      fixture.componentRef.setInput('notification', notification({ severity }));
      fixture.detectChanges();
      const icon = fixture.nativeElement.querySelector('.ni-glyph .icon') as HTMLElement;
      return Array.from(icon.classList).find(name => name.startsWith('icon-') && !/^icon-(xs|sm|md|lg|xl)$/.test(name))!;
    };

    const icons = (['Info', 'Success', 'Warning', 'Error'] as const).map(iconOf);

    expect(new Set(icons).size).toBe(4);
  });

  it('makes the main action of a notification that needs the user primary and keeps the rest secondary', () => {
    fixture.componentRef.setInput('attention', true);
    fixture.componentRef.setInput('notification', notification({
      actions: [{ kind: 'OpenUpdateDetails' }, { kind: 'InstallUpdate' }, { kind: 'DismissNotification' }],
    }));
    fixture.detectChanges();

    const classes = Array.from(fixture.nativeElement.querySelectorAll('.ni-action button'))
      .map(button => (button as HTMLElement).className);

    expect(classes[0]).toContain('sb-secondary');
    expect(classes[1]).toContain('sb-primary');
    expect(classes[2]).toContain('sb-ghost');
  });

  it('disables its actions and swallows clicks while a response is pending', () => {
    const emitted: unknown[] = [];
    fixture.componentInstance.action.subscribe(event => emitted.push(event));
    fixture.componentRef.setInput('attention', true);
    fixture.componentRef.setInput('pending', true);
    fixture.componentRef.setInput('notification', notification({ actions: [{ kind: 'EnablePluginAdb' }] }));
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.ni-action button') as HTMLButtonElement;
    expect(button.disabled).toBeTrue();
    fixture.componentInstance.onAction(new MouseEvent('click'), { kind: 'EnablePluginAdb' });

    expect(emitted).toEqual([]);
  });

  it('marks an unread entry and offers to mark it read, but only that one', () => {
    const marked: string[] = [];
    fixture.componentInstance.markRead.subscribe(id => marked.push(id));

    fixture.componentRef.setInput('notification', notification({ read: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ni-unread-dot')).toBeNull();
    expect(fixture.nativeElement.querySelector('.ni-mark-read')).toBeNull();

    fixture.componentRef.setInput('notification', notification({ read: false }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ni-title')?.textContent).toContain('Unread');
    expect(fixture.nativeElement.querySelector('.ni-mark-read button')?.getAttribute('aria-label')).toBe('Mark as read');

    (fixture.nativeElement.querySelector('.ni-mark-read button') as HTMLButtonElement).click();
    expect(marked).toEqual(['n1']);
  });

  it('names where a notification came from, with the source name unless it is the system', () => {
    fixture.componentRef.setInput(
      'notification',
      notification({ sourceCategory: 'Plugin', sourceName: 'Lights' }),
    );
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('.ni-source') as HTMLElement).textContent).toContain('Plugin');
    expect((fixture.nativeElement.querySelector('.ni-source') as HTMLElement).textContent).toContain('Lights');

    fixture.componentRef.setInput(
      'notification',
      notification({ sourceCategory: 'System', sourceName: 'Macro Deck' }),
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.ni-source-name')).toBeNull();
    expect((fixture.nativeElement.querySelector('.ni-source') as HTMLElement).textContent).toContain('Macro Deck');
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

  describe('button size', () => {
    let probe: HTMLElement;

    beforeEach(() => {
      probe = document.createElement('div');
      probe.style.height = 'var(--control-height)';
      document.body.appendChild(probe);
    });

    afterEach(() => probe.remove());

    function heightOf(selector: string): number {
      const button = fixture.nativeElement.querySelector(`${selector} button`) as HTMLElement;
      return button.getBoundingClientRect().height;
    }

    it('renders action, cancel and dismiss at the regular control height', () => {
      fixture.componentRef.setInput(
        'notification',
        notification({ action: { kind: 'OpenIntegration' }, cancelKey: 'batch-1' }),
      );
      fixture.detectChanges();
      const controlHeight = probe.getBoundingClientRect().height;

      expect(controlHeight).toBeGreaterThan(0);
      expect(heightOf('.ni-action:not(.ni-cancel-action)')).toBeGreaterThanOrEqual(controlHeight);
      expect(heightOf('.ni-cancel-action')).toBeGreaterThanOrEqual(controlHeight);
      expect(heightOf('.ni-dismiss')).toBeGreaterThanOrEqual(controlHeight);
    });

    it('names the icon-only dismiss button for assistive technology', () => {
      fixture.componentRef.setInput('notification', notification());
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.ni-dismiss button') as HTMLElement;
      expect(button.getAttribute('aria-label')).toBe('Dismiss notification');
    });
  });
});

describe('NotificationItemComponent for a running backup', () => {
  let fixture: ComponentFixture<NotificationItemComponent>;
  const operation = signal<GetBackupStatusResponse | null>(null);

  beforeEach(async () => {
    operation.set({
      operationId: 'op-1',
      kind: 'Create',
      stage: 'Saving',
      trigger: 'Scheduled',
      percentComplete: 42,
      etaSeconds: 30,
      updatedAt: '2026-07-28T10:00:00+00:00',
    });
    await TestBed.configureTestingModule({
      imports: [NotificationItemComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: BackupService, useValue: { operation } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(NotificationItemComponent);
    fixture.componentRef.setInput('notification', {
      id: 'n1',
      sequence: 1,
      timestamp: '2026-07-28T10:00:00+00:00',
      severity: 'Info',
      kind: 'General',
      title: 'Creating backup',
      progress: { processed: 40, total: 100 },
      cancelKey: 'backup:op-1',
    } satisfies UserNotification);
    fixture.detectChanges();
  });

  it('shows the same backup card as the dialog, with the live percentage and remaining time', () => {
    const card = fixture.nativeElement.querySelector('shared-backup-progress-card') as HTMLElement;

    expect(card).toBeTruthy();
    expect(card.querySelector('.bpc-title')?.textContent).toContain('Creating backup');
    expect(card.querySelector('.bpc-percent')?.textContent?.trim()).toBe('42%');
    expect(card.querySelector('.bpc-detail')?.textContent).toContain('30 s');
    expect(card.querySelector('.bpc-stages')).toBeNull();
  });

  it('offers to show the full dialog again', () => {
    let shown = 0;
    fixture.componentInstance.showDetails.subscribe(() => shown++);

    const button = Array.from(fixture.nativeElement.querySelectorAll('.ni-action button') as NodeListOf<HTMLButtonElement>)
      .find(b => b.textContent?.includes('Show details'));
    button!.click();

    expect(shown).toBe(1);
  });
});
