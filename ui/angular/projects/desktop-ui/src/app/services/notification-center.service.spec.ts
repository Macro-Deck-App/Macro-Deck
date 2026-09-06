import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { GetUserNotificationsResponse, UserNotification, UserNotificationsChangedEvent } from '@macro-deck/runtime';
import { ApiService, ConnectionState } from '@shared';
import { NotificationCenterService } from './notification-center.service';

describe('NotificationCenterService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let connectionState: BehaviorSubject<ConnectionState>;
  let changed: Subject<UserNotificationsChangedEvent>;

  function notification(id: string, progress: UserNotification['progress'] = null): UserNotification {
    return {
      id,
      sequence: 1,
      timestamp: '2026-07-28T10:00:00+00:00',
      severity: 'Info',
      kind: 'General',
      title: `title-${id}`,
      progress,
    };
  }

  function snapshot(notifications: UserNotification[]): GetUserNotificationsResponse {
    return { notifications };
  }

  async function flush(): Promise<void> {
    for (let i = 0; i < 8; i++) {
      await Promise.resolve();
    }
  }

  function createService(): NotificationCenterService {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    return TestBed.inject(NotificationCenterService);
  }

  beforeEach(() => {
    connectionState = new BehaviorSubject<ConnectionState>('connected');
    changed = new Subject<UserNotificationsChangedEvent>();

    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService',
      [
        'onNotification',
        'getNotifications',
        'dismissNotification',
        'dismissAllNotifications',
      ],
    );
    Object.defineProperty(apiSpy, 'connectionState$', { value: connectionState.asObservable() });
    Object.defineProperty(apiSpy, 'connectionStateSignal', {
      value: signal<ConnectionState>('connected').asReadonly(),
    });

    apiSpy.onNotification.and.callFake(() => changed.asObservable() as Observable<never>);
    apiSpy.getNotifications.and.resolveTo(snapshot([]));
    apiSpy.dismissNotification.and.resolveTo(undefined);
    apiSpy.dismissAllNotifications.and.resolveTo(undefined);
  });

  it('loads the current snapshot on construction, covering the first connect', async () => {
    apiSpy.getNotifications.and.resolveTo(snapshot([notification('1')]));
    const service = createService();

    await flush();

    expect(service.notifications().map(n => n.id)).toEqual(['1']);
  });

  it('a push replaces the list wholesale (no merge) and recomputes the count', async () => {
    const service = createService();
    await flush();

    changed.next({ notifications: [notification('1'), notification('2')] });
    expect(service.notifications().map(n => n.id)).toEqual(['1', '2']);
    expect(service.count()).toBe(2);
    expect(service.hasNotifications()).toBeTrue();

    changed.next({ notifications: [] });
    expect(service.notifications()).toEqual([]);
    expect(service.count()).toBe(0);
    expect(service.hasNotifications()).toBeFalse();
  });

  it('caps the badge text at 99+', () => {
    const service = createService();
    const many = Array.from({ length: 150 }, (_, i) => notification(`n${i}`));

    changed.next({ notifications: many });

    expect(service.count()).toBe(150);
    expect(service.badgeText()).toBe('99+');
  });

  it('reports running work separately from what can be cleared', () => {
    const service = createService();

    changed.next({ notifications: [notification('running', { processed: 1, total: 10 })] });
    expect(service.hasActiveProgress()).toBeTrue();
    expect(service.hasDismissable()).toBeFalse();

    changed.next({ notifications: [notification('running', { processed: 1, total: 10 }), notification('done')] });
    expect(service.hasDismissable()).toBeTrue();
  });

  it('a push that arrives while load() is pending survives the load completing', async () => {
    const service = createService();
    await flush();
    apiSpy.getNotifications.calls.reset();

    let resolveGet!: (value: GetUserNotificationsResponse) => void;
    apiSpy.getNotifications.and.returnValue(new Promise(resolve => (resolveGet = resolve)));

    const loadPromise = service.load();
    await Promise.resolve();

    changed.next({ notifications: [notification('push')] });
    expect(service.notifications().map(n => n.id)).toEqual(['push']);

    resolveGet(snapshot([notification('stale')]));
    await loadPromise;

    expect(service.notifications().map(n => n.id)).toEqual(['push']);
  });

  it('single-flights concurrent loads', async () => {
    const service = createService();
    await flush();
    apiSpy.getNotifications.calls.reset();

    await Promise.all([service.load(), service.load()]);

    expect(apiSpy.getNotifications).toHaveBeenCalledTimes(1);
  });

  it('reloads on a reconnect', async () => {
    createService();
    await flush();
    apiSpy.getNotifications.calls.reset();

    connectionState.next('disconnected');
    connectionState.next('connected');
    await flush();

    expect(apiSpy.getNotifications).toHaveBeenCalled();
  });

  it('dismiss calls the API and does not write local state directly', async () => {
    const service = createService();
    await flush();
    changed.next({ notifications: [notification('1')] });

    service.dismiss('1');

    expect(apiSpy.dismissNotification).toHaveBeenCalledWith('1');
    expect(service.notifications().map(n => n.id)).toEqual(['1']);
  });

  it('dismissAll calls the API and does not write local state directly', async () => {
    const service = createService();
    await flush();
    changed.next({ notifications: [notification('1')] });

    service.dismissAll();

    expect(apiSpy.dismissAllNotifications).toHaveBeenCalled();
    expect(service.notifications().map(n => n.id)).toEqual(['1']);
  });
});
