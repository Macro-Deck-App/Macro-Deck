import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';
import { HOST_URL_RESOLVER } from './host-url';
import { TransportError } from '@macro-deck/runtime';

async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

class FakeWebSocket {
  static readonly OPEN = 1;
  static instances: FakeWebSocket[] = [];
  readyState = 0;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onmessage: ((event: MessageEvent) => void) | null = null;
  onclose: (() => void) | null = null;
  readonly sent: string[] = [];

  constructor(readonly url: string, readonly protocol: string) {
    FakeWebSocket.instances.push(this);
  }

  send(value: string): void { this.sent.push(value); }
  close(): void {
    if (this.readyState === 3) return;
    this.readyState = 3;
    this.onclose?.();
  }
  open(): void {
    this.readyState = FakeWebSocket.OPEN;
    this.onopen?.();
  }
  receive(value: object): void {
    this.onmessage?.({ data: JSON.stringify(value) } as MessageEvent);
  }
}

async function completeUiSocket(socket: FakeWebSocket): Promise<void> {
  socket.open();
  socket.receive({ protocolVersion: 1, kind: 'welcome' });
  await flushMicrotasks();
  const requests = socket.sent.map(value => JSON.parse(value) as { kind: string; id?: string })
    .filter(envelope => envelope.kind === 'request');
  const register = requests[requests.length - 1];
  expect(register?.id).toBeDefined();
  socket.receive({ protocolVersion: 1, kind: 'response', correlationId: register!.id });
  await flushMicrotasks();
}

describe('ApiService', () => {
  function configure(resolver: () => Promise<string | null> = async () => null): ApiService {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: HOST_URL_RESOLVER, useValue: resolver },
      ],
    });
    return TestBed.inject(ApiService);
  }

  describe('http error handling', () => {
    it('parses the JSON body on a 2xx response', async () => {
      const api = configure();
      spyOn(window, 'fetch').and.resolveTo(
        new Response(JSON.stringify({ version: '1.2.3' }), { status: 200 }),
      );

      await expectAsync(api.getVersion()).toBeResolvedTo({ version: '1.2.3' } as never);
    });

    it('throws a typed TransportError carrying status and message on a non-ok response', async () => {
      const api = configure();
      spyOn(window, 'fetch').and.resolveTo(
        new Response(JSON.stringify({ title: 'Something broke', code: 'boom' }), { status: 500 }),
      );

      try {
        await api.getVersion();
        fail('expected getVersion to reject');
      } catch (error) {
        expect(error).toBeInstanceOf(TransportError);
        const transportError = error as TransportError;
        expect(transportError.status).toBe(500);
        expect(transportError.message).toBe('Something broke');
        expect(transportError.code).toBe('boom');
      }
    });

    it('does not misparse an empty error body (no SyntaxError), falls back to status text', async () => {
      const api = configure();
      spyOn(window, 'fetch').and.resolveTo(
        new Response('', { status: 503, statusText: 'Service Unavailable' }),
      );

      try {
        await api.getVersion();
        fail('expected getVersion to reject');
      } catch (error) {
        expect(error).toBeInstanceOf(TransportError);
        const transportError = error as TransportError;
        expect(transportError.status).toBe(503);
        expect(transportError.message).toBe('Service Unavailable');
      }
    });
  });

  // An API response is live state, so it must never come out of the HTTP cache. The host sends
  // no-store, but a client talking to an older host (or a kiosk browser configured to prefer its
  // cache) would otherwise render a deck built from a stale GET /api/folders - the reported case
  // was widgets pinned by a newer release missing until the browser cache was cleared by hand.
  // Issue #528. These endpoints answer HTTP 200 even on failure, so the wire shape is the only thing
  // standing between the confirmation flow and the host - and one of these mappings decides whether a
  // user's plugin settings survive an uninstall.
  describe('plugin installation transport', () => {
    function okResponse(): Response {
      return new Response(JSON.stringify({ success: true, activated: false, rolledBack: false, warnings: [] }),
        { status: 200 });
    }

    it('uploads the artifact under the file field the host binds', async () => {
      const api = configure();
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo(okResponse());

      await api.inspectPluginArtifact(new File(['artifact'], 'deck-tools.macroDeckPlugin'));

      const [url, init] = fetchSpy.calls.mostRecent().args;
      expect(url as string).toContain('/api/plugin-installation/inspect');
      expect(((init as RequestInit).body as FormData).get('file')).not.toBeNull();
    });

    it('asks the host to delete plugin data only when the caller says so', async () => {
      const api = configure();
      const fetchSpy = spyOn(window, 'fetch').and.callFake(() => Promise.resolve(okResponse()));

      await api.uninstallPlugin('com.acme.deck-tools');
      const byDefault = fetchSpy.calls.mostRecent().args[0] as string;

      await api.uninstallPlugin('com.acme.deck-tools', { keepData: false });
      const whenAsked = fetchSpy.calls.mostRecent().args[0] as string;

      expect(byDefault).not.toContain('keepData');
      expect(whenAsked).toContain('keepData=false');
    });

    it('encodes a plugin id that would otherwise change the route', async () => {
      const api = configure();
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo(okResponse());

      await api.uninstallPlugin('com.acme/deck tools');

      expect(fetchSpy.calls.mostRecent().args[0] as string).toContain('com.acme%2Fdeck%20tools');
    });
  });

  describe('request caching', () => {
    it('sends API requests with cache no-store, with a token', async () => {
      const api = configure();
      api.setAuthHooks(() => 'my-token', async () => false, () => {});
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo(new Response('{}', { status: 200 }));

      await api.getFolders();

      expect((fetchSpy.calls.mostRecent().args[1] as RequestInit).cache).toBe('no-store');
    });

    it('sends API requests with cache no-store, without a token', async () => {
      const api = configure();
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo(new Response('{}', { status: 200 }));

      await api.getFolders();

      expect((fetchSpy.calls.mostRecent().args[1] as RequestInit).cache).toBe('no-store');
    });
  });

  describe('auth hooks', () => {
    it('attaches the Bearer token from the registered provider', async () => {
      const api = configure();
      api.setAuthHooks(() => 'my-token', async () => false, () => {});
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo(
        new Response(JSON.stringify({ version: '1.2.3' }), { status: 200 }),
      );

      await api.getVersion();

      const init = fetchSpy.calls.mostRecent().args[1] as RequestInit;
      expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer my-token');
    });

    it('sends no Authorization header when the provider has no token', async () => {
      const api = configure();
      api.setAuthHooks(() => null, async () => false, () => {});
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo(new Response('{}', { status: 200 }));

      await api.getVersion();

      const init = fetchSpy.calls.mostRecent().args[1] as RequestInit;
      expect(init.headers).toBeUndefined();
    });

    it('retries exactly once after the 401 handler reports a successful refresh', async () => {
      const api = configure();
      let token = 'stale';
      api.setAuthHooks(
        () => token,
        async () => {
          token = 'fresh';
          return true;
        },
        () => {},
      );
      const fetchSpy = spyOn(window, 'fetch').and.callFake(((_: unknown, init?: RequestInit) => {
        const auth = (init?.headers as Record<string, string> | undefined)?.['Authorization'];
        return Promise.resolve(auth === 'Bearer fresh'
          ? new Response(JSON.stringify({ version: '1.2.3' }), { status: 200 })
          : new Response('', { status: 401 }));
      }) as typeof fetch);

      await expectAsync(api.getVersion()).toBeResolvedTo({ version: '1.2.3' } as never);
      expect(fetchSpy).toHaveBeenCalledTimes(2);
    });

    it('gives up after one retry: a second 401 surfaces as TransportError', async () => {
      const api = configure();
      const onUnauthorized = jasmine.createSpy('onUnauthorized').and.resolveTo(true);
      api.setAuthHooks(() => 'token', onUnauthorized, () => {});
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo(new Response('', { status: 401 }));

      await expectAsync(api.getVersion()).toBeRejectedWithError(TransportError);
      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(onUnauthorized).toHaveBeenCalledTimes(1);
    });

    it('does not invoke the 401 handler for auth endpoints (no refresh recursion)', async () => {
      const api = configure();
      const onUnauthorized = jasmine.createSpy('onUnauthorized').and.resolveTo(true);
      api.setAuthHooks(() => null, onUnauthorized, () => {});
      spyOn(window, 'fetch').and.resolveTo(new Response('', { status: 401 }));

      await expectAsync(api.refreshSession()).toBeRejectedWithError(TransportError);
      expect(onUnauthorized).not.toHaveBeenCalled();
    });

    it('notifies the 403 handler once and does not retry (a refresh cannot fix a scope)', async () => {
      const api = configure();
      const onUnauthorized = jasmine.createSpy('onUnauthorized').and.resolveTo(true);
      const onForbidden = jasmine.createSpy('onForbidden');
      api.setAuthHooks(() => 'token', onUnauthorized, () => {}, onForbidden);
      const fetchSpy = spyOn(window, 'fetch').and.resolveTo(new Response('', { status: 403 }));

      await expectAsync(api.getVersion()).toBeRejectedWithError(TransportError);
      expect(onForbidden).toHaveBeenCalledTimes(1);
      expect(onUnauthorized).not.toHaveBeenCalled();
      expect(fetchSpy).toHaveBeenCalledTimes(1);
    });

    it('does not invoke the 403 handler for auth endpoints', async () => {
      const api = configure();
      const onForbidden = jasmine.createSpy('onForbidden');
      api.setAuthHooks(() => null, async () => false, () => {}, onForbidden);
      spyOn(window, 'fetch').and.resolveTo(new Response('', { status: 403 }));

      await expectAsync(api.refreshSession()).toBeRejectedWithError(TransportError);
      expect(onForbidden).not.toHaveBeenCalled();
    });
  });


  describe('UI WebSocket reconnect lifecycle', () => {
    const originalWebSocket = window.WebSocket;

    beforeEach(() => {
      FakeWebSocket.instances = [];
      Object.defineProperty(window, 'WebSocket', { configurable: true, value: FakeWebSocket });
    });

    afterEach(() => {
      Object.defineProperty(window, 'WebSocket', { configurable: true, value: originalWebSocket });
    });

    function acceptTickets(): jasmine.Spy {
      let sequence = 0;
      return spyOn(window, 'fetch').and.callFake(() => Promise.resolve({
        ok: true,
        status: 200,
        json: async () => ({ value: `ticket-${++sequence}` }),
      } as Response));
    }

    it('retries indefinitely at a fixed two-second interval after each failed attempt', async () => {
      jasmine.clock().install();
      try {
        const resolver = jasmine.createSpy('resolveHostUrl').and.resolveTo(null);
        const api = configure(resolver);

        api.connect();
        await flushMicrotasks();
        expect(resolver).toHaveBeenCalledTimes(1);
        expect(api.connectionState).toBe('reconnecting');

        for (let attempts = 1; attempts <= 4; attempts++) {
          jasmine.clock().tick(1999);
          await flushMicrotasks();
          expect(resolver).toHaveBeenCalledTimes(attempts);
          jasmine.clock().tick(1);
          await flushMicrotasks();
          expect(resolver).toHaveBeenCalledTimes(attempts + 1);
        }

        api.disconnect();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('reconnects once two seconds after a connected socket closes', async () => {
      jasmine.clock().install();
      try {
        acceptTickets();
        const api = configure(async () => 'http://localhost:5191');
        api.connect();
        await flushMicrotasks();
        await completeUiSocket(FakeWebSocket.instances[0]);
        expect(api.connectionState).toBe('connected');

        FakeWebSocket.instances[0].close();
        expect(api.connectionState).toBe('reconnecting');
        jasmine.clock().tick(1999);
        await flushMicrotasks();
        expect(FakeWebSocket.instances.length).toBe(1);
        jasmine.clock().tick(1);
        await flushMicrotasks();
        expect(FakeWebSocket.instances.length).toBe(2);

        await completeUiSocket(FakeWebSocket.instances[1]);
        expect(api.connectionState).toBe('connected');
        api.disconnect();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('reconnectNow replaces the wait immediately and does not leave a second timer behind', async () => {
      jasmine.clock().install();
      try {
        acceptTickets();
        const api = configure(async () => 'http://localhost:5191');
        api.connect();
        await flushMicrotasks();
        await completeUiSocket(FakeWebSocket.instances[0]);
        FakeWebSocket.instances[0].close();

        api.reconnectNow();
        api.reconnectNow();
        await flushMicrotasks();
        expect(FakeWebSocket.instances.length).toBe(2);
        await completeUiSocket(FakeWebSocket.instances[1]);

        jasmine.clock().tick(10_000);
        await flushMicrotasks();
        expect(FakeWebSocket.instances.length).toBe(2);
        api.disconnect();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('does not resurrect a connection when disconnect wins a pending host resolution', async () => {
      let resolveHost!: (value: string | null) => void;
      const resolver = () => new Promise<string | null>(resolve => { resolveHost = resolve; });
      acceptTickets();
      const api = configure(resolver);

      api.connect();
      api.disconnect();
      resolveHost('http://localhost:5191');
      await flushMicrotasks();

      expect(FakeWebSocket.instances.length).toBe(0);
      expect(api.connectionState).toBe('disconnected');
    });

    it('does not reconnect after an explicit disconnect', async () => {
      jasmine.clock().install();
      try {
        acceptTickets();
        const api = configure(async () => 'http://localhost:5191');
        api.connect();
        await flushMicrotasks();
        await completeUiSocket(FakeWebSocket.instances[0]);

        api.disconnect();
        jasmine.clock().tick(60_000);
        await flushMicrotasks();

        expect(FakeWebSocket.instances.length).toBe(1);
        expect(api.connectionState).toBe('disconnected');
      } finally {
        jasmine.clock().uninstall();
      }
    });
  });

  describe('music player artwork urls', () => {
    it('builds the artwork URL without a size parameter by default', () => {
      const api = configure();

      expect(api.getMusicPlayerArtworkUrl('spotify::default', 'abc123')).toBe(
        '/api/music-player/artwork/abc123?instanceId=spotify%3A%3Adefault',
      );
    });

    it('appends the size query when a size is given', () => {
      const api = configure();

      expect(api.getMusicPlayerArtworkUrl('spotify::default', 'abc123', 128)).toBe(
        '/api/music-player/artwork/abc123?instanceId=spotify%3A%3Adefault&size=128',
      );
    });

    it('URI-encodes the instance and artwork ids', () => {
      const api = configure();

      expect(api.getMusicPlayerArtworkUrl('a/b', 'c d')).toBe(
        '/api/music-player/artwork/c%20d?instanceId=a%2Fb',
      );
    });
  });

  describe('integration icon urls', () => {
    // Issue #754: the route is keyed by the integration id, so a replaced icon only reaches an
    // already-rendered <img> if the version the caller knows about is part of the URL.
    it('gives a replaced icon a different URL', () => {
      const api = configure();

      expect(api.getIntegrationIconUrl('test.plugin', 'aaaa1111')).not.toBe(
        api.getIntegrationIconUrl('test.plugin', 'bbbb2222'),
      );
    });

    it('builds the plain icon URL when no version is known', () => {
      const api = configure();

      expect(api.getIntegrationIconUrl('test.plugin')).toBe('/api/integrations/test.plugin/icon');
      expect(api.getIntegrationIconUrl('test.plugin', null)).toBe('/api/integrations/test.plugin/icon');
    });
  });
});
