import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { UpdateAdbSettingsResponse, UserNotification, UserNotificationsChangedEvent } from '@macro-deck/runtime';
import { ApiService, ConnectionState, LocalizationService } from '@shared';
import { NotificationCenterService } from './notification-center.service';
import { PluginAdbConsentService } from './plugin-adb-consent.service';
import { SettingsModalService } from './settings-modal.service';

describe('PluginAdbConsentService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let changed: Subject<UserNotificationsChangedEvent>;
  let settingsModal: jasmine.SpyObj<SettingsModalService>;

  function consentRequest(id: string, sequence: number): UserNotification {
    return {
      id,
      sequence,
      timestamp: '2026-10-24T10:00:00+00:00',
      severity: 'Warning',
      kind: 'Security',
      title: `plugin-${id} wants ADB`,
      actions: [{ kind: 'EnablePluginAdb' }],
      read: false,
    };
  }

  async function flush(): Promise<void> {
    for (let i = 0; i < 8; i++) {
      await Promise.resolve();
    }
  }

  function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>(res => (resolve = res));
    return { promise, resolve };
  }

  async function setUp(list: UserNotification[]): Promise<{
    consent: PluginAdbConsentService;
    center: NotificationCenterService;
  }> {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: SettingsModalService, useValue: settingsModal },
        { provide: LocalizationService, useValue: { translateKey: (key: string) => key } },
      ],
    });
    const center = TestBed.inject(NotificationCenterService);
    const consent = TestBed.inject(PluginAdbConsentService);
    await flush();
    changed.next({ notifications: list });
    return { consent, center };
  }

  beforeEach(() => {
    changed = new Subject<UserNotificationsChangedEvent>();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'getNotifications',
      'dismissNotification',
      'updateAdbSettings',
    ]);
    Object.defineProperty(apiSpy, 'connectionState$', {
      value: new BehaviorSubject<ConnectionState>('connected').asObservable(),
    });
    Object.defineProperty(apiSpy, 'connectionStateSignal', {
      value: signal<ConnectionState>('connected').asReadonly(),
    });
    apiSpy.onNotification.and.callFake(() => changed.asObservable() as Observable<never>);
    apiSpy.getNotifications.and.resolveTo({ notifications: [] });
    settingsModal = jasmine.createSpyObj<SettingsModalService>('SettingsModalService', ['open']);
  });

  it('shows the request as pending and sends one request however often Allow is pressed', async () => {
    const response = deferred<UpdateAdbSettingsResponse>();
    apiSpy.updateAdbSettings.and.returnValue(response.promise);
    const { consent, center } = await setUp([consentRequest('a', 1)]);

    const first = consent.allow('a');
    const second = consent.allow('a');

    expect(consent.allowing()).toBe('a');
    expect(apiSpy.updateAdbSettings).toHaveBeenCalledTimes(1);
    expect(center.notifications().map(n => n.id)).toEqual(['a']);

    response.resolve({ success: true, resolvedExecutablePath: '/usr/bin/adb' } as UpdateAdbSettingsResponse);
    expect(await first).toBeTrue();
    expect(await second).toBeTrue();
    expect(consent.allowing()).toBeNull();
  });

  it('withdraws every ADB request once the host confirms, without a dismiss request', async () => {
    apiSpy.updateAdbSettings.and.resolveTo({ success: true, resolvedExecutablePath: '/usr/bin/adb' } as UpdateAdbSettingsResponse);
    const { consent, center } = await setUp([consentRequest('a', 1), consentRequest('b', 2)]);

    await consent.allow('a');

    expect(consent.pending()).toBeNull();
    expect(center.notifications()).toEqual([]);
    expect(apiSpy.dismissNotification).not.toHaveBeenCalled();
  });

  it('keeps the request and opens the ADB settings when the host refuses', async () => {
    apiSpy.updateAdbSettings.and.resolveTo({ success: false } as UpdateAdbSettingsResponse);
    const { consent, center } = await setUp([consentRequest('a', 1)]);

    expect(await consent.allow('a')).toBeFalse();

    expect(center.notifications().map(n => n.id)).toEqual(['a']);
    expect(settingsModal.open).toHaveBeenCalledWith('adb');
    expect(consent.allowing()).toBeNull();
  });
});
