import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Subject } from 'rxjs';
import { UserNotification, UserNotificationsChangedEvent } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { provideLocalizationTesting } from '../../testing/localization-test-support';
import { SecretApprovalService } from './secret-approval.service';

describe('SecretApprovalService', () => {
  let api: jasmine.SpyObj<ApiService>;
  let connection: BehaviorSubject<string>;
  let events: Subject<UserNotificationsChangedEvent>;

  function approvalNotification(id: string, sequence: number): UserNotification {
    return {
      id,
      sequence,
      timestamp: '2026-10-28T10:00:00+00:00',
      severity: 'Warning',
      kind: 'Security',
      title: 'A step is waiting for your approval',
      action: { kind: 'OpenSecretApprovals' }
    };
  }

  async function flush(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve));
  }

  beforeEach(() => {
    events = new Subject();
    connection = new BehaviorSubject('disconnected');
    api = jasmine.createSpyObj<ApiService>('ApiService', ['getSecretApprovals', 'approveSecretBlock', 'onNotification']);
    api.onNotification.and.callFake(method => (method === 'UserNotificationsChangedEvent' ? events : new Subject()) as never);
    Object.assign(api, { connectionState$: connection });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: ApiService, useValue: api }
      ]
    });
  });

  it('loads the pending count once the host is connected', async () => {
    api.getSecretApprovals.and.resolveTo({ count: 2, items: [] });
    const service = TestBed.inject(SecretApprovalService);
    await flush();
    expect(api.getSecretApprovals).not.toHaveBeenCalled();

    connection.next('connected');
    await flush();

    expect(service.count()).toBe(2);
  });

  it('refreshes the count when a notification asks for an approval, and not for unrelated ones', async () => {
    api.getSecretApprovals.and.resolveTo({ count: 1, items: [] });
    const service = TestBed.inject(SecretApprovalService);
    await flush();

    events.next({ notifications: [{ ...approvalNotification('other', 1), action: { kind: 'OpenLogs' } }] });
    await flush();
    expect(api.getSecretApprovals).not.toHaveBeenCalled();

    events.next({ notifications: [approvalNotification('approval', 2)] });
    await flush();
    expect(api.getSecretApprovals).toHaveBeenCalledTimes(1);
    expect(service.count()).toBe(1);

    events.next({ notifications: [approvalNotification('approval', 2)] });
    await flush();
    expect(api.getSecretApprovals).toHaveBeenCalledTimes(1);
  });
});
