import { ClientAppStrings, ClientAppStringsDefaults } from '@macro-deck/runtime';
import { Client } from './client';
import { startNetworkWatch } from './network-watch';
import { WidgetSessions } from './widget-sessions';

interface Call {
  method: string;
  path: string;
  body: Record<string, unknown> | undefined;
  bearer: string | undefined;
}

interface TokenAnswer {
  accessToken: string;
  expiresInSeconds: number;
  scope: 'admin' | 'client';
  username: string;
  device?: { deviceId: string; deviceSecret?: string; startupProfileId?: string };
}

const wireFolder = (id: string, parentId: string | null, isDefault = false) => ({
  id, name: id, parentId, order: 0, isExpanded: false, isDefault,
  // `columns` is the field on the wire; `folderFromWire` reads it into the domain's `cols`. Typed
  // nullable so a test can give a parent folder a width and exercise the inheritance chain.
  columns: null as number | null,
  cols: null, rows: null, background: '', spacing: null, borderRadius: null,
  viewId: 'macrodeck.widget-grid', viewConfiguration: null, widgets: [],
});

const token = (overrides: Partial<TokenAnswer> = {}): TokenAnswer => ({
  accessToken: 'access-1',
  expiresInSeconds: 3600,
  scope: 'client',
  username: 'owner',
  ...overrides,
});

class FakeHost {
  readonly calls: Call[] = [];

  keyRingLocked = false;
  setupComplete = true;
  trusted = false;
  refreshable: TokenAnswer | null = null;
  offline = false;
  refreshStatus = 200;
  loginAnswer: TokenAnswer | null = token();
  loginStatus = 200;
  enrollmentAnswer: TokenAnswer | null = null;
  profiles: {
    id: string; name: string; order: number;
    defaultColumns?: number; defaultRows?: number;
    defaultWidgetSpacing?: number; defaultWidgetBorderRadius?: number;
  }[] = [];
  profilesFail = false;
  foldersFailFor: string | null = null;
  private holds: { [profileId: string]: Promise<void> } = {};

  holdFolders(profileId: string): () => void {
    let release: () => void = () => undefined;
    this.holds[profileId] = new Promise<void>(resolve => { release = resolve; });
    return () => {
      delete this.holds[profileId];
      release();
    };
  }
  foldersByProfile: { [profileId: string]: ReturnType<typeof wireFolder>[] } = {};
  localizationFailCount = 0;
  localizationAnswer: { culture: string; translations: Record<string, string> } =
    { culture: 'en', translations: {} };

  install(): void {
    (globalThis as { fetch: unknown }).fetch = (url: string, init?: RequestInit) => {
      const answer = this.answer(url, init);
      // Checked after the call above records the attempt, so an offline spec can still assert on
      // what the client tried - a fetch that never happened proves nothing about giving up.
      if (this.offline) return Promise.reject(new TypeError('Failed to fetch'));
      return Promise.resolve(answer as unknown as Response);
    };
  }

  private allFolders(): ReturnType<typeof wireFolder>[] {
    let folders: ReturnType<typeof wireFolder>[] = [];
    for (const profileId in this.foldersByProfile) {
      if (!Object.prototype.hasOwnProperty.call(this.foldersByProfile, profileId)) continue;
      folders = folders.concat(this.foldersByProfile[profileId]);
    }
    return folders;
  }

  private answer(url: string, init?: RequestInit): unknown {
    const path = String(url).replace('http://host', '');
    const headers = (init && init.headers ? init.headers : {}) as Record<string, string>;
    const authorization = headers['Authorization'];
    this.calls.push({
      method: init && init.method ? init.method : 'GET',
      path,
      body: init && typeof init.body === 'string'
        ? JSON.parse(init.body) as Record<string, unknown>
        : undefined,
      bearer: authorization ? authorization.replace('Bearer ', '') : undefined,
    });

    if (path === '/api/key-ring/status') {
      return respond(200, {
        locked: this.keyRingLocked, lockReason: 'None', restartSupported: false,
      });
    }
    if (path === '/api/auth/status') {
      return respond(200, {
        setupComplete: this.setupComplete,
        authenticated: this.trusted,
        trusted: this.trusted,
        scope: this.trusted ? 'admin' : null,
        username: this.trusted ? 'desktop' : null,
      });
    }
    if (path === '/api/auth/login') {
      return this.loginAnswer === null || this.loginStatus !== 200
        ? respond(this.loginStatus === 200 ? 401 : this.loginStatus, { title: 'Invalid credentials.' })
        : respond(200, this.loginAnswer);
    }
    if (path === '/api/auth/refresh') {
      if (this.refreshStatus !== 200) return respond(this.refreshStatus, { title: 'Refresh failed.' });
      return this.refreshable === null
        ? respond(401, { title: 'No session.' })
        : respond(200, this.refreshable);
    }
    if (path === '/api/ui-websocket/tickets') return respond(200, { value: 'ticket-1' });
    if (path === '/api/auth/device-enrollment/redeem') {
      return this.enrollmentAnswer === null
        ? respond(401, { title: 'Spent.' })
        : respond(200, this.enrollmentAnswer);
    }
    if (path === '/api/actions/execute') {
      return this.executeStatus === 200
        ? respond(200, this.executeAnswer)
        : respond(this.executeStatus, { title: 'No.' });
    }
    if (path === '/api/auth/logout') return respond(204, undefined);
    if (path === '/api/localization') {
      if (this.localizationFailCount > 0) {
        this.localizationFailCount--;
        return respond(503, { title: 'Not ready.' });
      }
      return respond(200, this.localizationAnswer);
    }
    if (path === '/api/settings/appearance') {
      return this.appearanceFails
        ? respond(500, { title: 'Appearance is unavailable.' })
        : respond(200, this.appearance);
    }
    if (path === '/api/profiles') {
      return this.profilesFail
        ? respond(500, { title: 'Profiles are unavailable.' })
        : respond(200, { profiles: this.profiles });
    }
    if (path.indexOf('/api/folders') === 0) {
      const at = path.indexOf('?profileId=');
      const requested = at < 0 ? null : decodeURIComponent(path.slice(at + '?profileId='.length));
      if (requested !== null && requested === this.foldersFailFor) {
        return respond(500, { title: 'Folders are unavailable.' });
      }
      const folders = requested === null
        ? this.allFolders()
        : this.foldersByProfile[requested] || [];
      const held = requested === null ? undefined : this.holds[requested];
      return held === undefined
        ? respond(200, { folders })
        : held.then(() => respond(200, { folders }));
    }
    return respond(404, { title: 'Not found.' });
  }

  appearance: { themeMode: string; accentColor: string } = { themeMode: 'light', accentColor: '#ff8800' };
  appearanceFails = false;
  executeAnswer: Record<string, unknown> = { success: true, status: 'Succeeded' };
  executeStatus = 200;

  pathsFor(method: string, prefix: string): string[] {
    const matched: string[] = [];
    for (let index = 0; index < this.calls.length; index++) {
      const call = this.calls[index];
      if (call.method === method && call.path.indexOf(prefix) === 0) matched.push(call.path);
    }
    return matched;
  }

  bearerFor(prefix: string): string | undefined {
    for (let index = 0; index < this.calls.length; index++) {
      if (this.calls[index].path.indexOf(prefix) === 0) return this.calls[index].bearer;
    }
    return undefined;
  }

  bodyOf(path: string): Record<string, unknown> | undefined {
    for (let index = 0; index < this.calls.length; index++) {
      if (this.calls[index].path === path) return this.calls[index].body;
    }
    return undefined;
  }
}

function respond(status: number, body: unknown): unknown {
  const text = body === undefined ? '' : JSON.stringify(body);
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: '',
    text: () => Promise.resolve(text),
    json: () => Promise.resolve(body),
  };
}

const folderIds = (client: Client) => client.deck.folders.get().map(folder => folder.id);

describe('Client', () => {
  const originalFetch = (globalThis as { fetch?: unknown }).fetch;
  let host: FakeHost;

  beforeEach(() => {
    host = new FakeHost();
    host.install();
    localStorage.clear();
    jasmine.clock().install();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    (globalThis as { fetch?: unknown }).fetch = originalFetch;
  });

  const settle = async () => { for (let turn = 0; turn < 20; turn++) await Promise.resolve(); };
  const waitOutTheRetries = async () => {
    for (let attempt = 0; attempt < 5; attempt++) {
      jasmine.clock().tick(60_000);
      await settle();
    }
  };
  const build = (options?: ConstructorParameters<typeof Client>[2]) =>
    new Client(() => 'http://host', 'client-1', options);

  const twoProfiles = () => {
    host.profiles = [
      // Grids that differ in every dimension, and in none of them match the built-in fallback: a deck
      // that fell back to it must not be mistaken for one that followed the profile.
      { id: 'profile-b', name: 'Wall tablet', order: 1, defaultColumns: 8, defaultRows: 4, defaultWidgetSpacing: 20 },
      { id: 'profile-a', name: 'Desk', order: 0, defaultColumns: 6, defaultRows: 2, defaultWidgetSpacing: 10 },
    ];
    host.foldersByProfile = {
      'profile-a': [wireFolder('a-root', null, true), wireFolder('a-child', 'a-root')],
      'profile-b': [wireFolder('b-root', null, true), wireFolder('b-child', 'b-root')],
    };
  };

  describe('signing in', () => {
    it('asks for a token this client can run a deck with', async () => {
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');

      expect(host.bodyOf('/api/auth/login')).toEqual(jasmine.objectContaining({
        username: 'owner', password: 'secret', scope: 'client',
      }));
    });

    // Issue #839: the session always persists, so the client never asks the host about it.
    it('asks the host nothing about how long the session should last', async () => {
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');

      expect(host.bodyOf('/api/auth/login')).not.toEqual(
        jasmine.objectContaining({ stayLoggedIn: jasmine.anything() }));
    });

    it('reaches the deck once the host has answered', async () => {
      twoProfiles();
      const client = build();
      await client.probe();

      const result = await client.signIn('owner', 'secret');
      // The socket is the last step and no host answers one here, so the deck is reported the way a
      // live connection would report it.
      client.connection.state.set('connected');

      expect(result.ok).toBe(true);
      expect(client.signedInAs.get()).toBe('owner');
      expect(client.app.screen.get()).toBe('deck');
      expect(client.deck.location.get().folderId).not.toBeNull();
    });

    it('reports a refused sign-in in words the app can show, and stays signed out', async () => {
      host.loginAnswer = null;
      const client = build();
      await client.probe();

      const result = await client.signIn('owner', 'wrong');

      expect(result.ok).toBe(false);
      expect(result.message).toBe(ClientAppStringsDefaults[ClientAppStrings.Auth.SignInFailed]);
      expect(client.app.screen.get()).toBe('signedOut');
      expect(client.signedInAs.get()).toBeNull();
    });

    it('refuses a session whose scope is not the one this client requires', async () => {
      host.loginAnswer = token({ scope: 'client' });
      const client = build({ requiredScope: 'admin' });
      await client.probe();

      const result = await client.signIn('owner', 'secret');

      expect(result.ok).toBe(false);
      expect(result.message).toBe(ClientAppStringsDefaults[ClientAppStrings.Errors.Auth.NotAuthorized]);
      expect(client.app.screen.get()).toBe('signedOut');
    });

    it('presents the token it was given on the requests that follow', async () => {
      twoProfiles();
      host.loginAnswer = token({ accessToken: 'access-42' });
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');

      expect(host.bearerFor('/api/folders')).toBe('access-42');
    });
  });

  describe('the localization catalogue', () => {
    it('does not stall sign-in when it is unreachable', async () => {
      host.localizationFailCount = Infinity;
      twoProfiles();
      const client = build();
      await client.probe();

      const result = await client.signIn('owner', 'secret');
      client.connection.state.set('connected');

      expect(result.ok).toBe(true);
      expect(client.app.screen.get()).toBe('deck');
    });

    it('keeps asking until the catalogue arrives', async () => {
      host.localizationFailCount = 3;
      host.localizationAnswer = {
        culture: 'de-DE',
        translations: { [ClientAppStrings.Auth.SignIn]: 'Anmelden' },
      };
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      await settle();

      expect(client.localization.culture()).toBe('en');
      expect(client.translate(ClientAppStrings.Auth.SignIn))
        .toBe(ClientAppStringsDefaults[ClientAppStrings.Auth.SignIn]);

      await waitOutTheRetries();

      expect(client.localization.culture()).toBe('de-DE');
      expect(client.translate(ClientAppStrings.Auth.SignIn)).toBe('Anmelden');
      expect(host.pathsFor('GET', '/api/localization').length).toBe(4);
    });

    it('backs off rather than hammering', async () => {
      host.localizationFailCount = Infinity;
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      await settle();

      jasmine.clock().tick(100);
      await settle();

      expect(host.pathsFor('GET', '/api/localization').length).toBe(1);
    });

    it('stops once the catalogue arrived', async () => {
      host.localizationFailCount = 3;
      host.localizationAnswer = {
        culture: 'de-DE',
        translations: { [ClientAppStrings.Auth.SignIn]: 'Anmelden' },
      };
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      await settle();
      await waitOutTheRetries();
      const requestCount = host.pathsFor('GET', '/api/localization').length;

      jasmine.clock().tick(10 * 60 * 1000);
      await settle();

      expect(host.pathsFor('GET', '/api/localization').length).toBe(requestCount);
    });

    it('stops when the session ends', async () => {
      host.localizationFailCount = Infinity;
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      await settle();

      await client.signOut();
      const requestCount = host.pathsFor('GET', '/api/localization').length;

      jasmine.clock().tick(10 * 60 * 1000);
      await settle();

      expect(host.pathsFor('GET', '/api/localization').length).toBe(requestCount);
    });
  });

  describe('signing out', () => {
    const signedIn = async () => {
      twoProfiles();
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      client.connection.state.set('connected');
      return client;
    };

    it('tells the host, drops the connection and returns to the sign-in screen', async () => {
      const client = await signedIn();

      await client.signOut();

      expect(host.pathsFor('POST', '/api/auth/logout').length).toBe(1);
      expect(client.app.screen.get()).toBe('signedOut');
      expect(client.connection.state.get()).toBe('disconnected');
      expect(client.signedInAs.get()).toBeNull();
    });

    it('signs out even when the host never hears about it', async () => {
      const client = await signedIn();
      (globalThis as { fetch: unknown }).fetch = () => Promise.reject(new Error('offline'));

      await client.signOut();

      expect(client.app.screen.get()).toBe('signedOut');
    });

    it('stops presenting the credential it no longer holds', async () => {
      const client = await signedIn();
      await client.signOut();
      host.calls.length = 0;

      host.loginAnswer = null;
      await client.signIn('owner', 'wrong');

      expect(host.calls[0].bearer).toBeUndefined();
    });

    it('does not bring the old deck back on the next sign-in screen', async () => {
      const client = await signedIn();

      await client.signOut();

      // A deck that has painted normally outlives a dropped connection; a signed-out one must not.
      expect(client.app.conditions.get().deckRendered).toBe(false);
    });
  });

  describe('picking the session back up on a reload', () => {
    it('resumes the session the refresh cookie still stands for', async () => {
      twoProfiles();
      host.refreshable = token({ accessToken: 'resumed-1' });
      const client = build();

      await client.probe();
      client.connection.state.set('connected');

      expect(client.app.screen.get()).toBe('deck');
      expect(client.signedInAs.get()).toBe('owner');
      expect(host.pathsFor('GET', '/api/folders').length).toBe(1);
    });

    it('stays signed out when there is no session to resume', async () => {
      host.refreshable = null;
      const client = build();

      await client.probe();

      expect(client.app.screen.get()).toBe('signedOut');
      expect(host.pathsFor('GET', '/api/folders').length).toBe(0);
    });

    it('does not treat a merely cookie-authenticated host as a session it can work with', async () => {
      // The host reports an authenticated request, but this client holds no access token: the socket
      // ticket would go out without one. Only the trusted transport is signed in without tokens.
      host.refreshable = null;
      const client = build();

      await client.probe();

      expect(client.app.screen.get()).toBe('signedOut');
    });

    it('signs in without tokens over the transport the host trusts itself', async () => {
      twoProfiles();
      host.trusted = true;
      const client = build();

      await client.probe();

      expect(client.app.conditions.get().authenticated).toBe(true);
      expect(host.pathsFor('POST', '/api/auth/refresh').length).toBe(0);
    });

    it('spends a one-time enrollment credential handed to it in the URL', async () => {
      twoProfiles();
      host.enrollmentAnswer = token({ accessToken: 'enrolled-1' });
      let hash = '#enroll=abc123';
      const client = build({ readHash: () => hash, clearHash: () => { hash = ''; } });

      await client.probe();

      expect(host.bodyOf('/api/auth/device-enrollment/redeem')).toEqual(
        jasmine.objectContaining({ token: 'abc123' }));
      expect(client.app.conditions.get().authenticated).toBe(true);
      // Cleared, so a reload cannot replay a credential that is already spent.
      expect(hash).toBe('');
    });

    it('falls back to the ordinary path when the credential has already been spent', async () => {
      host.enrollmentAnswer = null;
      host.refreshable = null;
      const client = build({ readHash: () => '#enroll=abc123', clearHash: () => undefined });

      await client.probe();

      expect(client.app.screen.get()).toBe('signedOut');
    });
  });

  describe('staying connected through a network interruption', () => {
    const shortLived = () => token({ expiresInSeconds: 120 });

    class FakeSocket {
      static instances: FakeSocket[] = [];
      onopen: (() => void) | null = null;
      onerror: (() => void) | null = null;
      onmessage: ((event: MessageEvent) => void) | null = null;
      onclose: (() => void) | null = null;
      constructor(readonly url: string, readonly protocol: string) { FakeSocket.instances.push(this); }
      close(): void {}
      fail(): void { if (this.onerror) this.onerror(); }
    }

    const originalWebSocket = (globalThis as { WebSocket?: unknown }).WebSocket;

    beforeEach(() => {
      FakeSocket.instances = [];
      (globalThis as { WebSocket: unknown }).WebSocket = FakeSocket;
    });

    afterEach(() => {
      (globalThis as { WebSocket?: unknown }).WebSocket = originalWebSocket;
    });

    it('keeps the session and the deck when the network disappears mid-session', async () => {
      twoProfiles();
      host.loginAnswer = shortLived();
      // The refresh cookie stands the whole time - only the network goes away, not the session.
      host.refreshable = shortLived();
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      client.connection.state.set('connected');
      await settle();

      host.offline = true;
      client.connection.state.set('reconnecting');
      // The proactive refresh timer, armed for the short-lived token above, fires and really does
      // reject against the offline fake host.
      await waitOutTheRetries();

      const conditions = client.app.conditions.get();
      expect(conditions.authenticated).toBe(true);
      expect(client.signedInAs.get()).toBe('owner');
      expect(client.app.screen.get()).toBe('deck');
      expect(conditions.deckRendered).toBe(true);
      expect(conditions.connected).toBe(false);
    });

    it('reconnects to the same host by itself once the network comes back', async () => {
      twoProfiles();
      host.loginAnswer = shortLived();
      host.refreshable = shortLived();
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      // Frees the transport start()'s own connect() opened, so the reconnect below is free to
      // mint a ticket of its own rather than finding one already (indefinitely) in flight.
      FakeSocket.instances[FakeSocket.instances.length - 1].fail();
      await settle();
      client.connection.state.set('connected');
      await settle();

      host.offline = true;
      client.connection.state.set('reconnecting');
      await waitOutTheRetries();

      const stopWatch = startNetworkWatch({ retryNow: () => client.retryNow() });
      const before = host.calls.length;
      host.offline = false;
      window.dispatchEvent(new Event('online'));
      await settle();
      stopWatch();

      // A real socket never opens in this environment, so the successful handshake is simulated the
      // same way every other spec in this file simulates it.
      client.connection.state.set('connected');
      await settle();

      const laterCalls = host.calls.slice(before);
      expect(laterCalls.some(call => call.method === 'POST' && call.path === '/api/ui-websocket/tickets'))
        .withContext('a fresh ticket request after the network came back').toBeTrue();
      expect(client.app.screen.get()).toBe('deck');
      expect(client.app.conditions.get().connected).toBe(true);
      expect(host.pathsFor('POST', '/api/auth/login').length).toBe(1);
    });

    it('signs out when the host refuses the session, and stops asking', async () => {
      host.loginAnswer = shortLived();
      host.refreshable = null;
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      client.connection.state.set('connected');
      await settle();

      await waitOutTheRetries();

      expect(client.app.screen.get()).toBe('signedOut');
      expect(client.app.conditions.get().authenticated).toBe(false);
      expect(client.signedInAs.get()).toBeNull();
      expect(client.app.conditions.get().deckRendered).toBe(false);

      const refreshCount = host.pathsFor('POST', '/api/auth/refresh').length;
      jasmine.clock().tick(10 * 60 * 1000);
      await settle();

      expect(host.pathsFor('POST', '/api/auth/refresh').length).toBe(refreshCount);
    });

    it('stays signed out after an explicit sign-out whose request never reached the host', async () => {
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      client.connection.state.set('connected');
      await settle();

      // The cookie is still good on the host - this is load-bearing: only the logout request itself
      // never arrives.
      host.refreshable = token();
      host.offline = true;
      await client.signOut();

      expect(client.app.screen.get()).toBe('signedOut');
      expect(client.app.conditions.get().authenticated).toBe(false);
      expect(client.signedInAs.get()).toBeNull();

      const refreshesAtSignOut = host.pathsFor('POST', '/api/auth/refresh').length;
      const stopWatch = startNetworkWatch({ retryNow: () => client.retryNow() });
      host.offline = false;
      window.dispatchEvent(new Event('online'));
      document.dispatchEvent(new Event('visibilitychange'));
      await settle();
      stopWatch();

      expect(client.app.screen.get()).toBe('signedOut');
      expect(client.app.conditions.get().authenticated).toBe(false);
      expect(client.signedInAs.get()).toBeNull();
      expect(host.pathsFor('POST', '/api/auth/refresh').length).toBe(refreshesAtSignOut);
    });

    it('does not present a sign-in form when it starts up with no network', async () => {
      host.offline = true;
      const client = build();
      const stopWatch = client.watchForRecovery(1000);
      await client.probe();
      await settle();

      expect(client.app.screen.get()).toBe('starting');

      jasmine.clock().tick(1000);
      await settle();

      expect(host.pathsFor('GET', '/api/key-ring/status').length).toBeGreaterThanOrEqual(2);

      host.refreshable = token({ username: 'owner' });
      host.offline = false;
      jasmine.clock().tick(1000);
      await settle();
      client.connection.state.set('connected');
      await settle();

      expect(client.app.screen.get()).toBe('deck');
      expect(client.signedInAs.get()).toBe('owner');
      expect(host.pathsFor('POST', '/api/auth/login').length).toBe(0);
      stopWatch();
    });

    it('treats an unreachable host differently from a refused session', async () => {
      const unreachableCases: Array<{ name: string; setup: () => void }> = [
        { name: 'a rejected fetch', setup: () => { host.offline = true; } },
        { name: '408', setup: () => { host.refreshStatus = 408; } },
        { name: '429', setup: () => { host.refreshStatus = 429; } },
        { name: '500', setup: () => { host.refreshStatus = 500; } },
        { name: '503', setup: () => { host.refreshStatus = 503; } },
      ];

      for (const testCase of unreachableCases) {
        host = new FakeHost();
        host.install();
        host.loginAnswer = shortLived();
        host.refreshable = shortLived();
        const client = build();
        await client.probe();
        await client.signIn('owner', 'secret');
        client.connection.state.set('connected');
        await settle();

        testCase.setup();
        client.connection.state.set('reconnecting');
        await waitOutTheRetries();

        expect(client.app.conditions.get().authenticated)
          .withContext(testCase.name).toBe(true);
        expect(client.app.screen.get())
          .withContext(testCase.name).toBe('deck');
      }

      const refusedCases = [401, 403];
      for (const status of refusedCases) {
        host = new FakeHost();
        host.install();
        host.loginAnswer = shortLived();
        host.refreshable = shortLived();
        const client = build();
        await client.probe();
        await client.signIn('owner', 'secret');
        client.connection.state.set('connected');
        await settle();

        host.refreshStatus = status;
        client.connection.state.set('reconnecting');
        await waitOutTheRetries();

        expect(client.app.screen.get()).withContext(String(status)).toBe('signedOut');
        expect(client.app.conditions.get().authenticated).withContext(String(status)).toBe(false);
      }

      // The captive-portal case: a 2xx that carries no token is not the host refusing the session.
      host = new FakeHost();
      host.install();
      host.loginAnswer = shortLived();
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      client.connection.state.set('connected');
      await settle();

      (host as unknown as { refreshable: unknown }).refreshable = {};
      client.connection.state.set('reconnecting');
      await waitOutTheRetries();

      expect(client.app.conditions.get().authenticated).toBe(true);
      expect(client.app.screen.get()).toBe('deck');
    });
  });

  describe('the profile the deck comes from', () => {
    it('loads the folders of the profile this device is assigned, not the first one it finds',
      async () => {
        twoProfiles();
        // profile-b is neither first in order nor the one an unscoped read would answer first with.
        host.loginAnswer = token({ device: { deviceId: 'device-1', startupProfileId: 'profile-b' } });
        const client = build();
        await client.probe();

        await client.signIn('owner', 'secret');

        expect(folderIds(client)).toEqual(['b-root', 'b-child']);
        expect(client.deck.location.get().folderId).toBe('b-root');
      });

    it('scopes the folder read rather than filtering an answer about every profile', async () => {
      twoProfiles();
      host.loginAnswer = token({ device: { deviceId: 'device-1', startupProfileId: 'profile-b' } });
      const client = build();
      await client.probe();

      await client.signIn('owner', 'secret');

      expect(host.pathsFor('GET', '/api/folders')).toEqual(['/api/folders?profileId=profile-b']);
    });

    it('starts on the first profile in order when the device is assigned none', async () => {
      twoProfiles();
      host.loginAnswer = token({ device: { deviceId: 'device-1' } });
      const client = build();
      await client.probe();

      await client.signIn('owner', 'secret');

      expect(folderIds(client)).toEqual(['a-root', 'a-child']);
    });

    it('starts on the first profile in order when the assignment names one it cannot see', async () => {
      twoProfiles();
      host.loginAnswer = token({ device: { deviceId: 'device-1', startupProfileId: 'gone' } });
      const client = build();
      await client.probe();

      await client.signIn('owner', 'secret');

      expect(folderIds(client)).toEqual(['a-root', 'a-child']);
    });

    it('re-reads the same profile when the connection comes back', async () => {
      twoProfiles();
      host.loginAnswer = token({ device: { deviceId: 'device-1', startupProfileId: 'profile-b' } });
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      client.connection.state.set('connected');

      client.connection.state.set('reconnecting');
      client.connection.state.set('connected');
      await settle();

      expect(host.pathsFor('GET', '/api/folders'))
        .toEqual(['/api/folders?profileId=profile-b', '/api/folders?profileId=profile-b']);
    });

    it('still shows a deck when the host cannot say what its profiles are', async () => {
      twoProfiles();
      host.profilesFail = true;
      const client = build();
      await client.probe();

      await client.signIn('owner', 'secret');

      // One deck out of several beats no deck at all, so the read goes out unscoped.
      expect(host.pathsFor('GET', '/api/folders')).toEqual(['/api/folders']);
      expect(client.deck.folders.get().length).toBeGreaterThan(0);
    });

    it('draws a subfolder at the width its parent folder states', () => {
      // Folder.cols promises "null = inherit from the parent folder, then the profile default".
      // The deck went straight to the profile, so a subfolder of a six-column folder came out at
      // the profile's five while the editor beside it drew six.
      host.profiles = [{ id: 'profile-a', name: 'Desk', order: 0, defaultColumns: 5, defaultRows: 3 }];
      host.foldersByProfile = {
        'profile-a': [
          { ...wireFolder('a-root', null, true), columns: 6 },
          wireFolder('a-child', 'a-root'),
        ],
      };
      const client = build();

      return client.probe()
        .then(() => client.signIn('owner', 'secret'))
        .then(() => settle())
        .then(() => {
          const folders = client.deck.folders.get();
          const child = folders.filter(folder => folder.id === 'a-child')[0];

          expect(child.cols).toBeNull();
          expect(client.gridFor(child).cols).toBe(6);
        });
    });

    it('re-resolves the deck when the profile it inherits from changes', () => {
      // Most folders state no grid of their own, so the shape comes from the profile - which this
      // client reads once, while resolving the profile it starts on. Changing the global column
      // count left every inheriting folder drawing the old shape until a reload, while the folders
      // that state their own were right: which is what makes it look like the setting did nothing.
      host.profiles = [{ id: 'profile-a', name: 'Desk', order: 0, defaultColumns: 5, defaultRows: 3 }];
      host.foldersByProfile = { 'profile-a': [wireFolder('a-root', null, true)] };
      const client = build();
      const inheriting = { cols: null, rows: null, spacing: null, borderRadius: null };

      return client.probe()
        .then(() => client.signIn('owner', 'secret'))
        .then(() => settle())
        .then(() => {
          expect(client.gridFor(inheriting).cols).toBe(5);

          (client as never as { onNotification(type: string, body: unknown): void })
            .onNotification('ProfileUpdatedEvent', {
              profile: { id: 'profile-a', name: 'Desk', order: 0, defaultColumns: 8, defaultRows: 3 },
            });

          expect(client.gridFor(inheriting).cols).toBe(8);
        });
    });
  });

  describe('a profile the host sends the deck to', () => {
    const inheriting = { cols: null, rows: null, spacing: null, borderRadius: null };

    const navigate = (client: Client, payload: Record<string, unknown>) =>
      (client as unknown as { onNotification(type: string, payload: unknown): void })
        .onNotification('FolderNavigationEvent', payload);

    const onProfileA = async (): Promise<Client> => {
      twoProfiles();
      host.loginAnswer = token({ device: { deviceId: 'device-1', startupProfileId: 'profile-a' } });
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      await settle();
      return client;
    };

    it('draws the deck at the grid of the profile it was sent to', async () => {
      const client = await onProfileA();
      expect(client.gridFor(inheriting).cols).toBe(6);

      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      await settle();

      expect(client.gridFor(inheriting).cols).toBe(8);
      expect(client.gridFor(inheriting).rows).toBe(4);
      expect(client.gridFor(inheriting).spacing).toBe(20);
    });

    it('shows that profile\'s folders instead of the ones it was on', async () => {
      const client = await onProfileA();

      navigate(client, { command: 'changeTo', folderId: 'b-child', profileId: 'profile-b' });
      await settle();

      expect(folderIds(client)).toEqual(['b-root', 'b-child']);
      expect(client.deck.location.get().folderId).toBe('b-child');
      expect(client.deck.currentFolder).toBeDefined();
      expect(host.pathsFor('GET', '/api/folders')).toEqual([
        '/api/folders?profileId=profile-a', '/api/folders?profileId=profile-b',
      ]);
    });

    it('tells the host it is on the folder it was sent to, and on nothing on the way', async () => {
      const client = await onProfileA();
      // The socket machinery is the boundary here, not the subject.
      spyOn(client.connection, 'connect');
      const spy = spyOn(client.connection, 'request').and.resolveTo({ success: true } as never);
      client.connection.state.set('connected');
      await settle();
      spy.calls.reset();

      navigate(client,
        { command: 'changeTo', folderId: 'b-child', profileId: 'profile-b', navigationToken: 'tok-9' });
      await settle();

      const sent = spy.calls.allArgs()
        .filter(args => args[0] === 'ReportFolderChanged')
        .map(args => args[1] as Record<string, unknown>);
      expect(sent.length).toBe(1);
      expect(sent[0]['folderId']).toBe('b-child');
      expect(sent[0]['navigationToken']).toBe('tok-9');
    });

    it('ends on the newer of two switches even when the older one answers last', async () => {
      const client = await onProfileA();
      host.profiles.push(
        { id: 'profile-c', name: 'Kitchen', order: 2, defaultColumns: 3, defaultRows: 7 });
      host.foldersByProfile['profile-c'] = [wireFolder('c-root', null, true)];
      const releaseB = host.holdFolders('profile-b');

      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      navigate(client, { command: 'changeTo', folderId: 'c-root', profileId: 'profile-c' });
      await settle();
      releaseB();
      await settle();

      expect(folderIds(client)).toEqual(['c-root']);
      expect(client.deck.location.get().folderId).toBe('c-root');
      expect(client.gridFor(inheriting).cols).toBe(3);
    });

    // The bug this fixes is a deck drawn at one profile's size showing another's, so no interleaving
    // may end in a half-applied switch - a reconnect's reload lands in the middle of one here.
    it('never ends up with one profile\'s folders under another\'s grid', async () => {
      const client = await onProfileA();
      spyOn(client.connection, 'connect');
      client.connection.state.set('connected');
      await settle();
      const releaseB = host.holdFolders('profile-b');

      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      client.connection.state.set('reconnecting');
      client.connection.state.set('connected');
      await settle();
      // A move of its own, while both reads are still out: whatever lands afterwards belongs to a
      // deck nobody is waiting for any more.
      navigate(client, { command: 'parent' });
      releaseB();
      await settle();

      // Whichever of them landed, the deck is whole: the folders it holds and the grid it is drawn
      // at come from the same profile, and it stands on a folder it actually has.
      const onB = folderIds(client).indexOf('b-root') >= 0;
      expect(folderIds(client)).toEqual(onB ? ['b-root', 'b-child'] : ['a-root', 'a-child']);
      expect(client.gridFor(inheriting).cols).toBe(onB ? 8 : 6);
      expect(client.deck.currentFolder).toBeDefined();
    });

    it('does not go back to a profile the host has already navigated away from', async () => {
      const client = await onProfileA();
      spyOn(client.connection, 'connect');
      client.connection.state.set('connected');
      await settle();
      const releaseB = host.holdFolders('profile-b');

      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      navigate(client, { command: 'changeTo', folderId: 'a-child', profileId: 'profile-a' });
      releaseB();
      client.connection.state.set('reconnecting');
      client.connection.state.set('connected');
      await settle();

      expect(folderIds(client)).toEqual(['a-root', 'a-child']);
      expect(client.deck.location.get().folderId).toBe('a-child');
      expect(client.gridFor(inheriting).cols).toBe(6);
    });

    it('reads nothing for a move inside the profile it is already on', async () => {
      const client = await onProfileA();
      const reads = host.pathsFor('GET', '/api/folders').length;

      navigate(client, { command: 'changeTo', folderId: 'a-child', profileId: 'profile-a' });
      await settle();

      expect(client.deck.location.get().folderId).toBe('a-child');
      expect(host.pathsFor('GET', '/api/folders').length).toBe(reads);
    });

    it('keeps the deck it is showing when the profile\'s folders cannot be read', async () => {
      const client = await onProfileA();
      host.foldersFailFor = 'profile-b';

      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      await settle();

      expect(folderIds(client)).toEqual(['a-root', 'a-child']);
      expect(client.deck.location.get().folderId).toBe('a-root');
      expect(client.gridFor(inheriting).cols).toBe(6);
    });

    it('keeps the deck it is showing when it is sent to a profile that is not there', async () => {
      const client = await onProfileA();

      navigate(client, { command: 'changeTo', folderId: 'gone-root', profileId: 'gone' });
      await settle();

      expect(folderIds(client)).toEqual(['a-root', 'a-child']);
      expect(client.deck.location.get().folderId).toBe('a-root');
      expect(client.deck.currentFolder?.id).toBe('a-root');
      expect(client.gridFor(inheriting).cols).toBe(6);
    });

    it('reads the profile it switched to when the connection comes back', async () => {
      const client = await onProfileA();
      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      await settle();
      client.connection.state.set('connected');

      client.connection.state.set('reconnecting');
      client.connection.state.set('connected');
      await settle();

      expect(host.pathsFor('GET', '/api/folders').pop()).toBe('/api/folders?profileId=profile-b');
      expect(folderIds(client)).toEqual(['b-root', 'b-child']);
      expect(client.gridFor(inheriting).cols).toBe(8);
    });

    it('ends where the newer of two navigations sent it', async () => {
      const client = await onProfileA();

      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      navigate(client, { command: 'changeTo', folderId: 'a-child', profileId: 'profile-a' });
      await settle();

      expect(client.deck.location.get().folderId).toBe('a-child');
      expect(folderIds(client)).toEqual(['a-root', 'a-child']);
      expect(client.gridFor(inheriting).cols).toBe(6);
    });

    it('follows the profile it switched to when that profile is edited', async () => {
      const client = await onProfileA();
      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      await settle();

      (client as never as { onNotification(type: string, body: unknown): void })
        .onNotification('ProfileUpdatedEvent', {
          profile: { id: 'profile-b', name: 'Wall tablet', order: 1, defaultColumns: 10, defaultRows: 4 },
        });

      expect(client.gridFor(inheriting).cols).toBe(10);
    });

    it('ignores an edit to the profile it left', async () => {
      const client = await onProfileA();
      navigate(client, { command: 'changeTo', folderId: 'b-root', profileId: 'profile-b' });
      await settle();

      (client as never as { onNotification(type: string, body: unknown): void })
        .onNotification('ProfileUpdatedEvent', {
          profile: { id: 'profile-a', name: 'Desk', order: 0, defaultColumns: 2, defaultRows: 2 },
        });

      expect(client.gridFor(inheriting).cols).toBe(8);
    });

    it('redraws a profile whose grid was edited while this client was away', async () => {
      const client = await onProfileA();
      client.connection.state.set('connected');
      host.profiles = [
        { id: 'profile-b', name: 'Wall tablet', order: 1, defaultColumns: 8, defaultRows: 4 },
        { id: 'profile-a', name: 'Desk', order: 0, defaultColumns: 9, defaultRows: 3 },
      ];

      client.connection.state.set('reconnecting');
      client.connection.state.set('connected');
      await settle();

      expect(client.gridFor(inheriting).cols).toBe(9);
    });
  });

  describe('the colours the host is configured for', () => {
    const sink = () => {
      const applied: Array<{ mode: string | undefined; accent: string | undefined }> = [];
      return {
        applied,
        applyFromHost: (mode: string | undefined, accent: string | undefined) =>
          void applied.push({ mode, accent }),
      };
    };

    it('applies them on the way into the deck', async () => {
      const appearance = sink();
      const client = build({ appearance } as never);
      host.trusted = true;

      await client.probe();
      await settle();

      expect(appearance.applied).toContain({ mode: 'light', accent: '#ff8800' });
    });

    it('applies a change the host pushes, without being reloaded', async () => {
      const appearance = sink();
      const client = build({ appearance } as never);
      host.trusted = true;
      await client.probe();
      await settle();
      appearance.applied.length = 0;

      (client as never as { onNotification(type: string, body: unknown): void })
        .onNotification('AppearanceChangedEvent', { themeMode: 'dark', accentColor: '#00c853' });

      expect(appearance.applied).toEqual([{ mode: 'dark', accent: '#00c853' }]);
    });

    it('keeps what it had when the host will not say', async () => {
      const appearance = sink();
      twoProfiles();
      host.appearanceFails = true;
      const client = build({ appearance } as never);
      host.trusted = true;

      await client.probe();
      await settle();

      expect(appearance.applied).toEqual([]);
      // The deck still loads: the colours are not what the client is there for.
      expect(folderIds(client).length).toBeGreaterThan(0);
    });
  });

  describe('the device this client signs in as', () => {
    it('presents the identity it was given last time, so its assignment sticks', async () => {
      twoProfiles();
      host.loginAnswer = token({
        device: { deviceId: 'device-7', deviceSecret: 'secret-7', startupProfileId: 'profile-b' },
      });
      const first = build();
      await first.probe();
      await first.signIn('owner', 'secret');
      host.calls.length = 0;

      const second = build();
      await second.probe();
      await second.signIn('owner', 'secret');

      expect(host.bodyOf('/api/auth/login')).toEqual(jasmine.objectContaining({
        device: jasmine.objectContaining({ deviceId: 'device-7', deviceSecret: 'secret-7' }),
      }));
    });

    it('signs in as a new device when it has never been given one', async () => {
      twoProfiles();
      const client = build();
      await client.probe();

      await client.signIn('owner', 'secret');

      const device = (host.bodyOf('/api/auth/login') as { device: Record<string, unknown> }).device;
      expect(device['deviceId']).toBeUndefined();
      expect(device['clientType']).toBe('web-client');
    });
  });

  describe('a session that ends without being asked to', () => {
    const revoke = (client: Client) =>
      (client as unknown as { onNotification(type: string, payload: unknown): void })
        .onNotification('DeviceSessionRevokedEvent', {});

    it('returns to the sign-in screen when the host revokes this device', async () => {
      twoProfiles();
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');
      client.connection.state.set('connected');

      revoke(client);

      expect(client.app.screen.get()).toBe('signedOut');
      expect(client.connection.state.get()).toBe('disconnected');
    });

    it('forgets what widgets drew, so the next sign-in cannot see them', async () => {
      twoProfiles();
      const client = build();
      await client.probe();
      await client.signIn('owner', 'secret');

      const opener = {
        request: <T>(type: string): Promise<T> => Promise.resolve(
          (type === 'OpenWidgetUiSession'
            ? { accepted: true, sessionId: 's1' }
            : { accepted: true }) as T),
      };
      const sessions = new WidgetSessions(opener, client.sessions);
      (client as unknown as { widgetSessions: WidgetSessions }).widgetSessions = sessions;
      sessions.sync([
        { id: 'w1', folderId: 'f', x: 0, y: 0, w: 1, h: 1, type: 'action-button', data: {} } as never,
      ]);
      for (let tick = 0; tick < 5; tick++) await Promise.resolve();
      client.sessions.treeUpdated('s1', 1, { id: 1, type: 'ui.stack' } as never);
      expect(sessions.treeFor('w1')).toBeDefined();

      revoke(client);

      expect(sessions.treeFor('w1')).toBeUndefined();
    });
  });

  describe('recovery watch', () => {
    beforeEach(() => {
      host.setupComplete = false;
      host.refreshable = null;
    });

    it('asks again while the first-time setup is unfinished', async () => {
      const client = build();
      client.watchForRecovery(1000);
      await client.probe();
      await settle();

      expect(client.app.screen.get()).toBe('setupRequired');
      const before = host.calls.length;

      jasmine.clock().tick(1000);
      await settle();

      expect(host.calls.length).toBeGreaterThan(before);
      client.watchForRecovery(1000)();
    });

    it('moves on by itself once the setup is finished', async () => {
      const client = build();
      const stop = client.watchForRecovery(1000);
      await client.probe();
      await settle();
      expect(client.app.screen.get()).toBe('setupRequired');

      host.setupComplete = true;
      jasmine.clock().tick(1000);
      await settle();

      expect(client.app.screen.get()).toBe('signedOut');
      stop();
    });

    it('stops asking once it is no longer stuck', async () => {
      const client = build();
      const stop = client.watchForRecovery(1000);
      await client.probe();
      await settle();

      host.setupComplete = true;
      jasmine.clock().tick(1000);
      await settle();
      const settled = host.calls.length;

      jasmine.clock().tick(10_000);
      await settle();

      expect(host.calls.length).toBe(settled);
      stop();
    });

    it('watches a locked key ring the same way', async () => {
      host.keyRingLocked = true;
      host.setupComplete = true;
      const client = build();
      const stop = client.watchForRecovery(1000);
      await client.probe();
      await settle();
      expect(client.app.screen.get()).toBe('keyRingLocked');

      host.keyRingLocked = false;
      jasmine.clock().tick(1000);
      await settle();

      expect(client.app.screen.get()).toBe('signedOut');
      stop();
    });

    it('stops asking when the watch is torn down', async () => {
      const client = build();
      const stop = client.watchForRecovery(1000);
      await client.probe();
      await settle();

      stop();
      const before = host.calls.length;
      jasmine.clock().tick(10_000);
      await settle();

      expect(host.calls.length).toBe(before);
    });

    it('does not start a second probe on top of one already running', async () => {
      host.setupComplete = true;
      const client = build();
      const first = client.probe();
      const second = client.probe();
      await Promise.all([first, second]);
      await settle();

      // The key ring, the auth status and the one attempt to resume a session - one of each.
      expect(host.pathsFor('GET', '/api/key-ring/status').length).toBe(1);
      expect(host.pathsFor('GET', '/api/auth/status').length).toBe(1);
      expect(host.pathsFor('POST', '/api/auth/refresh').length).toBe(1);
    });
  });

  describe('deck navigation the host asks for', () => {
    const folder = (id: string, parentId: string | null, isDefault = false) =>
      wireFolder(id, parentId, isDefault) as never;

    const navigate = (client: Client, payload: Record<string, unknown>) => {
      // The notification path the connection would drive.
      (client as unknown as { onNotification(type: string, payload: unknown): void })
        .onNotification('FolderNavigationEvent', payload);
    };

    it('opens the folder a change-to names', () => {
      const client = build();
      client.deck.load([folder('root', null, true), folder('child', 'root')]);

      navigate(client, { command: 'changeTo', folderId: 'child' });

      expect(client.deck.location.get().folderId).toBe('child');
    });

    it('walks back the way the user came', () => {
      const client = build();
      client.deck.load([folder('root', null, true), folder('child', 'root')]);
      client.deck.openFolder('child');

      navigate(client, { command: 'back' });

      expect(client.deck.location.get().folderId).toBe('root');
    });

    it('goes up the folder tree for a parent command', () => {
      const client = build();
      client.deck.load([folder('root', null, true), folder('child', 'root')]);
      client.deck.openFolder('child');

      navigate(client, { command: 'parent' });

      expect(client.deck.location.get().folderId).toBe('root');
    });

    it('ignores a command it does not know rather than moving somewhere', () => {
      const client = build();
      client.deck.load([folder('root', null, true), folder('child', 'root')]);

      navigate(client, { command: 'teleport', folderId: 'child' });

      expect(client.deck.location.get().folderId).toBe('root');
    });
  });

  describe('reporting the folder the deck shows', () => {
    const folder = (id: string, parentId: string | null, isDefault = false) =>
      wireFolder(id, parentId, isDefault) as never;

    const navigate = (client: Client, payload: Record<string, unknown>) =>
      (client as unknown as { onNotification(type: string, payload: unknown): void })
        .onNotification('FolderNavigationEvent', payload);

    const reports = (spy: jasmine.Spy): Record<string, unknown>[] => spy.calls.allArgs()
      .filter(args => args[0] === 'ReportFolderChanged')
      .map(args => args[1] as Record<string, unknown>);

    const connect = async (client: Client): Promise<jasmine.Spy> => {
      // The socket machinery is the boundary here, not the subject: left real, its failed connect
      // attempt against the fake host flips the state back to 'reconnecting' underneath the test.
      spyOn(client.connection, 'connect');
      await client.probe();
      await client.signIn('owner', 'secret');
      const spy = spyOn(client.connection, 'request').and.resolveTo({ success: true } as never);
      client.connection.state.set('connected');
      await settle();
      return spy;
    };

    it('tells the host what it shows as soon as the connection is up', async () => {
      twoProfiles();
      const client = build();
      const spy = await connect(client);

      const sent = reports(spy);
      expect(sent.length).toBe(1);
      expect(sent[0]['folderId']).toBe(client.deck.location.get().folderId);
      expect(sent[0]['isResync']).toBeTrue();
    });

    it('echoes a device-targeted navigation token only with the folder it addressed', async () => {
      twoProfiles();
      const client = build();
      const spy = await connect(client);
      client.deck.load([folder('nav-root', null, true), folder('nav-child', 'nav-root')]);
      spy.calls.reset();

      navigate(client, { command: 'changeTo', folderId: 'nav-child', navigationToken: 'tok-1' });
      navigate(client, { command: 'back' });

      const sent = reports(spy);
      expect(sent.length).toBe(2);
      expect(sent[0]['folderId']).toBe('nav-child');
      expect(sent[0]['navigationToken']).toBe('tok-1');
      expect(sent[0]['isResync']).toBeFalse();
      // Walking back is the user's own move: no token, or the host would treat it as its own drive.
      expect(sent[1]['folderId']).toBe('nav-root');
      expect(sent[1]['navigationToken']).toBeUndefined();
      await settle();
    });

    it('stays quiet while the connection is down and catches up on reconnect', async () => {
      twoProfiles();
      const client = build();
      const spy = await connect(client);
      client.deck.load([folder('nav-root', null, true), folder('nav-child', 'nav-root')]);
      client.connection.state.set('disconnected');
      spy.calls.reset();

      client.deck.openFolder('nav-child');
      expect(reports(spy).length).toBe(0);

      client.connection.state.set('connected');

      const sent = reports(spy);
      expect(sent.length).toBe(1);
      expect(sent[0]['folderId']).toBe('nav-child');
      expect(sent[0]['isResync']).toBeTrue();
      await settle();
    });
  });

  describe('a press the host refuses', () => {
    const deckWith = (client: Client, type: string, data: Record<string, unknown> = {}) => {
      client.deck.load([{
        ...wireFolder('root', null, true),
        widgets: [{ id: 'w1', folderId: 'root', x: 0, y: 0, w: 1, h: 1, type, data }],
      }] as never);
    };

    const reported = (client: Client): string[] => {
      const messages: string[] = [];
      client.executionFeedback.failures.subscribe(failure => {
        if (failure !== null) messages.push(failure.message);
      });
      return messages;
    };

    const push = (client: Client, payload: Record<string, unknown>) =>
      (client as unknown as { onNotification(type: string, payload: unknown): void })
        .onNotification('ActionExecutionStatusEvent', payload);

    const generic = (client: Client) => client.translate(ClientAppStrings.Errors.Folder.ActionRunFailed);

    it('says what the host said was wrong', async () => {
      const client = build();
      deckWith(client, 'clock');
      const messages = reported(client);
      host.executeAnswer = {
        success: false, status: 'Failed', executionId: 'e1', durationMs: 7, actions: [],
        error: { code: 'PLUGIN_ERROR', message: 'The serial port is busy' },
      };

      await client.executeTrigger('w1', 'onShortPress');

      expect(messages).toEqual(['The serial port is busy']);
    });

    it('still says something when the host explained nothing', async () => {
      const client = build();
      deckWith(client, 'clock');
      const messages = reported(client);
      host.executeAnswer = { success: false, status: 'Failed' };

      await client.executeTrigger('w1', 'onShortPress');

      expect(messages).toEqual([generic(client)]);
    });

    it('treats a failure that named no status as the failure it says it is', async () => {
      const client = build();
      deckWith(client, 'clock');
      const messages = reported(client);
      host.executeAnswer = { success: false };

      await client.executeTrigger('w1', 'onShortPress');

      expect(messages).toEqual([generic(client)]);
    });

    it('waits for the outcome of a run that is still going rather than calling it a failure', async () => {
      const client = build();
      deckWith(client, 'clock');
      const messages = reported(client);
      // What a real host answers for a run that outlived its response bound: accepted, not failed.
      host.executeAnswer = { success: true, status: 'Accepted', executionId: 'e1' };

      await client.executeTrigger('w1', 'onShortPress');
      expect(messages).withContext('an accepted run has not failed').toEqual([]);

      push(client, {
        executionId: 'e1', status: 'Failed', durationMs: 12, widgetId: 'w1',
        triggerType: 'onShortPress', actions: [],
        // The shape the host really sends for its own text: a reference into the catalogue, not a
        // string. A client that rendered the object itself would show '[object Object]'.
        error: { code: 'ACTION_TIMEOUT', message: { $localized: { scope: 'macrodeck.app', key: 'Errors.Actions.TookTooLong' } } },
      });

      expect(messages).toEqual([client.translate('macrodeck.app:Errors.Actions.TookTooLong')]);
    });

    it('tells the user every time, not only the first', async () => {
      const client = build();
      deckWith(client, 'clock');
      const messages = reported(client);
      host.executeAnswer = {
        success: false, status: 'Failed', executionId: 'e1', durationMs: 7, actions: [],
        error: { code: 'PLUGIN_ERROR', message: 'The serial port is busy' },
      };

      await client.executeTrigger('w1', 'onShortPress');
      await client.executeTrigger('w1', 'onShortPress');

      expect(messages).toEqual(['The serial port is busy', 'The serial port is busy']);
    });

    it('reports a press the host never received', async () => {
      const client = build();
      deckWith(client, 'clock');
      const messages = reported(client);
      host.executeStatus = 503;

      await client.executeTrigger('w1', 'onShortPress');

      expect(messages).toEqual([generic(client)]);
    });

    it('reports a widget whose press never goes near the REST route', () => {
      const client = build();
      deckWith(client, 'action-button');
      const messages = reported(client);

      // What the host pushes when it refuses a press on a locked machine - the only signal this
      // widget's press ever produces.
      push(client, {
        executionId: 'e9', status: 'Failed', durationMs: 0, widgetId: 'w1',
        triggerType: 'onShortPress', actions: [],
        error: { code: 'HOST_LOCKED', message: { $localized: { scope: 'macrodeck.app', key: 'Errors.Common.HostLocked' } } },
      });

      expect(messages).toEqual([client.translate('macrodeck.app:Errors.Common.HostLocked')]);
      expect(host.pathsFor('POST', '/api/actions/execute')).toEqual([]);
    });

    it('says it once for one tap, not once per trigger the tile fires', async () => {
      const client = build();
      // No flows at all, so nothing is filtered out before the request: every trigger of the tap
      // reaches the host, and every one of them is refused.
      deckWith(client, 'clock');
      const messages = reported(client);
      host.executeAnswer = {
        success: false, status: 'Failed',
        error: { code: 'HOST_LOCKED', message: { $localized: { scope: 'macrodeck.app', key: 'Errors.Common.HostLocked' } } },
      };

      // What one tap on a tile actually fires, in order.
      await client.executeTrigger('w1', 'onTouchStart');
      await client.executeTrigger('w1', 'onTouchEnd');
      await client.executeTrigger('w1', 'onShortPress');

      expect(messages).toEqual([client.translate('macrodeck.app:Errors.Common.HostLocked')]);
      expect(host.pathsFor('POST', '/api/actions/execute').length)
        .withContext('the host still runs every trigger').toBe(3);
    });

    it('says nothing about a press that worked', async () => {
      const client = build();
      deckWith(client, 'clock');
      const messages = reported(client);
      host.executeAnswer = { success: true, status: 'Succeeded', executionId: 'e2', durationMs: 5 };

      await client.executeTrigger('w1', 'onShortPress');
      push(client, {
        executionId: 'e3', status: 'Succeeded', durationMs: 5, widgetId: 'w1',
        triggerType: 'onShortPress', actions: [],
      });

      expect(messages).toEqual([]);
    });

    it('says nothing about a press the client answered itself', async () => {
      const client = build();
      client.deck.load([
        { ...wireFolder('root', null, true), widgets: [{
          id: 'w1', folderId: 'root', x: 0, y: 0, w: 1, h: 1, type: 'clock',
          data: { flows: [{ triggerType: 'onShortPress', children: [{
            type: 'action',
            blockType: 'app.macro-deck.deck.change-folder',
            parameters: [{ name: 'folderId', value: 'child' }],
          }] }] },
        }] },
        wireFolder('child', 'root'),
      ] as never);
      const messages = reported(client);

      await client.executeTrigger('w1', 'onShortPress');

      expect(client.deck.location.get().folderId).toBe('child');
      expect(messages).toEqual([]);
      expect(host.pathsFor('POST', '/api/actions/execute')).toEqual([]);
    });

    // A change-folder is only the client's to answer while the folder is one it holds. A target in
    // another profile is not, and opening it here left the deck on a folder with nothing in it.
    it('asks the host for a change-folder to a folder this deck does not hold', async () => {
      const client = build();
      client.deck.load([{ ...wireFolder('root', null, true), widgets: [{
        id: 'w1', folderId: 'root', x: 0, y: 0, w: 1, h: 1, type: 'clock',
        data: { flows: [{ triggerType: 'onShortPress', children: [{
          type: 'action',
          blockType: 'app.macro-deck.deck.change-folder',
          parameters: [{ name: 'folderId', value: 'b-child' }],
        }] }] },
      }] }] as never);

      await client.executeTrigger('w1', 'onShortPress');

      expect(host.pathsFor('POST', '/api/actions/execute').length).toBe(1);
      expect(client.deck.location.get().folderId).toBe('root');
    });

    it('says it in the language the client is in now, not the one it started in', async () => {
      const client = build();
      deckWith(client, 'clock');
      const messages = reported(client);
      host.executeAnswer = { success: false, status: 'Failed' };

      await client.executeTrigger('w1', 'onShortPress');

      client.localization.apply({
        culture: 'de',
        translations: { [ClientAppStrings.Errors.Folder.ActionRunFailed]: 'Die Aktion konnte nicht ausgefuehrt werden' },
      });
      await client.executeTrigger('w1', 'onShortPress');

      expect(messages[1]).toBe('Die Aktion konnte nicht ausgefuehrt werden');
      expect(messages[1]).not.toBe(messages[0]);
    });
  });
});
