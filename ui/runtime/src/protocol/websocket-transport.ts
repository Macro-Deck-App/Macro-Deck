import { randomToken } from '../util/random-token';

export interface WebSocketEnvelope {
  protocolVersion: number;
  kind: string;
  type?: string;
  id?: string;
  correlationId?: string;
  payload?: unknown;
  error?: { code: string; message?: string };
}

export class WebSocketTransport {
  private static readonly PROTOCOL_VERSION = 1;
  private static readonly STALE_AFTER_MS = 60_000;
  private static readonly MAX_PENDING_REQUESTS = 256;
  private socket: WebSocket | null = null;
  private readonly handlers = new Map<string, Set<(payload: unknown) => void>>();
  private readonly anyHandlers = new Set<(type: string, payload: unknown) => void>();
  private readonly pending = new Map<string, { resolve: (value: unknown) => void; reject: (reason: unknown) => void }>();
  private sequence = 0;
  private staleTimer: ReturnType<typeof setTimeout> | null = null;
  private closed = false;

  constructor(private readonly onClose: () => void) {}

  async connect(url: string): Promise<void> {
    const socket = new WebSocket(url, 'macrodeck.ui.v1');
    this.socket = socket;
    const welcome = new Promise<void>((resolve, reject) => {
      socket.onopen = () => this.write({ protocolVersion: 1, kind: 'hello' });
      socket.onerror = () => {
        reject(new Error('WebSocket connection failed.'));
        socket.close();
      };
      socket.onmessage = event => {
        try {
          const envelope = JSON.parse(String(event.data)) as WebSocketEnvelope;
          this.refreshStaleTimer();
          if (envelope.protocolVersion !== WebSocketTransport.PROTOCOL_VERSION) {
            reject(new Error('Unsupported UI WebSocket protocol.'));
            socket.close();
            return;
          }
          if (envelope.kind === 'welcome') {
            resolve();
            return;
          }
          this.receive(envelope);
        } catch (error) {
          reject(error);
          socket.close();
        }
      };
      socket.onclose = () => {
        reject(new Error('WebSocket closed before the handshake completed.'));
        this.close();
      };
    });
    this.refreshStaleTimer();
    await welcome;
  }

  on<T>(type: string, handler: (payload: T) => void): () => void {
    const handlers = this.handlers.get(type) ?? new Set<(payload: unknown) => void>();
    handlers.add(handler as (payload: unknown) => void);
    this.handlers.set(type, handlers);
    return () => handlers.delete(handler as (payload: unknown) => void);
  }

  onAny(handler: (type: string, payload: unknown) => void): () => void {
    this.anyHandlers.add(handler);
    return () => this.anyHandlers.delete(handler);
  }

  send(type: string, payload?: unknown): void {
    this.write({ protocolVersion: 1, kind: 'message', type, payload });
  }

  request<T>(type: string, payload?: unknown, options?: { signal?: AbortSignal }): Promise<T> {
    const id = `${++this.sequence}-${randomToken()}`;
    return new Promise<T>((resolve, reject) => {
      if (this.closed || !this.socket || this.socket.readyState !== WebSocket.OPEN) {
        reject(new Error('WebSocket is not connected.'));
        return;
      }
      if (options?.signal?.aborted) {
        reject(new DOMException('The request was cancelled.', 'AbortError'));
        return;
      }
      if (this.pending.size >= WebSocketTransport.MAX_PENDING_REQUESTS) {
        reject(new Error('Too many pending WebSocket requests.'));
        return;
      }
      const abort = () => {
        this.pending.delete(id);
        try {
          this.write({ protocolVersion: 1, kind: 'cancel', correlationId: id });
        } catch {
          // Closing the socket races cancellation; the local promise still has to settle.
        } finally {
          reject(new DOMException('The request was cancelled.', 'AbortError'));
        }
      };
      options?.signal?.addEventListener('abort', abort, { once: true });
      this.pending.set(id, {
        resolve: value => { options?.signal?.removeEventListener('abort', abort); resolve(value as T); },
        reject: reason => { options?.signal?.removeEventListener('abort', abort); reject(reason); },
      });
      try {
        this.write({ protocolVersion: 1, kind: 'request', type, id, payload });
      } catch (error) {
        this.pending.delete(id);
        options?.signal?.removeEventListener('abort', abort);
        reject(error);
      }
    });
  }

  disconnect(): void {
    if (this.closed) return;
    try { this.write({ protocolVersion: 1, kind: 'goodbye' }); } catch { }
    this.socket?.close();
    this.close();
  }

  private receive(envelope: WebSocketEnvelope): void {
    if (envelope.kind === 'ping') {
      this.write({ protocolVersion: 1, kind: 'pong', correlationId: envelope.id });
      return;
    }
    if (envelope.kind === 'message' && envelope.type) {
      this.handlers.get(envelope.type)?.forEach(handler => handler(envelope.payload));
      this.anyHandlers.forEach(handler => handler(envelope.type!, envelope.payload));
      return;
    }
    if ((envelope.kind === 'response' || envelope.kind === 'error') && envelope.correlationId) {
      const pending = this.pending.get(envelope.correlationId);
      if (!pending) return;
      this.pending.delete(envelope.correlationId);
      envelope.kind === 'response' ? pending.resolve(envelope.payload) : pending.reject(envelope.error ?? new Error('Request failed.'));
    }
  }

  private write(envelope: WebSocketEnvelope): void {
    if (this.socket?.readyState !== WebSocket.OPEN) throw new Error('WebSocket is not connected.');
    this.socket.send(JSON.stringify(envelope));
  }

  private refreshStaleTimer(): void {
    if (this.staleTimer) clearTimeout(this.staleTimer);
    this.staleTimer = setTimeout(() => this.socket?.close(), WebSocketTransport.STALE_AFTER_MS);
  }

  private close(): void {
    if (this.closed) return;
    this.closed = true;
    this.socket = null;
    if (this.staleTimer) clearTimeout(this.staleTimer);
    this.pending.forEach(({ reject }) => reject(new Error('WebSocket disconnected.')));
    this.pending.clear();
    this.onClose();
  }
}
