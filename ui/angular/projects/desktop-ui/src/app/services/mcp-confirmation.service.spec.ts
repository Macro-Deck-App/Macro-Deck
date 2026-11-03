import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { TransportError } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { McpPendingConfirmation } from '../shared/transport/mcp-messages';
import { McpConfirmationService } from './mcp-confirmation.service';

function confirmation(id: string, createdOffsetMs: number, overrides: Partial<McpPendingConfirmation> = {}): McpPendingConfirmation {
  const now = Date.now();
  return {
    id,
    clientName: 'Claude',
    lines: ['Keyboard: Press key'],
    alwaysAllowable: true,
    createdAt: new Date(now + createdOffsetMs).toISOString(),
    expiresAt: new Date(now + 5 * 60_000).toISOString(),
    ...overrides,
  };
}

describe('McpConfirmationService', () => {
  let service: McpConfirmationService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getMcpConfirmations', 'allowMcpConfirmation', 'declineMcpConfirmation',
    ]);
    api.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    api.getMcpConfirmations.and.resolveTo({ confirmations: [] });
    api.allowMcpConfirmation.and.resolveTo({ success: true });
    api.declineMcpConfirmation.and.resolveTo({ success: true });
    Object.defineProperty(api, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    });
    service = TestBed.inject(McpConfirmationService);
  });

  it('shows the oldest waiting request once the host reports a change', async () => {
    api.getMcpConfirmations.and.resolveTo({
      confirmations: [confirmation('newer', 0), confirmation('older', -1000)],
    });

    notifications.get('McpConfirmationsChangedEvent')?.next({});
    await service.load();

    expect(service.current()?.id).toBe('older');
  });

  it('picks up requests that were already waiting when the app connects', async () => {
    api.getMcpConfirmations.and.resolveTo({ confirmations: [confirmation('waiting', 0)] });

    connectionState.set('connected');
    TestBed.tick();
    await service.load();

    expect(service.current()?.id).toBe('waiting');
  });

  it('passes the answer on and stops showing the request', async () => {
    api.getMcpConfirmations.and.resolveTo({ confirmations: [confirmation('a', 0)] });
    await service.load();

    await service.allow('a', true);

    expect(api.allowMcpConfirmation).toHaveBeenCalledWith('a', true);
    expect(service.current()).toBeNull();
  });

  it('stops asking for requests where the host refuses to show them', async () => {
    api.getMcpConfirmations.and.rejectWith(new TransportError(403, 'Forbidden'));

    await service.load();
    await service.load();

    expect(service.unavailable()).toBeTrue();
    expect(api.getMcpConfirmations).toHaveBeenCalledTimes(1);
  });
});
