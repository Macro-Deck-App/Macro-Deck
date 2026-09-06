import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { PairedPlugin, PendingPluginPairingRequest, TransportError } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { PluginPairingService } from './plugin-pairing.service';

function request(
  requestId: string,
  overrides: Partial<PendingPluginPairingRequest> = {},
): PendingPluginPairingRequest {
  // Relative to the real clock on purpose: `current` hides requests whose expiresAt has passed, so a
  // fixture pinned to an absolute date silently stops exercising anything once that date goes by.
  const now = Date.now();

  return {
    requestId,
    pluginId: `com.example.${requestId}`,
    displayName: `Plugin ${requestId}`,
    client: null,
    createdAt: new Date(now).toISOString(),
    expiresAt: new Date(now + 5 * 60_000).toISOString(),
    replacesExistingRegistration: false,
    existingRegistrationOrigin: null,
    existingRegistrationCreatedAt: null,
    arrivedOnPublicListener: false,
    ...overrides,
  };
}

function pairedPlugin(pluginId: string, overrides: Partial<PairedPlugin> = {}): PairedPlugin {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    createdAt: '2026-08-01T00:00:00Z',
    lastSeenAt: null,
    online: false,
    ...overrides,
  };
}

describe('PluginPairingService', () => {
  let service: PluginPairingService;
  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'getPluginPairingRequests', 'getPairedPlugins',
      'approvePluginPairingRequest', 'rejectPluginPairingRequest', 'revokePairedPlugin',
    ]);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getPluginPairingRequests.and.resolveTo({ requests: [] });
    apiSpy.getPairedPlugins.and.resolveTo({ registrations: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(PluginPairingService);
    api = apiSpy;
  });

  describe('cache repair', () => {
    it('pulls a fresh snapshot of requests and paired plugins when the transport connects', async () => {
      api.getPluginPairingRequests.and.resolveTo({ requests: [request('r1')] });
      api.getPairedPlugins.and.resolveTo({ registrations: [pairedPlugin('p1')] });

      connectionState.set('connected');
      await service.load();
      TestBed.tick();

      expect(api.getPluginPairingRequests).toHaveBeenCalled();
      expect(api.getPairedPlugins).toHaveBeenCalled();
      expect(service.pendingRequests().map(r => r.requestId)).toEqual(['r1']);
      expect(service.pairedPlugins().map(p => p.pluginId)).toEqual(['p1']);
    });

    it('reloads on PluginPairingRequestsChangedEvent, so state a missed push left stale is repaired', () => {
      api.getPluginPairingRequests.and.resolveTo({ requests: [request('r1')] });

      push('PluginPairingRequestsChangedEvent', {});

      expect(api.getPluginPairingRequests).toHaveBeenCalled();
    });

    it('also reloads on PluginTokensChangedEvent, so a revoke from the tokens tab repairs the paired list', () => {
      api.getPairedPlugins.and.resolveTo({ registrations: [] });

      push('PluginTokensChangedEvent', {});

      expect(api.getPairedPlugins).toHaveBeenCalled();
    });
  });

  describe('when the pairing endpoints are not trusted on this transport (LAN admin UI, 403)', () => {
    it('leaves no error and no cached state, and stops calling the host on later loads', async () => {
      api.getPluginPairingRequests.and.rejectWith(new TransportError(403, 'Forbidden'));
      api.getPairedPlugins.and.resolveTo({ registrations: [] });
      connectionState.set('connected');
      await service.load();

      expect(service.loadError()).toBeNull();
      expect(service.unavailable()).toBeTrue();
      expect(service.pendingRequests()).toEqual([]);
      expect(service.pairedPlugins()).toEqual([]);

      api.getPluginPairingRequests.calls.reset();
      connectionState.set('disconnected');
      connectionState.set('connected');
      await service.load();

      expect(api.getPluginPairingRequests).not.toHaveBeenCalled();
    });
  });

  describe('current', () => {
    it('returns the oldest non-expired request', async () => {
      const now = Date.now();
      api.getPluginPairingRequests.and.resolveTo({
        requests: [
          request('newer', { createdAt: new Date(now - 60_000).toISOString() }),
          request('older', { createdAt: new Date(now - 120_000).toISOString() }),
        ],
      });
      connectionState.set('connected');
      await service.load();

      expect(service.current()?.requestId).toBe('older');
      expect(service.queuedCount()).toBe(1);
    });

    it('skips a request whose expiresAt has already passed', async () => {
      const expired = request('expired', { expiresAt: '2000-01-01T00:00:00Z' });
      const live = request('live');
      api.getPluginPairingRequests.and.resolveTo({ requests: [expired, live] });
      connectionState.set('connected');
      await service.load();

      expect(service.current()?.requestId).toBe('live');
    });

    it('returns null when there is nothing pending', () => {
      expect(service.current()).toBeNull();
      expect(service.queuedCount()).toBe(0);
    });
  });

  describe('approve', () => {
    it('passes the replacement flag through unchanged and removes the request from the cache on success', async () => {
      api.getPluginPairingRequests.and.resolveTo({ requests: [request('r1')] });
      connectionState.set('connected');
      await service.load();
      api.approvePluginPairingRequest.and.resolveTo({ success: true });

      const result = await service.approve('r1', true);

      expect(result).toBeTrue();
      expect(api.approvePluginPairingRequest).toHaveBeenCalledWith('r1', { replaceExistingRegistration: true });
      expect(service.pendingRequests()).toEqual([]);
    });

    it('leaves the cache untouched on failure', async () => {
      api.getPluginPairingRequests.and.resolveTo({ requests: [request('r1')] });
      connectionState.set('connected');
      await service.load();
      api.approvePluginPairingRequest.and.resolveTo({
        success: false, error: { code: 'not_found', message: 'Gone.' },
      });

      const result = await service.approve('r1', false);

      expect(result).toBeFalse();
      expect(service.pendingRequests().map(r => r.requestId)).toEqual(['r1']);
    });
  });

  describe('reject', () => {
    it('removes the request from the cache on success', async () => {
      api.getPluginPairingRequests.and.resolveTo({ requests: [request('r1')] });
      connectionState.set('connected');
      await service.load();
      api.rejectPluginPairingRequest.and.resolveTo({ success: true });

      await service.reject('r1');

      expect(service.pendingRequests()).toEqual([]);
    });
  });

  describe('revoke', () => {
    it('removes the plugin from the local cache optimistically', async () => {
      api.getPairedPlugins.and.resolveTo({ registrations: [pairedPlugin('p1'), pairedPlugin('p2')] });
      connectionState.set('connected');
      await service.load();
      api.revokePairedPlugin.and.resolveTo(undefined);

      await service.revoke('p1');

      expect(api.revokePairedPlugin).toHaveBeenCalledWith('p1');
      expect(service.pairedPlugins().map(p => p.pluginId)).toEqual(['p2']);
    });
  });
});
