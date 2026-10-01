import { LocalizedText } from '../localization/localized-text';
import {
  GetVideoStreamsResponse,
  OpenVideoStreamResponse,
  VideoStreamDescriptionMessage,
  VideoStreamErrorCode,
  VideoStreamProviderItem,
  VideoStreamSessionChangedEvent,
  VideoStreamSessionClosedEvent,
  VideoStreamSessionReason,
  VideoStreamSessionState,
} from '../protocol/messages/video-stream';

export interface VideoStreamPort {
  request<T>(type: string, payload: unknown): Promise<T>;

  onNotification(listener: (type: string, payload: unknown) => void): () => void;

  onConnectionChanged(listener: (connected: boolean) => void): () => void;

  connected(): boolean;
}

export interface VideoStreamSessionListener {
  changed(session: VideoStreamSession): void;

  closed(reason: VideoStreamSessionReason, error: VideoStreamErrorCode | null, message: LocalizedText | null): void;
}

export const VIDEO_STREAM_KEEP_ALIVE_MS = 15_000;

export const VIDEO_STREAM_CATALOG_RETRY_MS = 5_000;

// A remote provider call can wait 30 s for a slot and run 30 s more before the host answers it.
export const VIDEO_STREAM_OUTSTANDING_MS = 65_000;

type Pending = { kind: 'suspend' | 'resume'; at: number };

export class VideoStreamSession {
  sessionId: string | null = null;
  revision = 0;
  state: VideoStreamSessionState = 'opening';
  description: VideoStreamDescriptionMessage | null = null;
  message: LocalizedText | null = null;
  closed = false;

  wantVisible = true;
  pending: Pending | null = null;

  constructor(
    private readonly client: VideoStreamClient,
    readonly providerId: string,
    readonly streamId: string,
    readonly listener: VideoStreamSessionListener,
  ) {}

  setVisible(visible: boolean): void {
    if (this.wantVisible === visible) return;
    this.wantVisible = visible;
    this.client.reconcile(this);
  }

  close(): void {
    this.client.release(this);
  }
}

export interface VideoStreamSurface {
  readonly client: VideoStreamClient;

  hidden(): boolean;

  setHidden(reason: string, hidden: boolean): void;

  onChange(listener: () => void): () => void;
}

export class VideoStreamClient {
  private readonly sessions = new Map<string, VideoStreamSession>();
  private readonly live = new Set<VideoStreamSession>();
  private pendingOpens = 0;
  private buffered: Array<{ type: string; payload: { sessionId?: string } }> = [];
  private keepAliveTimer: ReturnType<typeof setInterval> | null = null;
  private catalogValue: VideoStreamProviderItem[] | null = null;
  private catalogLoading: Promise<void> | null = null;
  private catalogStale = false;
  private catalogChanged = false;
  private catalogWanted = false;
  private catalogAttempted = false;
  private catalogRetry: ReturnType<typeof setTimeout> | null = null;
  private readonly catalogListeners: Array<(changed: boolean) => void> = [];
  private readonly connectionListeners: Array<(connected: boolean) => void> = [];
  private readonly surfaces = new Map<string, VideoStreamSurface>();

  constructor(private readonly port: VideoStreamPort) {
    port.onNotification((type, payload) => this.onNotification(type, payload));
    port.onConnectionChanged(connected => this.connectionChanged(connected));
  }

  connected(): boolean {
    return this.port.connected();
  }

  surface(name: string): VideoStreamSurface {
    const existing = this.surfaces.get(name);
    if (existing) return existing;

    const reasons = new Set<string>();
    const listeners: Array<() => void> = [];
    const surface: VideoStreamSurface = {
      client: this,
      hidden: () => reasons.size > 0,
      setHidden: (reason, hidden) => {
        const before = reasons.size > 0;
        if (hidden) reasons.add(reason);
        else reasons.delete(reason);
        if (before !== reasons.size > 0) listeners.slice().forEach(listener => listener());
      },
      onChange: listener => subscribe(listeners, listener),
    };
    this.surfaces.set(name, surface);
    return surface;
  }

  catalog(): VideoStreamProviderItem[] | null {
    this.catalogWanted = true;
    if (!this.catalogAttempted) this.refreshCatalog();
    return this.catalogValue;
  }

  onCatalogChanged(listener: (changed: boolean) => void): () => void {
    return subscribe(this.catalogListeners, listener);
  }

  onConnectionChanged(listener: (connected: boolean) => void): () => void {
    return subscribe(this.connectionListeners, listener);
  }

  private connectionChanged(connected: boolean): void {
    if (!connected) {
      this.buffered = [];
      const ended = Array.from(this.live);
      this.sessions.clear();
      this.live.clear();
      this.stopKeepAlive();
      for (const session of ended) this.end(session, 'consumer_disconnected', null, null);
    } else {
      this.catalogOutdated();
    }
    this.connectionListeners.slice().forEach(listener => listener(connected));
  }

  open(providerId: string, streamId: string, acceptedTransports: readonly string[],
    listener: VideoStreamSessionListener, visible = true): VideoStreamSession {
    const session = new VideoStreamSession(this, providerId, streamId, listener);
    session.wantVisible = visible;
    this.live.add(session);
    this.pendingOpens++;
    this.ensureKeepAlive();

    this.port.request<OpenVideoStreamResponse>('OpenVideoStream',
      { providerId, streamId, acceptedTransports: acceptedTransports.slice() })
      .then(response => {
        this.pendingOpens--;
        if (!this.live.has(session)) {
          this.dropBuffered(response.sessionId);
          void this.port.request('CloseVideoStream', { sessionId: response.sessionId }).catch(() => undefined);
          this.trimBuffer();
          return;
        }
        session.sessionId = response.sessionId;
        session.revision = response.revision;
        session.state = response.state;
        this.sessions.set(response.sessionId, session);
        this.replayBuffered(response.sessionId);
        this.trimBuffer();
      }, error => {
        this.pendingOpens--;
        this.trimBuffer();
        if (!this.live.delete(session)) return;
        const failure = asFailure(error);
        this.end(session, 'failed', failure.code, failure.message);
      });

    return session;
  }

  request<T>(type: string, payload: unknown): Promise<T> {
    return this.port.request<T>(type, payload);
  }

  release(session: VideoStreamSession): void {
    if (!this.live.delete(session)) return;
    session.closed = true;
    if (session.sessionId !== null) {
      this.sessions.delete(session.sessionId);
      void this.port.request('CloseVideoStream', { sessionId: session.sessionId }).catch(() => undefined);
    }
    if (this.live.size === 0) this.stopKeepAlive();
  }

  reconcile(session: VideoStreamSession): void {
    if (session.closed || session.sessionId === null || session.revision === 0) return;
    if (session.pending !== null && Date.now() - session.pending.at < VIDEO_STREAM_OUTSTANDING_MS) return;

    let kind: Pending['kind'] | null = null;
    if (session.wantVisible && session.state === 'suspended') kind = 'resume';
    else if (!session.wantVisible && session.state !== 'suspended') kind = 'suspend';
    if (kind === null) {
      session.pending = null;
      return;
    }

    const sent: Pending = { kind, at: Date.now() };
    session.pending = sent;
    this.port.request(kind === 'resume' ? 'ResumeVideoStream' : 'SuspendVideoStream', { sessionId: session.sessionId })
      .catch(error => {
        if (session.pending === sent) session.pending = null;
        if (asFailure(error).code === 'unknown_session') this.hostForgot(session);
      });
  }

  private onNotification(type: string, payload: unknown): void {
    switch (type) {
      case 'VideoStreamCatalogChangedEvent':
        this.catalogOutdated();
        return;
      case 'VideoStreamSessionChangedEvent':
      case 'VideoStreamSessionClosedEvent':
        this.route(type, payload as { sessionId?: string });
        return;
    }
  }

  private route(type: string, payload: { sessionId?: string }): void {
    const session = typeof payload?.sessionId === 'string' ? this.sessions.get(payload.sessionId) : undefined;
    if (session) {
      this.apply(session, type, payload);
    } else if (this.pendingOpens > 0) {
      this.buffered.push({ type, payload });
    }
  }

  private apply(session: VideoStreamSession, type: string, payload: unknown): void {
    if (session.closed) return;

    if (type === 'VideoStreamSessionChangedEvent') {
      const change = payload as VideoStreamSessionChangedEvent;
      if (change.revision <= session.revision) return;
      session.revision = change.revision;
      session.state = change.state;
      if (change.description) session.description = change.description;
      session.message = change.message ?? null;
      if (session.pending !== null && (session.pending.kind === 'suspend') === (change.state === 'suspended')) {
        session.pending = null;
      }
      session.listener.changed(session);
      this.reconcile(session);
    } else {
      const closed = payload as VideoStreamSessionClosedEvent;
      this.live.delete(session);
      if (session.sessionId !== null) this.sessions.delete(session.sessionId);
      if (this.live.size === 0) this.stopKeepAlive();
      this.end(session, closed.reason, closed.error ?? null, closed.message ?? null);
    }
  }

  private replayBuffered(sessionId: string): void {
    const session = this.sessions.get(sessionId)!;
    const mine = this.buffered.filter(entry => entry.payload.sessionId === sessionId);
    this.dropBuffered(sessionId);
    for (const entry of mine) this.apply(session, entry.type, entry.payload);
  }

  private dropBuffered(sessionId: string): void {
    this.buffered = this.buffered.filter(entry => entry.payload.sessionId !== sessionId);
  }

  private trimBuffer(): void {
    if (this.pendingOpens === 0) this.buffered = [];
  }

  private hostForgot(session: VideoStreamSession): void {
    if (!this.live.delete(session)) return;
    if (session.sessionId !== null) this.sessions.delete(session.sessionId);
    if (this.live.size === 0) this.stopKeepAlive();
    this.end(session, 'lease_expired', null, null);
  }

  private end(session: VideoStreamSession, reason: VideoStreamSessionReason,
    error: VideoStreamErrorCode | null, message: LocalizedText | null): void {
    if (session.closed) return;
    session.closed = true;
    session.listener.closed(reason, error, message);
  }

  private ensureKeepAlive(): void {
    if (this.keepAliveTimer !== null) return;
    this.keepAliveTimer = setInterval(() => this.keepAlive(), VIDEO_STREAM_KEEP_ALIVE_MS);
  }

  private stopKeepAlive(): void {
    if (this.keepAliveTimer === null) return;
    clearInterval(this.keepAliveTimer);
    this.keepAliveTimer = null;
  }

  private keepAlive(): void {
    this.sessions.forEach(session => {
      void this.port.request('KeepAliveVideoStream', { sessionId: session.sessionId })
        .catch(error => {
          if (asFailure(error).code === 'unknown_session') this.hostForgot(session);
        });
      this.reconcile(session);
    });
  }

  private retryCatalog(): void {
    if (this.catalogRetry !== null) return;
    this.catalogRetry = setTimeout(() => {
      this.catalogRetry = null;
      if (this.catalogWanted && this.port.connected()) this.refreshCatalog();
    }, VIDEO_STREAM_CATALOG_RETRY_MS);
  }

  private catalogOutdated(): void {
    this.catalogChanged = true;
    this.catalogAttempted = false;
    if (!this.catalogWanted) return;
    this.catalogStale = true;
    this.refreshCatalog();
  }

  private refreshCatalog(): void {
    if (this.catalogLoading !== null || !this.port.connected()) return;
    this.catalogAttempted = true;
    this.catalogStale = false;
    this.catalogLoading = this.port.request<GetVideoStreamsResponse>('GetVideoStreams', {})
      .then(response => {
        this.catalogValue = response?.providers ?? [];
        return true;
      }, () => false)
      .then(loaded => {
        this.catalogLoading = null;
        if (this.catalogStale) {
          this.refreshCatalog();
          return;
        }
        if (!loaded) {
          if (this.catalogChanged) this.retryCatalog();
          return;
        }
        const changed = this.catalogChanged;
        this.catalogChanged = false;
        this.catalogListeners.slice().forEach(listener => listener(changed));
      });
  }
}

export interface VideoStreamFailure {
  code: VideoStreamErrorCode;
  message: LocalizedText | null;
}

export function asFailure(error: unknown): VideoStreamFailure {
  const candidate = error as { code?: unknown; message?: unknown } | null;
  const code = candidate && typeof candidate.code === 'string' ? candidate.code as VideoStreamErrorCode : 'failed';
  const message = candidate && (typeof candidate.message === 'object' || typeof candidate.message === 'string')
    && !(error instanceof Error)
    ? candidate.message as LocalizedText
    : null;
  return { code, message };
}

function subscribe<T>(listeners: T[], listener: T): () => void {
  listeners.push(listener);
  return () => {
    const at = listeners.indexOf(listener);
    if (at >= 0) listeners.splice(at, 1);
  };
}
