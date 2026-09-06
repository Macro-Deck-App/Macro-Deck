import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { AuthService, AUTH_REQUIRED_SCOPE, ENROLLMENT_URL_ENVIRONMENT } from './auth.service';
import { CROSS_TAB_LOCK_ENVIRONMENT, CrossTabLockEnvironment } from './cross-tab-lock';
import { DeviceIdentityService } from './device-identity.service';
import { ApiService } from '../transport/api.service';
import { HOST_URL_RESOLVER } from '../transport/host-url';
import { AuthScope } from '@macro-deck/runtime';

async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

const TOKEN_RESPONSE = {
  accessToken: 'jwt-token',
  expiresInSeconds: 900,
  scope: 'client',
  username: 'admin',
};

const ADMIN_TOKEN_RESPONSE = { ...TOKEN_RESPONSE, scope: 'admin' };

describe('AuthService', () => {
  beforeEach(() => localStorage.clear());

  let lockRequests: string[];

  function lockEnvironment(): CrossTabLockEnvironment {
    return {
      locks: {
        request: <T>(name: string, callback: () => Promise<T>): Promise<T> => {
          lockRequests.push(name);
          return callback();
        },
      },
      storage: null,
      now: () => Date.now(),
      delay: () => Promise.resolve(),
    };
  }

  let clearedHash = false;

  function configure(
    requiredScope: AuthScope | null = null,
    hash = ''
  ): { auth: AuthService; api: ApiService } {
    lockRequests = [];
    clearedHash = false;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
        { provide: AUTH_REQUIRED_SCOPE, useValue: requiredScope },
        { provide: CROSS_TAB_LOCK_ENVIRONMENT, useFactory: lockEnvironment },
        {
          provide: ENROLLMENT_URL_ENVIRONMENT,
          useValue: { readHash: () => hash, clearHash: () => { clearedHash = true; } },
        },
      ],
    });
    return { auth: TestBed.inject(AuthService), api: TestBed.inject(ApiService) };
  }

  function mockFetch(routes: Record<string, () => Response>, isOffline?: () => boolean): jasmine.Spy {
    return spyOn(window, 'fetch').and.callFake((input: RequestInfo | URL) => {
      if (isOffline && isOffline()) return Promise.reject(new TypeError('Failed to fetch'));
      const url = String(input);
      for (const suffix of Object.keys(routes)) {
        if (url.endsWith(suffix)) {
          return Promise.resolve(routes[suffix]());
        }
      }
      return Promise.reject(new Error(`unrouted fetch: ${url}`));
    });
  }

  describe('device enrollment (issue #727)', () => {
    const session = () => new Response(JSON.stringify({
      accessToken: 'a', expiresIn: 3600, scope: 'client', username: 'dev',
      device: { deviceId: 'd1', deviceSecret: 's1' },
    }), { status: 200 });

    it('signs a device in with the credential its setup left in the URL', async () => {
      // The Car Thing has no keyboard: if this does not happen, the device reaches a login form
      // nobody can fill in.
      const { auth } = configure(null, '#enroll=tok-123');
      const fetchSpy = mockFetch({ '/api/auth/device-enrollment/redeem': session });

      await auth.bootstrap();

      expect(auth.state()).toBe('authenticated');
      expect(JSON.parse(String(fetchSpy.calls.mostRecent().args[1]?.body)).token).toBe('tok-123');
    });

    it('takes the credential out of the address bar once it is spent', async () => {
      const { auth } = configure(null, '#enroll=tok-123');
      mockFetch({ '/api/auth/device-enrollment/redeem': session });

      await auth.bootstrap();

      expect(clearedHash).toBeTrue();
    });

    it('falls back to the ordinary path when the credential is already spent', async () => {
      const { auth } = configure(null, '#enroll=stale');
      mockFetch({
        '/api/auth/device-enrollment/redeem': () => new Response('{}', { status: 401 }),
        '/api/auth/status': () => new Response(JSON.stringify({
          setupComplete: true, authenticated: false, trusted: false, scope: null, username: null,
        }), { status: 200 }),
        '/api/auth/refresh': () => new Response('{}', { status: 401 }),
      });

      await auth.bootstrap();

      expect(auth.state()).toBe('loggedOut');
    });

    it('never redeems anything on a client that was not handed a credential', async () => {
      const { auth } = configure();
      const fetchSpy = mockFetch({
        '/api/auth/status': () => new Response(JSON.stringify({
          setupComplete: true, authenticated: false, trusted: false, scope: null, username: null,
        }), { status: 200 }),
        '/api/auth/refresh': () => new Response('{}', { status: 401 }),
      });

      await auth.bootstrap();

      expect(fetchSpy.calls.all().some(call => String(call.args[0]).includes('device-enrollment')))
        .toBeFalse();
    });
  });

  it('flips to setupRequired when the host has no user yet', async () => {
    const { auth } = configure();
    mockFetch({
      '/api/auth/status': () => jsonResponse({ setupComplete: false, authenticated: false }),
    });

    await auth.bootstrap();

    expect(auth.state()).toBe('setupRequired');
  });

  it('treats a transport-trusted status (loopback) as an implicit admin session', async () => {
    const { auth } = configure();
    mockFetch({
      '/api/auth/status': () => jsonResponse(
        { setupComplete: true, authenticated: true, trusted: true, scope: 'admin', username: 'desktop' }),
    });

    await auth.bootstrap();

    expect(auth.state()).toBe('authenticated');
    expect(auth.scope()).toBe('admin');
    expect(auth.username()).toBe('desktop');
  });

  it('still refreshes when the status is only cookie-authenticated (not transport-trusted)', async () => {
    const { auth } = configure();
    let refreshCalls = 0;
    mockFetch({
      '/api/auth/status': () => jsonResponse(
        { setupComplete: true, authenticated: true, trusted: false, scope: 'client', username: 'admin' }),
      '/api/auth/refresh': () => {
        refreshCalls++;
        return jsonResponse(TOKEN_RESPONSE);
      },
    });

    await auth.bootstrap();

    expect(refreshCalls).toBe(1);
    expect(auth.state()).toBe('authenticated');
  });

  it('resumes the session via the refresh cookie during bootstrap', async () => {
    const { auth } = configure();
    mockFetch({
      '/api/auth/status': () => jsonResponse({ setupComplete: true, authenticated: false }),
      '/api/auth/refresh': () => jsonResponse(TOKEN_RESPONSE),
    });

    await auth.bootstrap();

    expect(auth.state()).toBe('authenticated');
    expect(auth.username()).toBe('admin');
  });

  it('lands on loggedOut when there is no resumable session', async () => {
    const { auth } = configure();
    mockFetch({
      '/api/auth/status': () => jsonResponse({ setupComplete: true, authenticated: false }),
      '/api/auth/refresh': () => jsonResponse({ title: 'No refresh token.' }, 401),
    });

    await auth.bootstrap();

    expect(auth.state()).toBe('loggedOut');
  });

  it('stays unknown while the host is unreachable', async () => {
    const { auth } = configure();
    spyOn(window, 'fetch').and.rejectWith(new Error('connection refused'));

    await auth.bootstrap();

    expect(auth.state()).toBe('unknown');
  });

  it('authenticates on login and surfaces the error message on failure', async () => {
    const { auth, api } = configure();
    await api.resolveBaseUrl();
    let loginStatus = 401;
    mockFetch({
      '/api/auth/login': () =>
        loginStatus === 200 ? jsonResponse(TOKEN_RESPONSE) : jsonResponse({ title: 'Invalid credentials' }, 401),
    });

    const failed = await auth.login('admin', 'wrong', 'client');
    loginStatus = 200;
    const succeeded = await auth.login('admin', 'password123', 'client');

    expect(failed.ok).toBeFalse();
    expect(failed.message).toBe('Invalid credentials');
    expect(succeeded.ok).toBeTrue();
    expect(auth.state()).toBe('authenticated');
    expect(auth.scope()).toBe('client');
  });

  it('shares a single in-flight refresh between concurrent 401 handlers', async () => {
    const { auth, api } = configure();
    await api.resolveBaseUrl();
    let refreshCalls = 0;
    mockFetch({
      '/api/auth/refresh': () => {
        refreshCalls++;
        return jsonResponse(TOKEN_RESPONSE);
      },
    });

    const hook = (auth as unknown as { handleUnauthorized: () => Promise<boolean> });
    const [first, second] = await Promise.all([hook.handleUnauthorized(), hook.handleUnauthorized()]);

    expect(first).toBeTrue();
    expect(second).toBeTrue();
    expect(refreshCalls).toBe(1);
  });

  it('proactively refreshes before the access token expires', async () => {
    jasmine.clock().install();
    try {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      let refreshCalls = 0;
      mockFetch({
        '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
        '/api/auth/refresh': () => {
          refreshCalls++;
          return jsonResponse(TOKEN_RESPONSE);
        },
      });

      await auth.login('admin', 'password123', 'client');
      expect(refreshCalls).toBe(0);

      jasmine.clock().tick(841_000);
      await flushMicrotasks();

      expect(refreshCalls).toBe(1);
      expect(auth.state()).toBe('authenticated');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('drops to loggedOut when a refresh fails mid-session', async () => {
    const { auth, api } = configure();
    await api.resolveBaseUrl();
    let refreshOk = true;
    mockFetch({
      '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
      '/api/auth/refresh': () =>
        refreshOk ? jsonResponse(TOKEN_RESPONSE) : jsonResponse({ title: 'Invalid refresh token.' }, 401),
    });

    await auth.login('admin', 'password123', 'client');
    refreshOk = false;
    const hook = (auth as unknown as { handleUnauthorized: () => Promise<boolean> });
    const retried = await hook.handleUnauthorized();

    expect(retried).toBeFalse();
    expect(auth.state()).toBe('loggedOut');
  });

  it('logs out locally even when the host call fails', async () => {
    const { auth, api } = configure();
    await api.resolveBaseUrl();
    mockFetch({
      '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
    });

    await auth.login('admin', 'password123', 'client');
    await auth.logout();

    expect(auth.state()).toBe('loggedOut');
    expect(auth.username()).toBeNull();
  });

  it('drops a resumed session below the required scope back to loggedOut', async () => {
    // A leftover client refresh cookie (e.g. from the web client on the same origin) must not
    // silently log the admin UI in with insufficient permissions - it re-prompts instead.
    const { auth } = configure('admin');
    mockFetch({
      '/api/auth/status': () => jsonResponse({ setupComplete: true, authenticated: false }),
      '/api/auth/refresh': () => jsonResponse(TOKEN_RESPONSE),
    });

    await auth.bootstrap();

    expect(auth.state()).toBe('loggedOut');
    expect(auth.scope()).toBeNull();
  });

  it('resumes a session that meets the required scope', async () => {
    const { auth } = configure('admin');
    mockFetch({
      '/api/auth/status': () => jsonResponse({ setupComplete: true, authenticated: false }),
      '/api/auth/refresh': () => jsonResponse(ADMIN_TOKEN_RESPONSE),
    });

    await auth.bootstrap();

    expect(auth.state()).toBe('authenticated');
    expect(auth.scope()).toBe('admin');
  });

  it('rejects a login whose issued scope is below the required scope', async () => {
    const { auth, api } = configure('admin');
    await api.resolveBaseUrl();
    mockFetch({
      '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
    });

    const result = await auth.login('admin', 'password123', 'client');

    expect(result.ok).toBeFalse();
    expect(auth.state()).not.toBe('authenticated');
    expect(auth.scope()).toBeNull();
  });

  it('drops to loggedOut when a mid-session refresh is downgraded below the required scope', async () => {
    const { auth, api } = configure('admin');
    await api.resolveBaseUrl();
    let refreshScope = 'admin';
    mockFetch({
      '/api/auth/login': () => jsonResponse(ADMIN_TOKEN_RESPONSE),
      '/api/auth/refresh': () => jsonResponse({ ...TOKEN_RESPONSE, scope: refreshScope }),
    });

    await auth.login('admin', 'password123', 'admin');
    refreshScope = 'client';
    const hook = (auth as unknown as { handleUnauthorized: () => Promise<boolean> });
    const retried = await hook.handleUnauthorized();

    expect(retried).toBeFalse();
    expect(auth.state()).toBe('loggedOut');
  });

  it('ends the session on a 403 only when the current scope is insufficient', async () => {
    const { auth, api } = configure('admin');
    await api.resolveBaseUrl();
    mockFetch({
      '/api/auth/login': () => jsonResponse(ADMIN_TOKEN_RESPONSE),
    });
    await auth.login('admin', 'password123', 'admin');

    const hook = (auth as unknown as {
      handleForbidden: () => void;
      _scope: { set: (value: AuthScope | null) => void };
    });

    hook.handleForbidden();
    expect(auth.state()).toBe('authenticated');

    hook._scope.set('client');
    hook.handleForbidden();
    expect(auth.state()).toBe('loggedOut');
  });

  describe('device identity (issue #250)', () => {
    it('sends a device block built from DeviceIdentityService on login', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      const fetchSpy = mockFetch({ '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE) });

      await auth.login('admin', 'password123', 'client');

      const [, init] = fetchSpy.calls.mostRecent().args as [unknown, RequestInit];
      const body = JSON.parse(init.body as string);
      expect(body.device).toBeTruthy();
      expect(body.device.clientType).toBe('unknown');
      expect(typeof body.device.proposedName).toBe('string');
    });

    it('adopts the device credential the host returns on a successful login', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({
        '/api/auth/login': () =>
          jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-1', deviceSecret: 'secret-1' } }),
      });

      await auth.login('admin', 'password123', 'client');

      expect(TestBed.inject(DeviceIdentityService).deviceId).toBe('device-1');
    });

    it('does not adopt a device when login is rejected for insufficient scope', async () => {
      const { auth, api } = configure('admin');
      await api.resolveBaseUrl();
      mockFetch({
        '/api/auth/login': () =>
          jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-1', deviceSecret: 'secret-1' } }),
      });

      await auth.login('admin', 'password123', 'client');

      expect(TestBed.inject(DeviceIdentityService).deviceId).toBeNull();
    });

    it("tracks this session's live device id from a login response", async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({
        '/api/auth/login': () => jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-1' } }),
      });

      await auth.login('admin', 'password123', 'client');

      expect(auth.currentDeviceId()).toBe('device-1');
    });

    it("updates the live device id from a refresh response too, not only login", async () => {
      const { auth, api } = configure();
      mockFetch({
        '/api/auth/status': () => jsonResponse({ setupComplete: true, authenticated: false }),
        '/api/auth/refresh': () => jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-2' } }),
      });

      await auth.bootstrap();

      expect(auth.currentDeviceId()).toBe('device-2');
    });

    it('clears the live device id when the session ends', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({
        '/api/auth/login': () => jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-1' } }),
      });

      await auth.login('admin', 'password123', 'client');
      expect(auth.currentDeviceId()).toBe('device-1');

      await auth.logout();

      expect(auth.currentDeviceId()).toBeNull();
    });

    it('treats a DeviceSessionRevokedEvent push as a lost session and disconnects (no reconnect loop)', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({ '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE) });

      // bootstrap() registers the subscription; the auth-status route is deliberately unrouted here
      // (rejects), which bootstrap() swallows, leaving state 'unknown' until the login below.
      await auth.bootstrap();
      await auth.login('admin', 'password123', 'client');
      expect(auth.state()).toBe('authenticated');

      const disconnectSpy = spyOn(api, 'disconnect').and.callThrough();
      const notifications = (api as unknown as { _notifications: Subject<{ method: string; params: unknown }> })
        ._notifications;
      notifications.next({ method: 'DeviceSessionRevokedEvent', params: {} });

      expect(auth.state()).toBe('loggedOut');
      expect(disconnectSpy).toHaveBeenCalled();
    });

    it('only registers the DeviceSessionRevokedEvent subscription once across repeated bootstraps', async () => {
      const { auth, api } = configure();
      const onNotificationSpy = spyOn(api, 'onNotification').and.callThrough();
      mockFetch({
        '/api/auth/status': () => jsonResponse({ setupComplete: true, authenticated: false }),
        '/api/auth/refresh': () => jsonResponse({ title: 'No refresh token.' }, 401),
      });

      await auth.bootstrap();
      await auth.bootstrap();

      const deviceSessionRevokedSubscriptions = onNotificationSpy.calls.all()
        .filter(call => call.args[0] === 'DeviceSessionRevokedEvent');
      expect(deviceSessionRevokedSubscriptions.length).toBe(1);
    });
  });

  describe('resume() (issue #257)', () => {
    function expireAccessToken(auth: AuthService): void {
      (auth as unknown as { accessTokenExpiresAt: number }).accessTokenExpiresAt = Date.now() - 1;
    }

    it('does not refresh when the access token is still healthy', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      let refreshCalls = 0;
      mockFetch({
        '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
        '/api/auth/refresh': () => {
          refreshCalls++;
          return jsonResponse(TOKEN_RESPONSE);
        },
      });
      await auth.login('admin', 'password123', 'client');

      await auth.resume();

      expect(refreshCalls).toBe(0);
    });

    it('refreshes exactly once when the access token is at or past its expiry leeway', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      let refreshCalls = 0;
      mockFetch({
        '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
        '/api/auth/refresh': () => {
          refreshCalls++;
          return jsonResponse(TOKEN_RESPONSE);
        },
      });
      await auth.login('admin', 'password123', 'client');
      expireAccessToken(auth);

      await auth.resume();

      expect(refreshCalls).toBe(1);
      expect(auth.state()).toBe('authenticated');
    });

    it('shares a single refresh between two concurrent resume() calls', async () => {
      jasmine.clock().install();
      try {
        const { auth, api } = configure();
        await api.resolveBaseUrl();
        let refreshCalls = 0;
        mockFetch({
          '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
          '/api/auth/refresh': () => {
            refreshCalls++;
            return jsonResponse(TOKEN_RESPONSE);
          },
        });
        await auth.login('admin', 'password123', 'client');
        expireAccessToken(auth);

        const first = auth.resume();
        const second = auth.resume();
        await flushMicrotasks();
        jasmine.clock().tick(1000);
        await flushMicrotasks();
        await Promise.all([first, second]);

        expect(refreshCalls).toBe(1);
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('drops to loggedOut on a failed resume refresh, with no retry loop', async () => {
      jasmine.clock().install();
      try {
        const { auth, api } = configure();
        await api.resolveBaseUrl();
        let refreshCalls = 0;
        mockFetch({
          '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
          '/api/auth/refresh': () => {
            refreshCalls++;
            return jsonResponse({ title: 'Invalid refresh token.' }, 401);
          },
        });
        await auth.login('admin', 'password123', 'client');
        expireAccessToken(auth);

        await auth.resume();
        expect(auth.state()).toBe('loggedOut');
        expect(refreshCalls).toBe(1);

        jasmine.clock().tick(120_000);
        await flushMicrotasks();

        expect(refreshCalls).toBe(1);
        expect(auth.state()).toBe('loggedOut');
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('does not attempt a request when the session is implicitly trusted', async () => {
      const { auth } = configure();
      const fetchSpy = mockFetch({
        '/api/auth/status': () =>
          jsonResponse({ setupComplete: true, authenticated: true, trusted: true, scope: 'admin', username: 'desktop' }),
      });
      await auth.bootstrap();
      expect(auth.state()).toBe('authenticated');
      const callsBeforeResume = fetchSpy.calls.count();

      await auth.resume();

      expect(fetchSpy.calls.count()).toBe(callsBeforeResume);
    });

    it('never turns a loggedOut session into authenticated', async () => {
      const { auth } = configure();
      mockFetch({
        '/api/auth/status': () => jsonResponse({ setupComplete: true, authenticated: false }),
        '/api/auth/refresh': () => jsonResponse({ title: 'No refresh token.' }, 401),
      });
      await auth.bootstrap();
      expect(auth.state()).toBe('loggedOut');

      await auth.resume();

      expect(auth.state()).toBe('loggedOut');
    });

    it('serialises the refresh across tabs, on every path that can refresh', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({
        '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE),
        '/api/auth/refresh': () => jsonResponse(TOKEN_RESPONSE),
      });
      await auth.login('admin', 'password123', 'client');
      expireAccessToken(auth);
      lockRequests.length = 0;

      await auth.resume();
      await (auth as unknown as { handleUnauthorized: () => Promise<boolean> }).handleUnauthorized();

      expect(lockRequests.length).toBe(2);
    });
  });

  describe('startup profile id (issue #251)', () => {
    it('is populated from a login response', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({
        '/api/auth/login': () =>
          jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-1', startupProfileId: 'profile-1' } }),
      });

      await auth.login('admin', 'password123', 'client');

      expect(auth.startupProfileId()).toBe('profile-1');
    });

    it('is updated from a refresh response too, not only login', async () => {
      const { auth, api } = configure();
      mockFetch({
        '/api/auth/status': () => jsonResponse({ setupComplete: true, authenticated: false }),
        '/api/auth/refresh': () =>
          jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-2', startupProfileId: 'profile-2' } }),
      });

      await auth.bootstrap();

      expect(auth.startupProfileId()).toBe('profile-2');
    });

    it('is null when the response carries no device at all', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({ '/api/auth/login': () => jsonResponse(TOKEN_RESPONSE) });

      await auth.login('admin', 'password123', 'client');

      expect(auth.startupProfileId()).toBeNull();
    });

    it("is unconditionally overwritten with null when a later response's device carries no assignment", async () => {
      // Unlike currentDeviceId, applySession must not just leave a stale value in place here - a
      // cleared assignment has to actually reach null, not linger at the previous response's value.
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({
        '/api/auth/login': () =>
          jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-1', startupProfileId: 'profile-1' } }),
        '/api/auth/refresh': () => jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-1' } }),
      });
      await auth.login('admin', 'password123', 'client');
      expect(auth.startupProfileId()).toBe('profile-1');

      const hook = (auth as unknown as { handleUnauthorized: () => Promise<boolean> });
      await hook.handleUnauthorized();

      expect(auth.startupProfileId()).toBeNull();
    });

    it('is cleared on logout', async () => {
      const { auth, api } = configure();
      await api.resolveBaseUrl();
      mockFetch({
        '/api/auth/login': () =>
          jsonResponse({ ...TOKEN_RESPONSE, device: { deviceId: 'device-1', startupProfileId: 'profile-1' } }),
      });

      await auth.login('admin', 'password123', 'client');
      expect(auth.startupProfileId()).toBe('profile-1');

      await auth.logout();

      expect(auth.startupProfileId()).toBeNull();
    });
  });

  describe('network interruption (issue #872)', () => {
    // Short-lived so a spec can move the clock past its 60s leeway without waiting out 900s of it.
    const SHORT_LIVED_TOKEN_RESPONSE = { ...TOKEN_RESPONSE, expiresInSeconds: 120 };

    it('keeps an authenticated session when the network disappears', async () => {
      jasmine.clock().install();
      jasmine.clock().mockDate();
      try {
        const { auth, api } = configure();
        await api.resolveBaseUrl();
        let offline = false;
        mockFetch({ '/api/auth/login': () => jsonResponse(SHORT_LIVED_TOKEN_RESPONSE) }, () => offline);
        await auth.login('admin', 'password123', 'client');

        offline = true;
        // Past the token's 60s leeway, or resume() short-circuits on a token that still looks fresh
        // and this passes for every implementation regardless of how it classifies the failure.
        jasmine.clock().tick(61_000);
        await flushMicrotasks();

        await auth.resume();

        expect(auth.state()).toBe('authenticated');
        expect(auth.username()).toBe('admin');
        expect(auth.scope()).toBe('client');
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('signs out when the host refuses to refresh the session', async () => {
      jasmine.clock().install();
      jasmine.clock().mockDate();
      try {
        const { auth, api } = configure();
        await api.resolveBaseUrl();
        mockFetch({
          '/api/auth/login': () => jsonResponse(SHORT_LIVED_TOKEN_RESPONSE),
          '/api/auth/refresh': () => jsonResponse({ title: 'Invalid refresh token.' }, 401),
        });
        await auth.login('admin', 'password123', 'client');

        jasmine.clock().tick(61_000);
        await flushMicrotasks();

        await auth.resume();

        expect(auth.state()).toBe('loggedOut');
        expect(auth.username()).toBeNull();
        expect(auth.scope()).toBeNull();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('stops retrying once the service it belongs to is thrown away', async () => {
      jasmine.clock().install();
      jasmine.clock().mockDate();
      try {
        const { auth, api } = configure();
        await api.resolveBaseUrl();
        let offline = false;
        let refreshCalls = 0;
        mockFetch({
          '/api/auth/login': () => jsonResponse(SHORT_LIVED_TOKEN_RESPONSE),
          '/api/auth/refresh': () => {
            refreshCalls++;
            return jsonResponse(SHORT_LIVED_TOKEN_RESPONSE);
          },
        }, () => offline);
        await auth.login('admin', 'password123', 'client');

        offline = true;
        jasmine.clock().tick(61_000);
        await flushMicrotasks();
        await auth.resume();
        const callsBeforeDestroy = refreshCalls;

        TestBed.resetTestingModule();
        // The network is back, so a retry that outlived the reset would reach the host and count.
        offline = false;
        // Long enough for several rounds of the backoff, had any survived the reset.
        jasmine.clock().tick(600_000);
        await flushMicrotasks();

        expect(refreshCalls).toBe(callsBeforeDestroy);
      } finally {
        jasmine.clock().uninstall();
      }
    });

    describe('does not fall through to the login form when it starts up with no network', () => {
      it('when every request rejects outright', async () => {
        const { auth } = configure();
        spyOn(window, 'fetch').and.rejectWith(new TypeError('Failed to fetch'));

        await auth.bootstrap();

        expect(auth.state()).toBe('unknown');
      });

      // The case that fails without the fix: a host that can answer `/api/auth/status` but not
      // `/api/auth/refresh` used to land on `loggedOut` - a login form a still-good cookie should
      // never have had to see.
      it('when the status answers but the refresh does not', async () => {
        const { auth } = configure();
        spyOn(window, 'fetch').and.callFake((input: RequestInfo | URL) => {
          const url = String(input);
          if (url.endsWith('/api/auth/status')) {
            return Promise.resolve(
              jsonResponse({ setupComplete: true, authenticated: false, trusted: false }));
          }
          if (url.endsWith('/api/auth/refresh')) {
            return Promise.reject(new TypeError('Failed to fetch'));
          }
          return Promise.reject(new Error(`unrouted fetch: ${url}`));
        });

        await auth.bootstrap();

        expect(auth.state()).toBe('unknown');
      });
    });
  });
});
