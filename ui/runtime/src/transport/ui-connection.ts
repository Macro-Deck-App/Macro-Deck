import { WebSocketTransport } from '../protocol/websocket-transport';
import { store, WritableStore } from '../store/store';
import { HttpClient } from './http-client';
import { websocketUrl } from './websocket-url';

export type UiConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

const UI_SOCKET_PATH = '/ws/ui';
const TICKET_PATH = '/api/ui-websocket/tickets';

const UI_PROTOCOL_VERSION = 1;

const RECONNECT_DELAY_MS = 2000;

export interface UiConnectionOptions {
  http: HttpClient;
  baseUrl(): string;
  clientId: string;
  // A host that predates the declaration reads only the client id, so sending it is always safe.
  features?: readonly string[];
  onUnauthorized?(): Promise<boolean>;
}

export class UiConnection {
  readonly state: WritableStore<UiConnectionState> = store<UiConnectionState>('disconnected');

  private transport: WebSocketTransport | null = null;
  private wanted = false;
  private attempt = 0;
  private dialing = false;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly listeners: Array<(type: string, payload: unknown) => void> = [];

  constructor(private readonly options: UiConnectionOptions) {}

  get clientId(): string {
    return this.options.clientId;
  }

  onNotification(listener: (type: string, payload: unknown) => void): () => void {
    this.listeners.push(listener);
    return () => {
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners.splice(at, 1);
    };
  }

  request<T>(type: string, payload?: unknown): Promise<T> {
    if (!this.transport) return Promise.reject(new Error('The UI connection is not open.'));
    return this.transport.request<T>(type, payload);
  }

  connect(): void {
    this.wanted = true;
    if (this.transport || this.dialing) return;
    this.dialing = true;
    this.state.set(this.state.get() === 'disconnected' ? 'connecting' : 'reconnecting');
    void this.establish(++this.attempt);
  }

  // A connection still being dialled is never replaced: resume and network hooks fire repeatedly on a
  // slow link, and each replacement would start the dial over.
  reconnectNow(): void {
    this.clearRetry();
    if (this.transport && this.state.get() === 'connected' && !this.transport.live) this.abandon(this.transport);
    this.connect();
  }

  // For a network change: the old socket may look alive while its route is already gone.
  replaceNow(): void {
    this.clearRetry();
    if (this.transport && this.state.get() === 'connected') this.abandon(this.transport);
    this.connect();
  }

  disconnect(): void {
    this.wanted = false;
    this.dialing = false;
    this.clearRetry();
    if (this.transport) {
      const closing = this.transport;
      this.transport = null;
      closing.disconnect();
    }
    this.state.set('disconnected');
  }

  private isCurrent(attempt: number): boolean {
    return this.wanted && attempt === this.attempt;
  }

  private async establish(attempt: number): Promise<void> {
    let opening: WebSocketTransport | null = null;
    try {
      const ticket = await this.mintTicket();
      if (!this.isCurrent(attempt)) return;

      const transport = new WebSocketTransport(() => {
        if (this.transport !== transport) return;
        this.transport = null;
        this.state.set(this.wanted ? 'reconnecting' : 'disconnected');
        this.scheduleRetry();
      });
      opening = transport;
      transport.onAny((type, payload) => {
        const notified = this.listeners.slice();
        for (let index = 0; index < notified.length; index++) {
          try {
            notified[index](type, payload);
          } catch (error) {
            console.error('A listener for %s failed.', type, error);
          }
        }
      });
      this.transport = transport;

      const url = websocketUrl(this.options.baseUrl())
        + UI_SOCKET_PATH + '?ticket=' + encodeURIComponent(ticket);
      await transport.connect(url);
      if (!this.isCurrent(attempt)) {
        this.abandon(transport);
        return;
      }

      const registration = this.options.features
        ? [this.options.clientId, [...this.options.features]]
        : [this.options.clientId];
      await transport.request<void>('RegisterClient', registration);
      if (!this.isCurrent(attempt)) {
        this.abandon(transport);
        return;
      }

      this.dialing = false;
      this.clearRetry();
      this.state.set('connected');
    } catch (error) {
      if (opening && this.transport === opening) this.abandon(opening);
      if (!this.isCurrent(attempt)) return;
      this.dialing = false;

      this.state.set(this.wanted ? 'reconnecting' : 'disconnected');
      // A refused ticket is not a host that is merely away, so retrying it forever would spin - but
      // only when the hook actually took the connection somewhere; one that declines (an unreachable
      // host, not a refusal) falls through to this connection's own retry, which would otherwise be
      // the only thing standing between a dead socket and one that eventually comes back.
      if (UiConnection.isUnauthorized(error) && this.options.onUnauthorized) {
        // A hook that threw took the connection nowhere, so it is treated exactly like one that
        // declined. Letting the rejection escape instead would leave `wanted` true with no retry
        // armed anywhere - a socket that is trying forever and attempting nothing.
        let owned = false;
        try {
          owned = await this.options.onUnauthorized();
        } catch {
          owned = false;
        }
        if (!owned) this.scheduleRetry();
      } else {
        this.scheduleRetry();
      }
    }
  }

  private abandon(transport: WebSocketTransport): void {
    if (this.transport === transport) this.transport = null;
    transport.disconnect();
  }

  private static isUnauthorized(error: unknown): boolean {
    return /\b401\b/.test(String(error));
  }

  private async mintTicket(): Promise<string> {
    const response = await this.options.http.fetchWithAuth(
      'POST', TICKET_PATH, {}, undefined, { 'X-MacroDeck-Ui-Protocol': String(UI_PROTOCOL_VERSION) });
    if (!response.ok) throw new Error(`UI WebSocket ticket request failed: ${response.status}`);

    const body = await response.json() as { value?: unknown };
    if (typeof body.value !== 'string' || body.value.length === 0) {
      throw new Error('The UI WebSocket ticket response carried no ticket.');
    }
    return body.value;
  }

  private scheduleRetry(): void {
    if (!this.wanted || this.retryTimer !== null || this.transport) return;
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.connect();
    }, RECONNECT_DELAY_MS);
  }

  private clearRetry(): void {
    if (this.retryTimer === null) return;
    clearTimeout(this.retryTimer);
    this.retryTimer = null;
  }
}
