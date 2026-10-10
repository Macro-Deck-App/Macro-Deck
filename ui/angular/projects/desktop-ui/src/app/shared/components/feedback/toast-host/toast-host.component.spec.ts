import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ActiveNotification } from '@macro-deck/runtime';
import { NotificationPopupService } from '../../../services/notification-popup.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastService } from '../../../services/toast.service';
import { ToastHostComponent } from './toast-host.component';
import { provideLocalizationTesting } from '../../../localization/localization-test-support';

describe('ToastHostComponent', () => {
  let fixture: ComponentFixture<ToastHostComponent>;
  let toasts: ToastService;
  let popupToasts: ReturnType<typeof signal<ActiveNotification[]>>;
  let respond: jasmine.Spy;

  function hostToast(id: string, overrides: Partial<ActiveNotification> = {}): ActiveNotification {
    return {
      id,
      revision: 1,
      sourceCategory: 'plugin',
      sourceId: 'lights',
      sourceName: 'Lights',
      title: `Title ${id}`,
      severity: 'warning',
      popup: 'toast',
      inbox: false,
      dismissible: true,
      createdAt: '2026-07-28T10:00:00.000Z',
      actions: [],
      ...overrides,
    };
  }

  beforeEach(async () => {
    popupToasts = signal<ActiveNotification[]>([]);
    respond = jasmine.createSpy('respond').and.resolveTo({ status: 'accepted' });

    await TestBed.configureTestingModule({
      imports: [ToastHostComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        ToastService,
        { provide: NotificationPopupService, useValue: { toasts: popupToasts, respond } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ToastHostComponent);
    toasts = TestBed.inject(ToastService);
    fixture.detectChanges();
  });

  it('renders nothing while the queue is empty', () => {
    expect(fixture.nativeElement.querySelectorAll('.th-toast').length).toBe(0);
  });

  it('renders the message and detail of a queued toast', async () => {
    toasts.show('Profile exported', { detail: 'Saved to /tmp/deck.macroDeckProfile', durationMs: 0 });
    await fixture.whenStable();

    const toast = fixture.nativeElement.querySelector('.th-toast') as HTMLElement;
    expect(toast.querySelector('.th-message')?.textContent).toContain('Profile exported');
    expect(toast.querySelector('.th-detail')?.textContent).toContain('Saved to /tmp/deck.macroDeckProfile');
    expect(toast.classList).not.toContain('th-error');
  });

  it('marks an error toast', async () => {
    toasts.show('Export failed', { variant: 'error', durationMs: 0 });
    await fixture.whenStable();

    expect((fixture.nativeElement.querySelector('.th-toast') as HTMLElement).classList).toContain('th-error');
  });

  it('dismisses a toast from its close button', async () => {
    toasts.show('Profile exported', { durationMs: 0 });
    await fixture.whenStable();

    (fixture.nativeElement.querySelector('.th-dismiss') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('.th-toast').length).toBe(0);
    expect(toasts.toasts()).toEqual([]);
  });

  it('shows a host notification in the same host element as an app toast, with its severity and source', async () => {
    toasts.show('Profile exported', { durationMs: 0 });
    popupToasts.set([hostToast('a', { message: 'Bulb 3 is offline' })]);
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('.th').length).toBe(1);
    const items = fixture.nativeElement.querySelectorAll('.th-toast') as NodeListOf<HTMLElement>;
    expect(items.length).toBe(2);

    const host = items[1];
    expect(host.classList).toContain('th-warning');
    expect(host.querySelector('.th-message')?.textContent).toContain('Title a');
    expect(host.querySelector('.th-detail')?.textContent).toContain('Bulb 3 is offline');
    expect(host.querySelector('.th-source-category')?.textContent).toContain('Plugin');
    expect(host.querySelector('.th-source-name')?.textContent).toContain('Lights');
  });

  it('answers the host from an action button and from the close button', async () => {
    popupToasts.set([hostToast('a', { actions: [{ id: 'undo', label: 'Undo', style: 'primary' }] })]);
    await fixture.whenStable();

    (fixture.nativeElement.querySelector('.th-actions button') as HTMLButtonElement).click();
    expect(respond).toHaveBeenCalledWith('a', 'undo');

    (fixture.nativeElement.querySelector('.th-dismiss') as HTMLButtonElement).click();
    expect(respond).toHaveBeenCalledWith('a', null);
  });

  it('offers no close button on a notification the host marked not dismissible', async () => {
    popupToasts.set([hostToast('a', { dismissible: false })]);
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.th-dismiss')).toBeNull();
  });

  it('shows at most three toasts and queues the rest until room frees up', async () => {
    popupToasts.set(['a', 'b', 'c', 'd'].map(id => hostToast(id)));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelectorAll('.th-toast').length).toBe(3);

    popupToasts.set(['d'].map(id => hostToast(id)));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelectorAll('.th-toast').length).toBe(1);
  });
});
