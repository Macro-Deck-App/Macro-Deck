import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService, LOOPBACK_REAUTH_ENVIRONMENT, LoopbackReauthEnvironment } from './auth.service';
import { CROSS_TAB_LOCK_ENVIRONMENT } from './cross-tab-lock';
import { ApiService } from '../transport/api.service';
import { HOST_URL_RESOLVER } from '../transport/host-url';

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

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

const trustedStatus = { setupComplete: true, authenticated: true, trusted: true, scope: 'admin', username: 'admin' };
const untrustedStatus = { setupComplete: true, authenticated: false, trusted: false, scope: null, username: null };

describe('AuthService trust recovery', () => {
  const realSetTimeout = window.setTimeout.bind(window);
  const originalWebSocket = window.WebSocket;
  let status: () => Response;
  let ticket: () => Response;
  let refresh: () => Response;
  let tickets: number;

  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 4; turn++) await new Promise(resolve => realSetTimeout(resolve, 0));
  };

  const advance = async (milliseconds: number): Promise<void> => {
    jasmine.clock().tick(milliseconds);
    await settle();
  };

  const liveSocket = async (): Promise<void> => {
    const socket = FakeWebSocket.instances[FakeWebSocket.instances.length - 1];
    socket.open();
    socket.receive({ protocolVersion: 1, kind: 'welcome' });
    await settle();
    const requests = socket.sent.map(value => JSON.parse(value) as { kind: string; id?: string })
      .filter(envelope => envelope.kind === 'request');
    socket.receive({ protocolVersion: 1, kind: 'response', correlationId: requests[requests.length - 1].id });
    await settle();
  };

  const signedInWindow = async (reauth?: Partial<LoopbackReauthEnvironment>):
  Promise<{ auth: AuthService; api: ApiService }> => {
    let lastAttempt: number | null = null;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
        { provide: CROSS_TAB_LOCK_ENVIRONMENT, useValue: {
          locks: { request: <T>(_name: string, _options: unknown, callback: () => Promise<T>) => callback() },
          storage: null,
          now: () => Date.now(),
          delay: () => Promise.resolve(),
        } },
        { provide: LOOPBACK_REAUTH_ENVIRONMENT, useValue: {
          reauthenticate: undefined,
          lastAttempt: () => lastAttempt,
          recordAttempt: (at: number) => { lastAttempt = at; },
          now: () => Date.now(),
          ...reauth,
        } },
      ],
    });
    const auth = TestBed.inject(AuthService);
    await auth.bootstrap();
    return { auth, api: TestBed.inject(ApiService) };
  };

  beforeEach(() => {
    localStorage.clear();
    FakeWebSocket.instances = [];
    Object.defineProperty(window, 'WebSocket', { configurable: true, value: FakeWebSocket });
    status = () => jsonResponse(trustedStatus);
    ticket = () => jsonResponse({}, 401);
    refresh = () => jsonResponse({}, 401);
    tickets = 0;
    spyOn(window, 'fetch').and.callFake((input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith('/api/auth/status')) return Promise.resolve(status());
      if (url.endsWith('/api/auth/refresh')) return Promise.resolve(refresh());
      if (url.endsWith('/api/ui-websocket/tickets')) {
        tickets++;
        return Promise.resolve(ticket());
      }
      if (url.endsWith('/api/profiles')) return Promise.resolve(jsonResponse({}, 401));
      return Promise.reject(new Error(`unrouted fetch: ${url}`));
    });
    jasmine.clock().install();
    jasmine.clock().mockDate();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    Object.defineProperty(window, 'WebSocket', { configurable: true, value: originalWebSocket });
  });

  it('asks for a new ticket again without a reload when no shell can restore trust', async () => {
    const { api } = await signedInWindow();
    api.connect();
    await settle();
    expect(tickets).toBe(1);

    await advance(1999);
    expect(tickets).toBe(1);
    await advance(1);
    expect(tickets).toBe(2);
    await advance(3999);
    expect(tickets).toBe(2);
    await advance(1);
    expect(tickets).toBe(3);

    ticket = () => jsonResponse({ value: 'ticket' });
    await advance(8000);
    await liveSocket();

    expect(api.isConnected).toBeTrue();
  });

  it('gives the shell its reload time before the first retry', async () => {
    const reauthenticate = jasmine.createSpy('reauthenticate').and.resolveTo();
    const { api } = await signedInWindow({ reauthenticate });
    api.connect();
    await settle();

    await advance(29_999);
    expect(tickets).toBe(1);
    await advance(1);

    expect(tickets).toBe(2);
    expect(reauthenticate).toHaveBeenCalledTimes(1);
  });

  it('never reloads the window twice for one incident', async () => {
    const reauthenticate = jasmine.createSpy('reauthenticate').and.resolveTo();
    const { api } = await signedInWindow({ reauthenticate });
    api.connect();
    await settle();

    for (let minute = 0; minute < 5; minute++) await advance(60_000);

    expect(tickets).toBeGreaterThan(3);
    expect(reauthenticate).toHaveBeenCalledTimes(1);
  });

  it('starts over at the shortest delay once the socket is back', async () => {
    const { api } = await signedInWindow();
    api.connect();
    await settle();
    await advance(2000);
    await advance(4000);
    ticket = () => jsonResponse({ value: 'ticket' });
    await advance(8000);
    await liveSocket();

    ticket = () => jsonResponse({}, 401);
    FakeWebSocket.instances[FakeWebSocket.instances.length - 1].close();
    await advance(5000);
    const afterClose = tickets;
    await advance(2000);

    expect(tickets).toBe(afterClose + 1);
  });

  it('takes the window to the login form when the host no longer trusts it and the session is gone', async () => {
    const { auth, api } = await signedInWindow();
    api.connect();
    await settle();
    status = () => jsonResponse(untrustedStatus);

    await advance(2000);

    expect(auth.trusted()).toBeFalse();
    expect(auth.state()).toBe('loggedOut');
  });

  it('takes the window to first-run setup when the host has no account any more', async () => {
    const { auth, api } = await signedInWindow();
    api.connect();
    await settle();
    status = () => jsonResponse({ ...untrustedStatus, setupComplete: false });

    await advance(2000);

    expect(auth.state()).toBe('setupRequired');
  });

  it('reconnects on its refresh cookie when the host no longer trusts the window', async () => {
    const { auth, api } = await signedInWindow();
    api.connect();
    await settle();
    status = () => jsonResponse(untrustedStatus);
    refresh = () => jsonResponse({ accessToken: 'jwt', expiresInSeconds: 900, scope: 'admin', username: 'admin' });
    ticket = () => jsonResponse({ value: 'ticket' });

    await advance(2000);

    expect(auth.state()).toBe('authenticated');
    expect(auth.trusted()).toBeFalse();
    expect(tickets).toBe(2);
  });

  it('keeps trying while the host cannot be asked', async () => {
    const { auth, api } = await signedInWindow();
    api.connect();
    await settle();
    status = () => { throw new TypeError('Failed to fetch'); };

    await advance(2000);
    status = () => jsonResponse(trustedStatus);
    await advance(4000);

    expect(tickets).toBe(2);
    expect(auth.state()).toBe('authenticated');
  });

  it('does not reconnect after a sign-out during the retry', async () => {
    const { auth, api } = await signedInWindow();
    spyOn(api, 'logoutSession').and.resolveTo();
    api.connect();
    await settle();

    await auth.logout();
    await advance(60_000);

    expect(tickets).toBe(1);
    expect(auth.state()).toBe('loggedOut');
  });

  it('leaves a live socket alone after a stray 401', async () => {
    ticket = () => jsonResponse({ value: 'ticket' });
    const { api } = await signedInWindow();
    api.connect();
    await settle();
    await liveSocket();
    const sockets = FakeWebSocket.instances.length;

    await api.getProfiles().catch(() => undefined);
    await advance(2000);

    expect(FakeWebSocket.instances.length).toBe(sockets);
    expect(api.isConnected).toBeTrue();
  });
});
