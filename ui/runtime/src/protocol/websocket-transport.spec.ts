import { WebSocketTransport } from './websocket-transport';

class FakeWebSocket {
  static readonly OPEN = 1;
  static instances: FakeWebSocket[] = [];
  readyState = 0;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onmessage: ((event: MessageEvent) => void) | null = null;
  onclose: (() => void) | null = null;
  readonly sent: string[] = [];

  constructor(readonly url: string, readonly protocol: string) { FakeWebSocket.instances.push(this); }
  send(value: string): void { this.sent.push(value); }
  close(): void { this.readyState = 3; this.onclose?.(); }
  open(): void { this.readyState = FakeWebSocket.OPEN; this.onopen?.(); }
  receive(value: object): void { this.onmessage?.({ data: JSON.stringify(value) } as MessageEvent); }
}

describe('WebSocketTransport', () => {
  const original = window.WebSocket;

  beforeEach(() => {
    FakeWebSocket.instances = [];
    Object.defineProperty(window, 'WebSocket', { configurable: true, value: FakeWebSocket });
  });

  afterEach(() => Object.defineProperty(window, 'WebSocket', { configurable: true, value: original }));

  it('uses JSON hello, routes notifications, and correlates a response', async () => {
    const transport = new WebSocketTransport(() => undefined);
    const connected = transport.connect('ws://host/ws/ui?ticket=secret');
    const socket = FakeWebSocket.instances[0];
    socket.open();
    expect(JSON.parse(socket.sent[0])).toEqual(jasmine.objectContaining({ kind: 'hello', protocolVersion: 1 }));
    socket.receive({ protocolVersion: 1, kind: 'welcome' });
    await connected;

    const notification = jasmine.createSpy('notification');
    transport.on('VariablesChangedEvent', notification);
    socket.receive({ protocolVersion: 1, kind: 'message', type: 'VariablesChangedEvent', payload: { changed: [] } });
    expect(notification).toHaveBeenCalledWith({ changed: [] });

    transport.send('ReportFolderChanged', [{ folderId: 'folder-1' }]);
    expect(JSON.parse(socket.sent.at(-1)!)).toEqual(jasmine.objectContaining({
      protocolVersion: 1,
      kind: 'message',
      type: 'ReportFolderChanged',
      payload: [{ folderId: 'folder-1' }],
    }));

    const result = transport.request<{ accepted: boolean }>('RegisterClient', ['client']);
    const request = JSON.parse(socket.sent.at(-1)!) as { id: string };
    socket.receive({ protocolVersion: 1, kind: 'response', correlationId: request.id, payload: { accepted: true } });
    await expectAsync(result).toBeResolvedTo({ accepted: true });
  });

  it('rejects pending work and calls the close callback once', async () => {
    const closed = jasmine.createSpy('closed');
    const transport = new WebSocketTransport(closed);
    const connected = transport.connect('ws://host/ws/ui?ticket=secret');
    const socket = FakeWebSocket.instances[0];
    socket.open();
    socket.receive({ protocolVersion: 1, kind: 'welcome' });
    await connected;
    const pending = transport.request('WatchVariables', [[]]);
    socket.close();
    await expectAsync(pending).toBeRejected();
    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('rejects connect when the socket closes before welcome', async () => {
    const transport = new WebSocketTransport(() => undefined);
    const connected = transport.connect('ws://host/ws/ui?ticket=secret');
    const socket = FakeWebSocket.instances[0];
    socket.open();
    socket.close();

    await expectAsync(connected).toBeRejectedWithError(/handshake/);
  });

  it('answers ping immediately and sends cancel when a request is aborted', async () => {
    const transport = new WebSocketTransport(() => undefined);
    const connected = transport.connect('ws://host/ws/ui?ticket=secret');
    const socket = FakeWebSocket.instances[0];
    socket.open();
    socket.receive({ protocolVersion: 1, kind: 'welcome' });
    await connected;

    socket.receive({ protocolVersion: 1, kind: 'ping', id: 'ping-1' });
    expect(JSON.parse(socket.sent.at(-1)!)).toEqual(jasmine.objectContaining({
      kind: 'pong', correlationId: 'ping-1',
    }));

    const abort = new AbortController();
    const pending = transport.request('WatchVariables', [[]], { signal: abort.signal });
    const request = JSON.parse(socket.sent.at(-1)!) as { id: string };
    abort.abort();
    await expectAsync(pending).toBeRejectedWithError(DOMException);
    expect(JSON.parse(socket.sent.at(-1)!)).toEqual(jasmine.objectContaining({
      kind: 'cancel', correlationId: request.id,
    }));
  });

  it('bounds pending correlations at 256', async () => {
    const transport = new WebSocketTransport(() => undefined);
    const connected = transport.connect('ws://host/ws/ui?ticket=secret');
    const socket = FakeWebSocket.instances[0];
    socket.open();
    socket.receive({ protocolVersion: 1, kind: 'welcome' });
    await connected;

    const pending = Array.from({ length: 256 }, () => transport.request('WatchVariables', [[]]));
    await expectAsync(transport.request('WatchVariables', [[]])).toBeRejectedWithError(/Too many pending/);
    socket.close();
    await Promise.allSettled(pending);
  });

  it('keeps the connection and the other handlers when one handler throws', async () => {
    const closed = jasmine.createSpy('closed');
    const transport = new WebSocketTransport(closed);
    const connected = transport.connect('ws://host/ws/ui?ticket=secret');
    const socket = FakeWebSocket.instances[0];
    socket.open();
    socket.receive({ protocolVersion: 1, kind: 'welcome' });
    await connected;
    spyOn(console, 'error');
    const typed = jasmine.createSpy('typed');
    const any = jasmine.createSpy('any');
    transport.on('NotificationEvent', () => { throw new Error('renderer failed'); });
    transport.on('NotificationEvent', typed);
    transport.onAny(() => { throw new Error('listener failed'); });
    transport.onAny(any);

    socket.receive({ protocolVersion: 1, kind: 'message', type: 'NotificationEvent', payload: { id: 1 } });
    socket.receive({ protocolVersion: 1, kind: 'message', type: 'NotificationEvent', payload: { id: 2 } });

    expect(typed).toHaveBeenCalledTimes(2);
    expect(any).toHaveBeenCalledWith('NotificationEvent', { id: 2 });
    expect(closed).not.toHaveBeenCalled();
    expect(socket.readyState).toBe(FakeWebSocket.OPEN);
  });

  it('still closes on a frame that is not JSON', async () => {
    const closed = jasmine.createSpy('closed');
    const transport = new WebSocketTransport(closed);
    const connected = transport.connect('ws://host/ws/ui?ticket=secret');
    const socket = FakeWebSocket.instances[0];
    socket.open();
    socket.receive({ protocolVersion: 1, kind: 'welcome' });
    await connected;

    socket.onmessage?.({ data: 'not json' } as MessageEvent);

    expect(closed).toHaveBeenCalledTimes(1);
  });

  describe('when the link goes silent', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    it('tears down at once, without waiting for the browser to finish closing', async () => {
      const closed = jasmine.createSpy('closed');
      const transport = new WebSocketTransport(closed);
      const connected = transport.connect('ws://host/ws/ui?ticket=secret');
      const socket = FakeWebSocket.instances[0];
      socket.open();
      socket.receive({ protocolVersion: 1, kind: 'welcome' });
      await connected;
      const pending = transport.request('WatchVariables', [[]]);
      // A dead link leaves the browser socket in CLOSING until its own handshake times out.
      socket.close = () => { socket.readyState = 2; };

      jasmine.clock().tick(60_000);

      expect(closed).toHaveBeenCalledTimes(1);
      await expectAsync(pending).toBeRejected();
      const late = jasmine.createSpy('late');
      transport.onAny(late);
      socket.receive({ protocolVersion: 1, kind: 'message', type: 'VariablesChangedEvent', payload: {} });
      socket.onclose?.();
      expect(late).not.toHaveBeenCalled();
      expect(closed).toHaveBeenCalledTimes(1);
    });

    it('stops counting as live once a host ping is overdue', async () => {
      jasmine.clock().mockDate(new Date(2026, 0, 1));
      const transport = new WebSocketTransport(() => undefined);
      const connected = transport.connect('ws://host/ws/ui?ticket=secret');
      const socket = FakeWebSocket.instances[0];
      socket.open();
      socket.receive({ protocolVersion: 1, kind: 'welcome' });
      await connected;

      jasmine.clock().tick(25_000);
      expect(transport.live).toBeTrue();
      socket.receive({ protocolVersion: 1, kind: 'ping', id: 'ping-1' });
      jasmine.clock().tick(31_000);
      expect(transport.live).toBeFalse();
    });
  });

  it('does not leak a correlation when serializing the request fails', async () => {
    const transport = new WebSocketTransport(() => undefined);
    const connected = transport.connect('ws://host/ws/ui?ticket=secret');
    const socket = FakeWebSocket.instances[0];
    socket.open();
    socket.receive({ protocolVersion: 1, kind: 'welcome' });
    await connected;
    const cyclic: { self?: unknown } = {};
    cyclic.self = cyclic;

    await expectAsync(transport.request('WatchVariables', cyclic)).toBeRejected();
    const valid = Array.from({ length: 256 }, () => transport.request('WatchVariables', [[]]));
    const requests = socket.sent.slice(-256).map(value => JSON.parse(value) as { id: string });
    requests.forEach(request => socket.receive({
      protocolVersion: 1, kind: 'response', correlationId: request.id, payload: { ok: true },
    }));
    await expectAsync(Promise.all(valid)).toBeResolved();
  });
});
