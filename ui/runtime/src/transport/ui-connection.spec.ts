import { HttpClient } from './http-client';
import { UiConnection, UiConnectionState } from './ui-connection';

class FakeWebSocket {
  static instances: FakeWebSocket[] = [];
  static readonly OPEN = 1;
  readyState = 0;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onmessage: ((event: MessageEvent) => void) | null = null;
  onclose: (() => void) | null = null;
  readonly sent: string[] = [];

  constructor(readonly url: string, readonly protocol: string) { FakeWebSocket.instances.push(this); }
  send(value: string): void { this.sent.push(value); }
  close(): void { this.readyState = 3; if (this.onclose) this.onclose(); }

  handshake(): void {
    this.readyState = FakeWebSocket.OPEN;
    if (this.onopen) this.onopen();
    this.reply({ protocolVersion: 1, kind: 'welcome' });
    this.answerPending();
  }

  answerPending(): void {
    for (let index = 0; index < this.sent.length; index++) {
      const envelope = JSON.parse(this.sent[index]) as { kind: string; id?: string };
      if (envelope.kind === 'request' && envelope.id) {
        this.reply({ protocolVersion: 1, kind: 'response', correlationId: envelope.id, payload: null });
      }
    }
  }

  reply(value: object): void {
    if (this.onmessage) this.onmessage({ data: JSON.stringify(value) } as MessageEvent);
  }
}

describe('UiConnection', () => {
  const originalFetch = (globalThis as { fetch?: unknown }).fetch;
  const originalSocket = (globalThis as { WebSocket?: unknown }).WebSocket;
  let ticketCalls: RequestInit[];

  function stubTicket(status = 200, value: unknown = 'ticket-1'): void {
    ticketCalls = [];
    (globalThis as { fetch: unknown }).fetch = (_url: string, init: RequestInit) => {
      ticketCalls.push(init);
      return Promise.resolve({
        ok: status >= 200 && status < 300,
        status,
        statusText: '',
        text: () => Promise.resolve(JSON.stringify({ value })),
        json: () => Promise.resolve({ value }),
      } as unknown as Response);
    };
  }

  beforeEach(() => {
    FakeWebSocket.instances = [];
    (globalThis as { WebSocket: unknown }).WebSocket = FakeWebSocket;
    stubTicket();
    jasmine.clock().install();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    (globalThis as { fetch?: unknown }).fetch = originalFetch;
    (globalThis as { WebSocket?: unknown }).WebSocket = originalSocket;
  });

  const build = (onUnauthorized?: () => Promise<boolean>, features?: readonly string[]) => new UiConnection({
    http: new HttpClient({ baseUrl: () => 'http://host:8194' }),
    baseUrl: () => 'http://host:8194',
    clientId: 'client-1',
    features,
    onUnauthorized,
  });

  const registrations = (socket: FakeWebSocket) => socket.sent
    .map(value => JSON.parse(value) as { kind: string; type?: string; payload?: unknown })
    .filter(envelope => envelope.kind === 'request' && envelope.type === 'RegisterClient')
    .map(envelope => envelope.payload);

  const settle = async () => { for (let turn = 0; turn < 8; turn++) await Promise.resolve(); };

  it('starts disconnected and reports connecting as soon as it is asked', async () => {
    const connection = build();
    const seen: UiConnectionState[] = [];
    connection.state.subscribe(state => seen.push(state));

    expect(connection.state.get()).toBe('disconnected');
    connection.connect();

    expect(seen).toEqual(['connecting']);
  });

  it('mints a ticket carrying the protocol header the host requires', async () => {
    const connection = build();
    connection.connect();
    await settle();

    expect(ticketCalls.length).toBe(1);
    expect((ticketCalls[0].headers as Record<string, string>)['X-MacroDeck-Ui-Protocol']).toBe('1');
    connection.disconnect();
  });

  it('dials the websocket scheme of the host address, carrying the ticket', async () => {
    const connection = build();
    connection.connect();
    await settle();

    expect(FakeWebSocket.instances.length).toBe(1);
    expect(FakeWebSocket.instances[0].url).toBe('ws://host:8194/ws/ui?ticket=ticket-1');
    connection.disconnect();
  });

  it('reaches connected once the handshake and the registration are answered', async () => {
    const connection = build();
    connection.connect();
    await settle();

    FakeWebSocket.instances[0].handshake();
    await settle();
    FakeWebSocket.instances[0].answerPending();
    await settle();

    expect(connection.state.get()).toBe('connected');
    connection.disconnect();
  });

  it('declares its features with the registration on every connect', async () => {
    const connection = build(undefined, ['open-url']);
    connection.connect();
    await settle();
    FakeWebSocket.instances[0].handshake();
    await settle();
    FakeWebSocket.instances[0].answerPending();
    await settle();

    FakeWebSocket.instances[0].close();
    jasmine.clock().tick(2000);
    await settle();
    FakeWebSocket.instances[1].handshake();
    await settle();

    expect(registrations(FakeWebSocket.instances[0])).toEqual([['client-1', ['open-url']]]);
    expect(registrations(FakeWebSocket.instances[1])).toEqual([['client-1', ['open-url']]]);
    connection.disconnect();
  });

  it('registers with the client id alone when it declares no features', async () => {
    const connection = build();
    connection.connect();
    await settle();
    FakeWebSocket.instances[0].handshake();
    await settle();

    expect(registrations(FakeWebSocket.instances[0])).toEqual([['client-1']]);
    connection.disconnect();
  });

  it('goes reconnecting and dials again on a fixed interval after a drop', async () => {
    const connection = build();
    connection.connect();
    await settle();
    FakeWebSocket.instances[0].handshake();
    await settle();
    FakeWebSocket.instances[0].answerPending();
    await settle();

    FakeWebSocket.instances[0].close();
    expect(connection.state.get()).toBe('reconnecting');

    jasmine.clock().tick(2000);
    await settle();

    expect(FakeWebSocket.instances.length).toBe(2);
    connection.disconnect();
  });

  it('stops retrying when the hook takes ownership of the refused connection', async () => {
    stubTicket(401);
    let refused = 0;
    const connection = build(() => { refused++; return Promise.resolve(true); });

    connection.connect();
    await settle();
    jasmine.clock().tick(10_000);
    await settle();

    expect(refused).toBe(1);
    // A host that keeps saying no is not a host that is merely away.
    expect(FakeWebSocket.instances.length).toBe(0);
  });

  it('keeps retrying a refused ticket when the hook declines to take it over', async () => {
    stubTicket(401);
    let refused = 0;
    const connection = build(() => { refused++; return Promise.resolve(false); });

    connection.connect();
    await settle();
    jasmine.clock().tick(2000);
    await settle();

    // A hook that neither reconnected nor disconnected must not leave the socket dead forever.
    expect(refused).toBeGreaterThan(1);
    connection.disconnect();
  });

  it('keeps retrying when the host is merely unreachable', async () => {
    stubTicket(503);
    const connection = build();

    connection.connect();
    await settle();
    expect(connection.state.get()).toBe('reconnecting');

    jasmine.clock().tick(2000);
    await settle();

    expect(ticketCalls.length).toBeGreaterThan(1);
    connection.disconnect();
  });

  it('stops wanting a connection once disconnected', async () => {
    const connection = build();
    connection.connect();
    await settle();
    connection.disconnect();

    jasmine.clock().tick(10_000);
    await settle();

    expect(connection.state.get()).toBe('disconnected');
  });

  it('refuses a request while nothing is open, rather than dropping it silently', async () => {
    await expectAsync(build().request('AnyThing')).toBeRejected();
  });

  const connected = async (connection: UiConnection) => {
    connection.connect();
    await settle();
    FakeWebSocket.instances.at(-1)!.handshake();
    await settle();
    FakeWebSocket.instances.at(-1)!.answerPending();
    await settle();
  };

  it('keeps notifying later listeners when one of them throws', async () => {
    const connection = build();
    spyOn(console, 'error');
    connection.onNotification(() => { throw new Error('broken listener'); });
    const seen: string[] = [];
    connection.onNotification(type => seen.push(type));
    await connected(connection);

    FakeWebSocket.instances[0].reply({ protocolVersion: 1, kind: 'message', type: 'DeckChanged' });

    expect(seen).toEqual(['DeckChanged']);
    expect(connection.state.get()).toBe('connected');
    connection.disconnect();
  });

  it('replaces a connection that has gone silent when asked to reconnect now', async () => {
    jasmine.clock().mockDate(new Date(2026, 0, 1));
    const connection = build();
    await connected(connection);

    jasmine.clock().tick(35_000);
    connection.reconnectNow();
    await settle();

    expect(FakeWebSocket.instances.length).toBe(2);
    expect(connection.state.get()).toBe('reconnecting');
    connection.disconnect();
  });

  it('keeps a connection that is still hearing from the host', async () => {
    jasmine.clock().mockDate(new Date(2026, 0, 1));
    const connection = build();
    await connected(connection);

    jasmine.clock().tick(5_000);
    connection.reconnectNow();
    await settle();

    expect(FakeWebSocket.instances.length).toBe(1);
    expect(connection.state.get()).toBe('connected');
    connection.disconnect();
  });

  it('does not restart a dial that is still in progress', async () => {
    const connection = build();
    connection.connect();
    await settle();

    connection.reconnectNow();
    connection.replaceNow();
    await settle();

    expect(FakeWebSocket.instances.length).toBe(1);
    connection.disconnect();
  });

  it('does not restart a redial that is still minting its ticket', async () => {
    const connection = build();
    await connected(connection);
    FakeWebSocket.instances[0].close();
    jasmine.clock().tick(2000);

    connection.reconnectNow();
    await settle();

    expect(ticketCalls.length).toBe(2);
    expect(FakeWebSocket.instances.length).toBe(2);
    connection.disconnect();
  });

  it('dials afresh when connected again after a disconnect, even with the old dial unfinished', async () => {
    const connection = build();
    connection.connect();
    connection.disconnect();
    connection.connect();
    await settle();

    expect(ticketCalls.length).toBe(2);
    connection.disconnect();
  });

  it('replaces even a live connection after a network change', async () => {
    jasmine.clock().mockDate(new Date(2026, 0, 1));
    const connection = build();
    await connected(connection);

    connection.replaceNow();
    await settle();
    FakeWebSocket.instances[1].handshake();
    await settle();
    FakeWebSocket.instances[1].answerPending();
    await settle();

    expect(FakeWebSocket.instances.length).toBe(2);
    expect(connection.state.get()).toBe('connected');
    connection.disconnect();
  });

  it('passes host notifications to every listener until it is dropped', async () => {
    const connection = build();
    const seen: string[] = [];
    const stop = connection.onNotification(type => seen.push(type));
    connection.connect();
    await settle();
    FakeWebSocket.instances[0].handshake();
    await settle();

    FakeWebSocket.instances[0].reply({ protocolVersion: 1, kind: 'message', type: 'DeckChanged' });
    expect(seen).toEqual(['DeckChanged']);

    stop();
    FakeWebSocket.instances[0].reply({ protocolVersion: 1, kind: 'message', type: 'DeckChanged' });
    expect(seen).toEqual(['DeckChanged']);

    connection.disconnect();
  });
});
