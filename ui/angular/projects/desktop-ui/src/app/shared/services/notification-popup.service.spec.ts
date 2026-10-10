import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Subject } from 'rxjs';
import {
  ActiveNotification,
  ActiveNotificationsEvent,
  NotificationClosedEvent,
  NotificationShownEvent,
  NotificationUpdatedEvent,
  RespondToNotificationResponse,
} from '@macro-deck/runtime';

import { ApiService, ConnectionState } from '../transport';
import { NotificationPopupService } from './notification-popup.service';

function notification(id: string, overrides: Partial<ActiveNotification> = {}): ActiveNotification {
  return {
    id,
    revision: 1,
    sourceCategory: 'system',
    sourceId: 'host',
    sourceName: 'Macro Deck',
    title: `title-${id}`,
    severity: 'info',
    popup: 'toast',
    inbox: false,
    dismissible: true,
    createdAt: `2026-07-28T10:00:0${id.length}.000Z`,
    actions: [],
    ...overrides,
  };
}

describe('NotificationPopupService', () => {
  let shown: Subject<NotificationShownEvent>;
  let updated: Subject<NotificationUpdatedEvent>;
  let closed: Subject<NotificationClosedEvent>;
  let snapshots: Subject<ActiveNotificationsEvent>;
  let connection: BehaviorSubject<ConnectionState>;
  let api: jasmine.SpyObj<ApiService>;
  let service: NotificationPopupService;

  function createService(): NotificationPopupService {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    });
    return TestBed.inject(NotificationPopupService);
  }

  beforeEach(() => {
    shown = new Subject();
    updated = new Subject();
    closed = new Subject();
    snapshots = new Subject();
    connection = new BehaviorSubject<ConnectionState>('disconnected');

    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotificationShown',
      'onNotificationUpdated',
      'onNotificationClosed',
      'onActiveNotifications',
      'getActiveNotifications',
      'respondToNotification',
    ]);
    Object.defineProperty(api, 'connectionState$', { value: connection.asObservable() });
    api.onNotificationShown.and.returnValue(shown.asObservable());
    api.onNotificationUpdated.and.returnValue(updated.asObservable());
    api.onNotificationClosed.and.returnValue(closed.asObservable());
    api.onActiveNotifications.and.returnValue(snapshots.asObservable());
    api.getActiveNotifications.and.resolveTo({ notifications: [] });
    api.respondToNotification.and.resolveTo({ status: 'accepted' });
  });

  afterEach(() => jasmine.clock().uninstall());

  it('shows toasts and dialogs apart, the oldest dialog first, and applies updates and closes', () => {
    service = createService();

    shown.next({ notification: notification('a', { popup: 'toast' }) });
    shown.next({ notification: notification('bb', { popup: 'dialog', createdAt: '2026-07-28T10:00:09.000Z' }) });
    shown.next({ notification: notification('ccc', { popup: 'dialog', createdAt: '2026-07-28T10:00:05.000Z' }) });

    expect(service.toasts().map(n => n.id)).toEqual(['a']);
    expect(service.dialog()?.id).toBe('ccc');

    updated.next({ notification: notification('a', { revision: 2, title: 'newer' }) });
    expect(service.toasts()[0].title).toBe('newer');

    closed.next({ id: 'ccc', revision: 2, reason: 'action', actionId: 'ok' });
    expect(service.dialog()?.id).toBe('bb');
  });

  it('takes the host snapshot after a connect as the whole truth', async () => {
    api.getActiveNotifications.and.resolveTo({ notifications: [notification('fresh')] });
    service = createService();
    shown.next({ notification: notification('stale') });
    connection.next('connected');

    for (let i = 0; i < 8; i++) {
      await Promise.resolve();
    }

    expect(service.toasts().map(n => n.id)).toEqual(['fresh']);
  });

  it('drops a queued dialog the host closed and does not bring it back from an old snapshot', () => {
    service = createService();
    shown.next({ notification: notification('a', { popup: 'dialog' }) });
    shown.next({ notification: notification('bb', { popup: 'dialog' }) });

    closed.next({ id: 'bb', revision: 2, reason: 'owner-gone' });
    snapshots.next({ notifications: [notification('a', { popup: 'dialog' }), notification('bb', { popup: 'dialog' })] });

    expect(service.dialogs().map(n => n.id)).toEqual(['a']);
  });

  it('expires a queued dialog at the time the host set, not only the visible one', () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date('2026-07-28T10:00:00.000Z'));
    service = createService();

    shown.next({ notification: notification('a', { popup: 'dialog' }) });
    shown.next({
      notification: notification('bb', { popup: 'dialog', expiresAt: '2026-07-28T10:00:02.000Z' }),
    });
    expect(service.dialogs().length).toBe(2);

    jasmine.clock().tick(2100);

    expect(service.dialogs().map(n => n.id)).toEqual(['a']);
  });

  it('answers the host with the pressed action and the values typed', async () => {
    service = createService();

    await service.respond('a', 'save', { name: 'Ada' });

    expect(api.respondToNotification).toHaveBeenCalledWith({ id: 'a', actionId: 'save', values: { name: 'Ada' } });
  });

  it('keeps the dialog while the host calls the answer invalid, and removes one it no longer knows', async () => {
    service = createService();
    shown.next({ notification: notification('a', { popup: 'dialog' }) });
    shown.next({ notification: notification('bb', { popup: 'dialog' }) });

    api.respondToNotification.and.resolveTo({ status: 'invalid' } as RespondToNotificationResponse);
    await service.respond('a', 'save', {});
    expect(service.dialog()?.id).toBe('a');

    api.respondToNotification.and.resolveTo({ status: 'already-closed' } as RespondToNotificationResponse);
    await service.respond('a', null, {});
    expect(service.dialog()?.id).toBe('bb');
  });
});
