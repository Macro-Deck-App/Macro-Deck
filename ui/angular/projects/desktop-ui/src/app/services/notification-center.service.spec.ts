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
        'markNotificationRead',
        'markAllNotificationsRead',
      ],
    );
    Object.defineProperty(apiSpy, 'connectionState$', { value: connectionState.asObservable() });
    Object.defineProperty(apiSpy, 'connectionStateSignal', {
      value: signal<ConnectionState>('connected').asReadonly(),
    });

    apiSpy.onNotification.and.callFake(() => changed.asObservable() as Observable<never>);
    apiSpy.getNotifications.and.resolveTo(snapshot([]));
    apiSpy.dismissNotification.and.resolveTo({ success: true });
    apiSpy.dismissAllNotifications.and.resolveTo({ success: true });
    apiSpy.markNotificationRead.and.resolveTo({ success: true });
    apiSpy.markAllNotificationsRead.and.resolveTo({ success: true });
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

  describe('optimistic dismiss', () => {
    function pending<T>(): { promise: Promise<T>; resolve: (value: T) => void; reject: (error: unknown) => void } {
      let resolve!: (value: T) => void;
      let reject!: (error: unknown) => void;
      const promise = new Promise<T>((res, rej) => {
        resolve = res;
        reject = rej;
      });
      return { promise, resolve, reject };
    }

    function at(id: string, sequence: number, extra: Partial<UserNotification> = {}): UserNotification {
      return { ...notification(id), sequence, ...extra };
    }

    async function serviceWith(list: UserNotification[]): Promise<NotificationCenterService> {
      const service = createService();
      await flush();
      changed.next({ notifications: list });
      return service;
    }

    const ids = (service: NotificationCenterService): string[] => service.notifications().map(n => n.id);

    it('removes the entry and updates the count before the host answers', async () => {
      apiSpy.dismissNotification.and.returnValue(pending<{ success: boolean }>().promise);
      const service = await serviceWith([at('1', 1), at('2', 2)]);

      service.dismiss('1');

      expect(ids(service)).toEqual(['2']);
      expect(service.badgeText()).toBe('1');
      expect(apiSpy.dismissNotification).toHaveBeenCalledOnceWith('1');
    });

    it('sends one request when the same entry is dismissed twice in a row', async () => {
      apiSpy.dismissNotification.and.returnValue(pending<{ success: boolean }>().promise);
      const service = await serviceWith([at('1', 1)]);

      service.dismiss('1');
      service.dismiss('1');

      expect(apiSpy.dismissNotification).toHaveBeenCalledTimes(1);
    });

    it('keeps the entry hidden when a stale push still carries it, during and after the request', async () => {
      const request = pending<{ success: boolean }>();
      apiSpy.dismissNotification.and.returnValue(request.promise);
      const service = await serviceWith([at('1', 1), at('2', 2)]);

      service.dismiss('1');
      changed.next({ notifications: [at('1', 1), at('2', 2)] });
      expect(ids(service)).toEqual(['2']);

      request.resolve({ success: true });
      await flush();
      changed.next({ notifications: [at('1', 1), at('2', 2)] });
      expect(ids(service)).toEqual(['2']);

      changed.next({ notifications: [at('2', 2)] });
      changed.next({ notifications: [at('1', 1), at('2', 2)] });
      expect(ids(service)).toEqual(['1', '2']);
    });

    it('shows the entry again when the host raises it anew under the same id', async () => {
      const service = await serviceWith([at('1', 5)]);

      service.dismiss('1');
      await flush();
      changed.next({ notifications: [at('1', 9)] });

      expect(ids(service)).toEqual(['1']);
    });

    it('puts the entry back and reloads when the request fails', async () => {
      apiSpy.dismissNotification.and.rejectWith(new Error('offline'));
      apiSpy.getNotifications.and.resolveTo(snapshot([at('1', 1)]));
      const service = await serviceWith([at('1', 1)]);
      apiSpy.getNotifications.calls.reset();

      service.dismiss('1');
      expect(ids(service)).toEqual([]);
      await flush();

      expect(ids(service)).toEqual(['1']);
      expect(apiSpy.getNotifications).toHaveBeenCalled();
    });

    it('waits for a fetch started after a refused dismiss before showing the host state', async () => {
      const service = await serviceWith([at('1', 1)]);
      const older = pending<GetUserNotificationsResponse>();
      const fresh = pending<GetUserNotificationsResponse>();
      apiSpy.getNotifications.and.returnValues(older.promise, fresh.promise);
      void service.load();
      apiSpy.dismissNotification.and.resolveTo({ success: false });

      service.dismiss('1');
      await flush();
      older.resolve(snapshot([at('1', 1)]));
      await flush();
      expect(ids(service)).toEqual([]);

      fresh.resolve(snapshot([]));
      await flush();
      expect(ids(service)).toEqual([]);
      changed.next({ notifications: [at('1', 1)] });
      expect(ids(service)).toEqual(['1']);
    });

    it('never offers running work for dismissal', async () => {
      const service = await serviceWith([at('running', 1, { progress: { processed: 1, total: 4 } })]);

      service.dismiss('running');
      service.dismissAll();

      expect(ids(service)).toEqual(['running']);
      expect(apiSpy.dismissNotification).not.toHaveBeenCalled();
      expect(apiSpy.dismissAllNotifications).not.toHaveBeenCalled();
    });

    it('clears every finished entry in one step with one request, keeping running work', async () => {
      apiSpy.dismissAllNotifications.and.returnValue(pending<{ success: boolean }>().promise);
      const service = await serviceWith([
        at('a', 1),
        at('b', 2),
        at('running', 3, { progress: { processed: 1, total: 4 } }),
      ]);

      service.dismissAll();

      expect(ids(service)).toEqual(['running']);
      expect(apiSpy.dismissAllNotifications).toHaveBeenCalledTimes(1);
    });

    it('does not hide an entry that arrives after clear all was clicked', async () => {
      apiSpy.dismissAllNotifications.and.returnValue(pending<{ success: boolean }>().promise);
      const service = await serviceWith([at('a', 1)]);

      service.dismissAll();
      changed.next({ notifications: [at('a', 1), at('new', 2)] });

      expect(ids(service)).toEqual(['new']);
    });

    it('restores every cleared entry when clear all fails', async () => {
      apiSpy.dismissAllNotifications.and.rejectWith(new Error('offline'));
      apiSpy.getNotifications.and.resolveTo(snapshot([at('a', 1), at('b', 2)]));
      const service = await serviceWith([at('a', 1), at('b', 2)]);

      service.dismissAll();
      await flush();

      expect(ids(service)).toEqual(['a', 'b']);
    });

    it('hides entries the host already resolved without asking it again', async () => {
      const service = await serviceWith([at('a', 1, { kind: 'Security' }), at('b', 2, { kind: 'Security' }), at('c', 3)]);

      service.resolveLocally(n => n.kind === 'Security');
      changed.next({ notifications: [at('a', 1, { kind: 'Security' }), at('b', 2, { kind: 'Security' }), at('c', 3)] });

      expect(ids(service)).toEqual(['c']);
      expect(apiSpy.dismissNotification).not.toHaveBeenCalled();
    });
  });

  describe('optimistic read state', () => {
    it('keeps an entry read when a stale push still says unread', async () => {
      const service = createService();
      await flush();
      changed.next({ notifications: [{ ...notification('1'), read: false }] });

      service.markRead('1');
      await flush();
      changed.next({ notifications: [{ ...notification('1'), read: false }] });

      expect(service.hasUnread()).toBeFalse();
    });

    it('shows a re-raised entry as unread again', async () => {
      const service = createService();
      await flush();
      changed.next({ notifications: [{ ...notification('1'), sequence: 3, read: false }] });

      service.markRead('1');
      await flush();
      changed.next({ notifications: [{ ...notification('1'), sequence: 7, read: false }] });

      expect(service.hasUnread()).toBeTrue();
    });

    it('does not ask the host to mark an entry that is already read', async () => {
      const service = createService();
      await flush();
      changed.next({ notifications: [{ ...notification('1'), read: true }] });

      service.markRead('1');
      service.markAllRead();

      expect(apiSpy.markNotificationRead).not.toHaveBeenCalled();
      expect(apiSpy.markAllNotificationsRead).not.toHaveBeenCalled();
    });
  });

  it('marks one entry read at once and asks the host to remember it', async () => {
    const service = createService();
    await flush();
    changed.next({ notifications: [{ ...notification('1'), read: false }, { ...notification('2'), read: false }] });

    service.markRead('1');

    expect(service.notifications().map(n => n.read)).toEqual([true, false]);
    expect(service.hasUnread()).toBeTrue();
    expect(apiSpy.markNotificationRead).toHaveBeenCalledOnceWith('1');
  });

  it('marks everything read at once', async () => {
    const service = createService();
    await flush();
    changed.next({ notifications: [{ ...notification('1'), read: false }, { ...notification('2'), read: false }] });

    service.markAllRead();

    expect(service.hasUnread()).toBeFalse();
    expect(apiSpy.markAllNotificationsRead).toHaveBeenCalled();
  });

  it('takes the host list back when it refuses to mark an entry read', async () => {
    const service = createService();
    await flush();
    changed.next({ notifications: [{ ...notification('1'), read: false }] });
    apiSpy.markNotificationRead.and.resolveTo({ success: false });
    apiSpy.getNotifications.and.resolveTo(snapshot([{ ...notification('1'), read: false }]));

    service.markRead('1');
    await flush();

    expect(service.notifications()[0].read).toBeFalse();
  });
});
