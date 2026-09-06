import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { PluginAccessToken, PluginSessionInfo } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { PluginTokenService } from './plugin-token.service';

function token(id: string, overrides: Partial<PluginAccessToken> = {}): PluginAccessToken {
  return {
    id,
    name: `Token ${id}`,
    scopes: [],
    createdAt: '2026-08-01T00:00:00Z',
    registrations: [],
    activeSessionCount: 0,
    ...overrides,
  };
}

function session(id: string, overrides: Partial<PluginSessionInfo> = {}): PluginSessionInfo {
  return {
    sessionId: id,
    pluginId: `plugin-${id}`,
    tokenId: 't1',
    origin: 'self-registered',
    negotiatedVersion: 1,
    state: 'connected',
    connectedAt: '2026-08-01T00:00:00Z',
    lastSeenAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

describe('PluginTokenService', () => {
  let service: PluginTokenService;
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
      'onNotification', 'getPluginTokens', 'getPluginSessions', 'createPluginToken',
      'revokePluginToken', 'deletePluginToken', 'terminatePluginSession',
    ]);
    apiSpy.onNotification.and.callFake((method: string) => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    });
    apiSpy.getPluginTokens.and.resolveTo({ tokens: [] });
    apiSpy.getPluginSessions.and.resolveTo({ sessions: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(PluginTokenService);
    api = apiSpy;
  });

  describe('cache repair', () => {
    it('pulls a fresh snapshot of tokens and sessions when the transport connects', async () => {
      api.getPluginTokens.and.resolveTo({ tokens: [token('t1')] });
      api.getPluginSessions.and.resolveTo({ sessions: [session('s1')] });

      connectionState.set('connected');
      await service.load();
      TestBed.tick();

      expect(api.getPluginTokens).toHaveBeenCalled();
      expect(api.getPluginSessions).toHaveBeenCalled();
      expect(service.tokens().map(t => t.id)).toEqual(['t1']);
      expect(service.sessions().map(s => s.sessionId)).toEqual(['s1']);
    });

    it('pulls again on a reconnect, so state missed while offline is repaired', async () => {
      connectionState.set('connected');
      TestBed.tick();
      await service.load();
      const afterFirst = api.getPluginTokens.calls.count();

      connectionState.set('disconnected');
      TestBed.tick();
      connectionState.set('connected');
      TestBed.tick();
      await service.load();

      expect(api.getPluginTokens.calls.count()).toBeGreaterThan(afterFirst);
    });

    it('collapses concurrent loads into one pair of requests', async () => {
      const before = api.getPluginTokens.calls.count();

      await Promise.all([service.load(), service.load(), service.load()]);

      expect(api.getPluginTokens.calls.count()).toBe(before + 1);
      expect(api.getPluginSessions.calls.count()).toBe(before + 1);
    });

    it('sets loadError and leaves the caches untouched on failure', async () => {
      api.getPluginTokens.and.rejectWith(new Error('boom'));

      await service.load();

      expect(service.loadError()).toBe('boom');
      expect(service.tokens()).toEqual([]);
    });
  });

  describe('push subscriptions', () => {
    it('reloads on PluginTokensChangedEvent', () => {
      api.getPluginTokens.and.resolveTo({ tokens: [token('t1')] });

      push('PluginTokensChangedEvent', {});

      expect(api.getPluginTokens).toHaveBeenCalled();
    });

    it('reloads on PluginSessionsChangedEvent', () => {
      api.getPluginSessions.and.resolveTo({ sessions: [session('s1')] });

      push('PluginSessionsChangedEvent', {});

      expect(api.getPluginSessions).toHaveBeenCalled();
    });
  });

  describe('createToken', () => {
    it('upserts the returned token but returns the plaintext to the caller', async () => {
      const created = token('t1', { name: 'CI bot' });
      api.createPluginToken.and.resolveTo({ token: created, plaintext: 'secret-value' });

      const response = await service.createToken({ name: 'CI bot' });

      expect(response.plaintext).toBe('secret-value');
      expect(service.tokens()).toEqual([created]);
    });

    it('never stores the plaintext on the cached token', async () => {
      api.createPluginToken.and.resolveTo({ token: token('t1'), plaintext: 'secret-value' });

      await service.createToken({ name: 'CI bot' });

      expect(JSON.stringify(service.tokens())).not.toContain('secret-value');
    });
  });

  describe('revokeToken', () => {
    it('marks the token revoked and drops its sessions locally on success', async () => {
      api.getPluginTokens.and.resolveTo({ tokens: [token('t1', { activeSessionCount: 2 })] });
      api.getPluginSessions.and.resolveTo({ sessions: [session('s1', { tokenId: 't1' }), session('s2', { tokenId: 't2' })] });
      connectionState.set('connected');
      await service.load();
      api.revokePluginToken.and.resolveTo({ success: true, terminatedSessions: 1 });

      const response = await service.revokeToken('t1');

      expect(response.terminatedSessions).toBe(1);
      expect(service.tokens()[0].revokedAt).toBeTruthy();
      expect(service.tokens()[0].activeSessionCount).toBe(0);
      expect(service.sessions().find(s => s.sessionId === 's1')?.state).toBe('dropped');
      expect(service.sessions().find(s => s.sessionId === 's2')?.state).toBe('connected');
    });

    it('leaves the cache untouched on failure', async () => {
      api.getPluginTokens.and.resolveTo({ tokens: [token('t1')] });
      connectionState.set('connected');
      await service.load();
      api.revokePluginToken.and.resolveTo({ success: false, error: { code: 'nope', message: 'nope' }, terminatedSessions: 0 });

      await service.revokeToken('t1');

      expect(service.tokens()[0].revokedAt).toBeFalsy();
    });
  });

  describe('deleteToken', () => {
    it('drops the token from the cache on success, returning the response unaltered', async () => {
      api.getPluginTokens.and.resolveTo({ tokens: [token('t1'), token('t2')] });
      connectionState.set('connected');
      await service.load();
      api.deletePluginToken.and.resolveTo({ success: true });

      const response = await service.deleteToken('t1');

      expect(response).toEqual({ success: true });
      expect(service.tokens().map(t => t.id)).toEqual(['t2']);
    });

    it('leaves the cache untouched on failure, returning the response unaltered', async () => {
      api.getPluginTokens.and.resolveTo({ tokens: [token('t1')] });
      connectionState.set('connected');
      await service.load();
      const failure = { success: false, error: { code: 'not_revoked', message: 'Revoke it first.' } };
      api.deletePluginToken.and.resolveTo(failure);

      const response = await service.deleteToken('t1');

      expect(response).toEqual(failure);
      expect(service.tokens().map(t => t.id)).toEqual(['t1']);
    });
  });

  describe('terminateSession', () => {
    it('marks the session dropped on success', async () => {
      api.getPluginSessions.and.resolveTo({ sessions: [session('s1')] });
      connectionState.set('connected');
      await service.load();
      api.terminatePluginSession.and.resolveTo({ success: true });

      await service.terminateSession('s1');

      expect(service.sessions()[0].state).toBe('dropped');
    });

    it('leaves the session untouched on failure', async () => {
      api.getPluginSessions.and.resolveTo({ sessions: [session('s1')] });
      connectionState.set('connected');
      await service.load();
      api.terminatePluginSession.and.resolveTo({ success: false, error: { code: 'nope', message: 'nope' } });

      await service.terminateSession('s1');

      expect(service.sessions()[0].state).toBe('connected');
    });
  });
});
