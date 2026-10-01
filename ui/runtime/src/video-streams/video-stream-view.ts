import { LocalizationTranslator, LocalizedText, resolveLocalizedText } from '../localization/localized-text';
import { ClientAppStrings } from '../localization/generated/client-app-strings';
import {
  VideoStreamErrorCode,
  VideoStreamItem,
  VideoStreamSessionReason,
} from '../protocol/messages/video-stream';
import { VideoStreamSession, VideoStreamSurface } from './video-stream-client';
import { createVideoPlayer, playableTransports, VideoPlayer } from './video-players';

export interface VideoStreamReference {
  provider: string;
  id: string;
}

export type VideoStreamFit = 'contain' | 'cover';

export type VideoStreamStatus =
  'none' | 'connecting' | 'playing' | 'unavailable' | 'no-source' | 'not-found' | 'unsupported' | 'error';

export const VIDEO_VISIBILITY_DEBOUNCE_MS = 300;
export const VIDEO_OFF_SCREEN_CLOSE_MS = 30_000;
export const VIDEO_RETRY_MIN_MS = 1_000;
export const VIDEO_RETRY_MAX_MS = 30_000;

const WAIT_FOR_CATALOG: ReadonlySet<string> = new Set([
  'unknown_provider', 'unknown_stream', 'transport_not_accepted',
]);

type Waiting = 'nothing' | 'visible' | 'catalog' | 'connection' | 'backoff';

export class VideoStreamView {
  readonly root: HTMLElement;
  private readonly frame: HTMLElement;
  private readonly status: HTMLElement;
  private readonly playable: string[];
  private readonly doc: Document;

  private surface: VideoStreamSurface | null = null;
  private reference: VideoStreamReference | null = null;
  private fit: VideoStreamFit = 'contain';
  private box = { width: 0, height: 0 };
  private localization: LocalizationTranslator;

  private session: VideoStreamSession | null = null;
  private player: VideoPlayer | null = null;
  private negotiated: string | null = null;
  private failedTransports = new Set<string>();

  private onScreen: boolean;
  private documentVisible: boolean;
  private shown = false;
  private visibilityTimer: ReturnType<typeof setTimeout> | null = null;
  private offScreenTimer: ReturnType<typeof setTimeout> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryDelay = VIDEO_RETRY_MIN_MS;
  private waiting: Waiting = 'nothing';
  private state: VideoStreamStatus = 'none';
  private statusMessage: LocalizedText | null = null;

  private readonly unsubscribers: Array<() => void> = [];
  private readonly surfaceUnsubscribers: Array<() => void> = [];
  private observer: { disconnect(): void } | null = null;
  private disposed = false;

  constructor(doc: Document, localization: LocalizationTranslator) {
    this.doc = doc;
    this.localization = localization;
    this.playable = playableTransports(doc);

    this.root = doc.createElement('div');
    this.root.className = 'widget-video-stream';
    this.frame = doc.createElement('div');
    this.frame.className = 'widget-video-stream-frame';
    this.status = doc.createElement('div');
    this.status.className = 'widget-video-stream-status';
    this.root.appendChild(this.frame);
    this.root.appendChild(this.status);

    const hidden = (doc as Document & { hidden?: boolean }).hidden;
    this.documentVisible = hidden !== true;
    if (typeof hidden === 'boolean') {
      const listener = () => this.visibilityChanged();
      doc.addEventListener('visibilitychange', listener);
      this.unsubscribers.push(() => doc.removeEventListener('visibilitychange', listener));
    }

    const view = doc.defaultView as (Window & { IntersectionObserver?: typeof IntersectionObserver }) | null;
    if (view && typeof view.IntersectionObserver === 'function') {
      this.onScreen = false;
      const observer = new view.IntersectionObserver(entries => {
        const last = entries[entries.length - 1];
        this.onScreen = last.isIntersecting === true || last.intersectionRatio > 0;
        this.visibilityChanged();
      });
      observer.observe(this.root);
      this.observer = observer;
    } else {
      this.onScreen = true;
    }
  }

  update(surface: VideoStreamSurface | null, reference: VideoStreamReference | null, fit: VideoStreamFit,
    box: { width: number; height: number }, localization: LocalizationTranslator): void {
    this.localization = localization;
    this.fit = fit;
    const hadArea = this.box.width > 0 && this.box.height > 0;
    this.box = box;
    if (hadArea !== (box.width > 0 && box.height > 0) && this.session !== null) this.visibilityChanged();

    if (surface !== this.surface) {
      this.detachSurface();
      this.surface = surface;
      if (surface) this.attachSurface(surface);
    }

    if (!sameReference(reference, this.reference)) {
      this.reference = reference;
      this.restart();
    } else if ((this.waiting === 'nothing' || this.waiting === 'visible') && this.session === null) {
      this.pump();
    }

    this.layout();
    this.paintStatus();
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    this.closeSession();
    this.clearTimers();
    this.detachSurface();
    this.observer?.disconnect();
    this.observer = null;
    this.unsubscribers.splice(0).forEach(unsubscribe => unsubscribe());
  }

  private attachSurface(surface: VideoStreamSurface): void {
    const client = surface.client;
    this.surfaceUnsubscribers.push(
      surface.onChange(() => this.visibilityChanged()),
      client.onCatalogChanged(changed => this.catalogChanged(changed)),
      client.onConnectionChanged(connected => this.connectionChanged(connected)),
    );
  }

  private detachSurface(): void {
    this.surfaceUnsubscribers.splice(0).forEach(unsubscribe => unsubscribe());
  }

  private visible(): boolean {
    return this.onScreen && this.box.width > 0 && this.box.height > 0 && !(this.surface?.hidden() ?? false);
  }

  private visibilityChanged(): void {
    if (this.visibilityTimer !== null) clearTimeout(this.visibilityTimer);
    this.visibilityTimer = setTimeout(() => {
      this.visibilityTimer = null;
      this.applyVisibility();
    }, VIDEO_VISIBILITY_DEBOUNCE_MS);
  }

  private applyVisibility(): void {
    if (this.disposed) return;
    const documentVisible = (this.doc as Document & { hidden?: boolean }).hidden !== true;
    if (!documentVisible && this.documentVisible) {
      this.documentVisible = false;
      this.closeSession();
      this.waiting = 'visible';
      this.paintStatus();
      return;
    }
    this.documentVisible = documentVisible;

    const visible = this.visible();
    if (this.session === null) {
      if (visible && (this.waiting === 'visible' || this.waiting === 'nothing')) this.pump();
      return;
    }

    this.session.setVisible(visible);
    this.clearOffScreen();
    if (!visible) {
      this.player?.stop();
      // Suspended sessions count toward the host's per-connection session limit.
      const armedFor = this.session;
      this.offScreenTimer = setTimeout(() => {
        this.offScreenTimer = null;
        if (this.session !== armedFor || this.visible()) return;
        this.closeSession();
        this.waiting = 'visible';
      }, VIDEO_OFF_SCREEN_CLOSE_MS);
      return;
    }
    if (this.player !== null && !this.player.playing()) {
      this.shown = false;
      this.setStatus('connecting');
      if (this.session.state === 'active') this.startPlayer();
    }
  }

  private restart(): void {
    this.closeSession();
    this.clearRetry();
    this.failedTransports.clear();
    this.retryDelay = VIDEO_RETRY_MIN_MS;
    this.waiting = 'nothing';
    this.shown = false;
    this.pump();
  }

  private pump(): void {
    if (this.disposed || this.session !== null) return;
    const reference = this.reference;
    const surface = this.surface;
    if (reference === null || surface === null) {
      this.setStatus('none');
      return;
    }
    if (!surface.client.connected()) {
      this.waiting = 'connection';
      this.setStatus('connecting');
      return;
    }
    if (!this.documentVisible || !this.visible()) {
      this.waiting = 'visible';
      if (this.state === 'none') this.setStatus('connecting');
      return;
    }

    const stream = this.catalogStream();
    if (stream === 'no-provider' || stream === 'missing') {
      this.waiting = 'catalog';
      this.statusMessage = null;
      this.setStatus(stream === 'no-provider' ? 'no-source' : 'not-found');
      return;
    }
    if (stream !== null && (stream.state === 'unavailable' || stream.state === 'disconnected')) {
      this.waiting = 'catalog';
      this.statusMessage = null;
      this.setStatus('unavailable');
      return;
    }

    const transports = this.playable.filter(transport => !this.failedTransports.has(transport));
    this.waiting = 'nothing';
    this.statusMessage = null;
    this.setStatus('connecting');
    this.session = surface.client.open(reference.provider, reference.id, transports, {
      changed: session => this.sessionChanged(session),
      closed: (reason, error, message) => this.sessionClosed(reason, error, message),
    });
  }

  private sessionChanged(session: VideoStreamSession): void {
    if (session !== this.session) return;
    this.statusMessage = session.message;
    if (session.state === 'reconnecting' || session.state === 'suspended') {
      this.player?.stop();
      this.shown = false;
      if (session.state === 'reconnecting' || this.visible()) this.setStatus('connecting');
      return;
    }
    if (session.state === 'opening') {
      if (!this.shown) this.setStatus('connecting');
      return;
    }
    if (session.description === null) return;

    const description = session.description;
    if (this.player === null || this.player.transport !== description.transport) {
      this.replacePlayer(description.transport);
      if (this.player === null) {
        this.failTransport(description.transport);
        return;
      }
    }

    const signature = describe(description);
    if (!this.visible()) return;
    if (this.player.playing() && signature === this.negotiated) {
      if (this.shown) this.setStatus('playing');
      return;
    }
    this.startPlayer();
  }

  private startPlayer(): void {
    const description = this.session?.description;
    if (!this.player || !description) return;
    this.negotiated = describe(description);
    if (!this.shown) this.setStatus('connecting');
    this.player.start(description);
  }

  private replacePlayer(transport: string): void {
    this.disposePlayer();
    this.player = createVideoPlayer(this.doc, transport, {
      firstFrame: () => this.firstFrame(),
      resized: () => this.layout(),
      failed: () => this.failTransport(transport),
    });
    if (this.player) {
      this.player.element.className = 'widget-video-stream-media';
      this.frame.appendChild(this.player.element);
    }
  }

  private disposePlayer(): void {
    if (this.player === null) return;
    this.player.stop();
    this.player.element.remove();
    this.player = null;
    this.negotiated = null;
  }

  private firstFrame(): void {
    this.shown = true;
    this.failedTransports.clear();
    this.retryDelay = VIDEO_RETRY_MIN_MS;
    this.setStatus('playing');
    this.layout();
  }

  private failTransport(transport: string): void {
    const remaining = this.playable.filter(candidate => !this.failedTransports.has(candidate));
    if (remaining.length > 1) this.failedTransports.add(transport);
    this.closeSession();
    this.scheduleRetry('unavailable', null);
  }

  private sessionClosed(reason: VideoStreamSessionReason, error: VideoStreamErrorCode | null, message: LocalizedText | null): void {
    this.session = null;
    this.disposePlayer();
    this.shown = false;
    if (this.disposed) return;

    if (reason === 'consumer_disconnected' || reason === 'host_shutdown') {
      if (this.surface && !this.surface.client.connected()) {
        this.waiting = 'connection';
        this.setStatus('connecting');
        return;
      }
    }

    const code = reason === 'failed' ? error : null;
    if (code === 'transport_not_accepted' && this.failedTransports.size > 0) {
      this.failedTransports.clear();
      this.scheduleRetry('unavailable', null);
      return;
    }
    if (code !== null && WAIT_FOR_CATALOG.has(code)) {
      this.waiting = 'catalog';
      this.statusMessage = message;
      this.setStatus(code === 'transport_not_accepted' ? 'unsupported'
        : code === 'unknown_provider' ? 'no-source' : 'not-found');
      return;
    }

    this.scheduleRetry(reason === 'failed' ? 'error' : 'unavailable', message);
  }

  private scheduleRetry(status: VideoStreamStatus, message: LocalizedText | null): void {
    this.clearRetry();
    this.statusMessage = message;
    this.setStatus(status);
    this.waiting = 'backoff';
    const delay = this.retryDelay;
    this.retryDelay = Math.min(this.retryDelay * 2, VIDEO_RETRY_MAX_MS);
    if (delay >= VIDEO_RETRY_MAX_MS) this.failedTransports.clear();
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.waiting = 'nothing';
      this.pump();
    }, delay);
  }

  private catalogChanged(changed: boolean): void {
    this.layout();
    this.paintAccessibility();
    if (!changed || this.session !== null || this.waiting === 'backoff' || this.waiting === 'nothing') return;
    this.failedTransports.clear();
    this.retryDelay = VIDEO_RETRY_MIN_MS;
    if (this.waiting === 'catalog') this.waiting = 'nothing';
    this.pump();
  }

  private connectionChanged(connected: boolean): void {
    if (!connected) return;
    this.clearRetry();
    this.retryDelay = VIDEO_RETRY_MIN_MS;
    if (this.session === null) {
      this.waiting = 'nothing';
      this.pump();
    }
  }

  private closeSession(): void {
    const session = this.session;
    this.session = null;
    this.disposePlayer();
    this.shown = false;
    session?.close();
  }

  private clearRetry(): void {
    if (this.retryTimer === null) return;
    clearTimeout(this.retryTimer);
    this.retryTimer = null;
  }

  private clearOffScreen(): void {
    if (this.offScreenTimer === null) return;
    clearTimeout(this.offScreenTimer);
    this.offScreenTimer = null;
  }

  private clearTimers(): void {
    this.clearRetry();
    this.clearOffScreen();
    if (this.visibilityTimer !== null) clearTimeout(this.visibilityTimer);
    this.visibilityTimer = null;
  }

  private catalogStream(): VideoStreamItem | 'no-provider' | 'missing' | null {
    const reference = this.reference;
    const catalog = this.surface?.client.catalog() ?? null;
    if (reference === null || catalog === null) return null;
    const provider = catalog.find(candidate => candidate.id === reference.provider);
    if (provider === undefined) return 'no-provider';
    if (provider.streams.length === 0) return null;
    const stream = provider?.streams.find(candidate => candidate.id === reference.id);
    return stream ?? 'missing';
  }

  private setStatus(status: VideoStreamStatus): void {
    this.state = status;
    if (status !== 'error' && status !== 'unavailable' && status !== 'connecting' && status !== 'unsupported'
      && status !== 'not-found' && status !== 'no-source') {
      this.statusMessage = null;
    }
    this.paintStatus();
  }

  private paintStatus(): void {
    const text = this.statusText();
    this.root.setAttribute('data-status', this.state);
    if (this.status.textContent !== text) this.status.textContent = text;
    this.status.hidden = this.state === 'playing' || text === '';
    this.paintAccessibility();
  }

  private statusText(): string {
    if (this.statusMessage !== null) {
      const provided = resolveLocalizedText(this.statusMessage, this.localization);
      if (provided !== '' && this.state !== 'playing' && this.state !== 'none') return provided;
    }
    switch (this.state) {
      case 'none': return this.reference === null ? this.translate(ClientAppStrings.Deck.VideoStream.NoStream) : '';
      case 'connecting': return this.translate(ClientAppStrings.Deck.VideoStream.Connecting);
      case 'unavailable':
      case 'error': return this.translate(ClientAppStrings.Errors.VideoStream.StreamUnavailable);
      case 'no-source': return this.translate(ClientAppStrings.Errors.VideoStream.UnknownProvider);
      case 'not-found': return this.translate(ClientAppStrings.Errors.VideoStream.UnknownStream);
      case 'unsupported': return this.translate(ClientAppStrings.Errors.VideoStream.TransportNotAccepted);
      default: return '';
    }
  }

  private paintAccessibility(): void {
    const stream = this.catalogStream();
    const name = stream !== null && stream !== 'missing' && stream !== 'no-provider' ? resolveLocalizedText(stream.name, this.localization) : '';
    const status = this.state === 'playing' ? '' : this.statusText();
    const label = name !== '' && status !== ''
      ? this.translate(ClientAppStrings.Deck.VideoStream.AccessibleLabel, { stream: name, status })
      : name || status;
    if (label === '') {
      this.root.removeAttribute('role');
      this.root.removeAttribute('aria-label');
      return;
    }
    this.root.setAttribute('role', 'img');
    this.root.setAttribute('aria-label', label);
  }

  private translate(qualified: string, args?: Record<string, unknown>): string {
    const at = qualified.indexOf(':');
    return this.localization.translate(qualified.slice(0, at), qualified.slice(at + 1), args);
  }

  private layout(): void {
    const { width, height } = this.box;
    const intrinsic = this.player?.intrinsicSize() ?? this.catalogSize();
    const rect = frameRect(width, height, intrinsic, this.fit);
    setPx(this.frame, 'left', rect.left);
    setPx(this.frame, 'top', rect.top);
    setPx(this.frame, 'width', rect.width);
    setPx(this.frame, 'height', rect.height);
    const fontSize = `${Math.round(Math.max(9, Math.min(width, height) * 0.08))}px`;
    if (this.status.style.fontSize !== fontSize) this.status.style.fontSize = fontSize;
  }

  private catalogSize(): { width: number; height: number } | null {
    const stream = this.catalogStream();
    if (stream === null || stream === 'missing' || stream === 'no-provider' || !stream.width || !stream.height) return null;
    return { width: stream.width, height: stream.height };
  }
}

export function frameRect(boxWidth: number, boxHeight: number, intrinsic: { width: number; height: number } | null,
  fit: VideoStreamFit): { left: number; top: number; width: number; height: number } {
  if (intrinsic === null || boxWidth <= 0 || boxHeight <= 0) {
    return { left: 0, top: 0, width: Math.max(boxWidth, 0), height: Math.max(boxHeight, 0) };
  }
  const scaleX = boxWidth / intrinsic.width;
  const scaleY = boxHeight / intrinsic.height;
  const scale = fit === 'cover' ? Math.max(scaleX, scaleY) : Math.min(scaleX, scaleY);
  const width = intrinsic.width * scale;
  const height = intrinsic.height * scale;
  return { left: (boxWidth - width) / 2, top: (boxHeight - height) / 2, width, height };
}

function setPx(element: HTMLElement, name: 'left' | 'top' | 'width' | 'height', value: number): void {
  const next = `${Math.round(value * 100) / 100}px`;
  if (element.style[name] !== next) element.style[name] = next;
}

function sameReference(a: VideoStreamReference | null, b: VideoStreamReference | null): boolean {
  return a === b || (a !== null && b !== null && a.provider === b.provider && a.id === b.id);
}

function describe(description: { transport: string; url?: string }): string {
  return `${description.transport}\n${description.url ?? ''}`;
}
