import { UiNode } from '../ui-framework/ui-node.interface';
import { renderUiNode, UiNodeRenderHandle } from '../render/ui-node-renderer';
import { UiRenderHost } from '../render/ui-render-host';
import { createUiComponentRegistry, UI_CORE_COMPONENTS } from '../ui-framework/component-registry';
import { VideoStreamProviderItem } from '../protocol/messages/video-stream';
import {
  VIDEO_STREAM_CATALOG_RETRY_MS,
  VIDEO_STREAM_KEEP_ALIVE_MS,
  VideoStreamClient,
  VideoStreamPort,
  VideoStreamSession,
  VideoStreamSessionListener,
  VideoStreamSurface,
} from './video-stream-client';
import {
  frameRect, VIDEO_OFF_SCREEN_CLOSE_MS, VIDEO_RETRY_MAX_MS, VIDEO_RETRY_MIN_MS, VIDEO_VISIBILITY_DEBOUNCE_MS,
} from './video-stream-view';

interface FakeRequest {
  type: string;
  payload: { sessionId?: string; [key: string]: unknown };
  resolve(value?: unknown): void;
  reject(error: unknown): void;
}

class FakePort implements VideoStreamPort {
  readonly requests: FakeRequest[] = [];
  online = true;
  catalog: VideoStreamProviderItem[] = [];
  private readonly notificationListeners: Array<(type: string, payload: unknown) => void> = [];
  private readonly connectionListeners: Array<(connected: boolean) => void> = [];
  readonly answered = new Set(['KeepAliveVideoStream', 'CloseVideoStream', 'SignalVideoStream']);
  catalogFails = false;

  request<T>(type: string, payload: unknown): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const request: FakeRequest = {
        type, payload: payload as FakeRequest['payload'], resolve: value => resolve(value as T), reject,
      };
      this.requests.push(request);
      if (this.answered.has(type)) resolve(undefined as T);
      if (type === 'GetVideoStreams') {
        if (this.catalogFails) reject({ code: 'failed' });
        else resolve({ providers: this.catalog } as T);
      }
    });
  }

  onNotification(listener: (type: string, payload: unknown) => void): () => void {
    this.notificationListeners.push(listener);
    return () => undefined;
  }

  onConnectionChanged(listener: (connected: boolean) => void): () => void {
    this.connectionListeners.push(listener);
    return () => undefined;
  }

  connected(): boolean {
    return this.online;
  }

  push(type: string, payload: unknown): void {
    this.notificationListeners.slice().forEach(listener => listener(type, payload));
  }

  setConnected(connected: boolean): void {
    this.online = connected;
    this.connectionListeners.slice().forEach(listener => listener(connected));
  }

  sent(type: string): FakeRequest[] {
    return this.requests.filter(request => request.type === type);
  }

  last(type: string): FakeRequest {
    const matching = this.sent(type);
    if (matching.length === 0) throw new Error(`No ${type} was sent.`);
    return matching[matching.length - 1];
  }
}

async function flush(): Promise<void> {
  for (let index = 0; index < 20; index++) await Promise.resolve();
}

function recorder(): VideoStreamSessionListener & { changes: VideoStreamSession[]; closes: unknown[][] } {
  const changes: VideoStreamSession[] = [];
  const closes: unknown[][] = [];
  return {
    changes,
    closes,
    changed: session => changes.push(session),
    signal: () => undefined,
    closed: (reason, error, message) => closes.push([reason, error, message]),
  };
}

function changed(sessionId: string, revision: number, state: string, url = 'http://camera/stream.mjpg') {
  return {
    sessionId, revision, state, reason: 'none',
    description: state === 'active' ? { transport: 'mjpeg', url } : undefined,
  };
}

describe('VideoStreamClient', () => {
  let port: FakePort;
  let client: VideoStreamClient;

  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 8, 28, 12, 0, 0));
    port = new FakePort();
    client = new VideoStreamClient(port);
  });

  afterEach(() => jasmine.clock().uninstall());

  it('asks the host for a session on the referenced stream with the transports this client plays', () => {
    client.open('com.example.obs::studio', 'Program', ['webrtc', 'mjpeg'], recorder());

    expect(port.last('OpenVideoStream').payload).toEqual({
      providerId: 'com.example.obs::studio', streamId: 'Program', acceptedTransports: ['webrtc', 'mjpeg'],
    });
  });

  it('applies a description the host pushed before it answered the open', async () => {
    const listener = recorder();
    client.open('p::a', 's', ['mjpeg'], listener);

    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();

    expect(listener.changes.length).toBe(1);
    expect(listener.changes[0].description?.url).toBe('http://camera/stream.mjpg');
  });

  it('closes a session released while its open was still in flight, once the host names it', async () => {
    const listener = recorder();
    const session = client.open('p::a', 's', ['mjpeg'], listener);
    session.close();

    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();

    expect(port.sent('CloseVideoStream').map(request => request.payload)).toEqual([{ sessionId: 's1' }]);
    expect(listener.changes).toEqual([]);
    expect(listener.closes).toEqual([]);
  });

  it('closes a session exactly once when it is released', async () => {
    const session = client.open('p::a', 's', ['mjpeg'], recorder());
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();

    session.close();
    session.close();

    expect(port.sent('CloseVideoStream').length).toBe(1);
  });

  it('renews every open session well inside the host lease', async () => {
    client.open('p::a', 's', ['mjpeg'], recorder());
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();

    jasmine.clock().tick(44_000);

    expect(port.sent('KeepAliveVideoStream').length).toBeGreaterThanOrEqual(2);
  });

  it('ignores a session change older than the one it already applied', async () => {
    const listener = recorder();
    client.open('p::a', 's', ['mjpeg'], listener);
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();

    port.push('VideoStreamSessionChangedEvent', changed('s1', 2, 'active', 'http://camera/new.mjpg'));
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active', 'http://camera/old.mjpg'));

    expect(listener.changes.length).toBe(1);
    expect(listener.changes[0].description?.url).toBe('http://camera/new.mjpg');
  });

  it('suspends a view hidden while opening only once the host reports the session open', async () => {
    const session = client.open('p::a', 's', ['mjpeg'], recorder());
    session.setVisible(false);
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();

    expect(port.sent('SuspendVideoStream')).toEqual([]);

    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));

    expect(port.sent('SuspendVideoStream').length).toBe(1);
  });

  it('suspends once when the open state was pushed before the open response while hidden', async () => {
    const session = client.open('p::a', 's', ['mjpeg'], recorder());
    session.setVisible(false);
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();

    expect(port.sent('SuspendVideoStream').length).toBe(1);
  });

  it('asks again for a resume the host refused as busy, and ends active', async () => {
    const session = client.open('p::a', 's', ['mjpeg'], recorder());
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'suspended'));
    port.last('ResumeVideoStream').reject({ code: 'busy' });
    await flush();

    jasmine.clock().tick(VIDEO_STREAM_KEEP_ALIVE_MS);
    await flush();
    expect(port.sent('ResumeVideoStream').length).toBe(2);

    port.push('VideoStreamSessionChangedEvent', changed('s1', 2, 'active'));
    expect(session.state).toBe('active');
  });

  it('sends one resume to a provider that takes long to answer it', async () => {
    const session = client.open('p::a', 's', ['mjpeg'], recorder());
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();
    session.setVisible(false);
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'suspended'));

    session.setVisible(true);
    jasmine.clock().tick(50_000);
    await flush();

    expect(port.sent('ResumeVideoStream').length).toBe(1);
  });

  it('reports a session the host no longer knows as expired', async () => {
    const listener = recorder();
    client.open('p::a', 's', ['mjpeg'], listener);
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();
    port.answered.delete('KeepAliveVideoStream');

    jasmine.clock().tick(VIDEO_STREAM_KEEP_ALIVE_MS);
    port.last('KeepAliveVideoStream').reject({ code: 'unknown_session' });
    await flush();

    expect(listener.closes).toEqual([['lease_expired', null, null]]);
  });

  it('ends every session when the connection to Macro Deck drops', async () => {
    const opened = recorder();
    const opening = recorder();
    client.open('p::a', 's', ['mjpeg'], opened);
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();
    client.open('p::a', 't', ['mjpeg'], opening);

    port.setConnected(false);

    expect(opened.closes).toEqual([['consumer_disconnected', null, null]]);
    expect(opening.closes).toEqual([['consumer_disconnected', null, null]]);
  });

  it('reports a refused open with the host error code', async () => {
    const listener = recorder();
    client.open('p::a', 's', ['mjpeg'], listener);
    port.last('OpenVideoStream').reject({ code: 'unknown_stream', message: 'gone' });
    await flush();

    expect(listener.closes).toEqual([['failed', 'unknown_stream', 'gone']]);
  });

  it('discards pushes for sessions no open is waiting for', async () => {
    const listener = recorder();
    client.open('p::a', 's', ['mjpeg'], listener);
    port.last('OpenVideoStream').resolve({ sessionId: 's1', revision: 0, state: 'opening' });
    await flush();

    port.push('VideoStreamSessionClosedEvent', { sessionId: 'someone-else', reason: 'provider_removed' });
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));

    expect(listener.closes).toEqual([]);
    expect(listener.changes.length).toBe(1);
  });

  it('keeps surfaces independent, so hiding the deck leaves the screensaver visible', () => {
    client.surface('deck').setHidden('screensaver', true);
    client.surface('modal').setHidden('screensaver', true);

    expect(client.surface('deck').hidden()).toBeTrue();
    expect(client.surface('modal').hidden()).toBeTrue();
    expect(client.surface('screensaver').hidden()).toBeFalse();

    client.surface('deck').setHidden('screensaver', false);
    expect(client.surface('deck').hidden()).toBeFalse();
  });
});

describe('frameRect', () => {
  it('letterboxes the whole picture for contain', () => {
    expect(frameRect(200, 200, { width: 1920, height: 1080 }, 'contain'))
      .toEqual({ left: 0, top: 43.75, width: 200, height: 112.5 });
  });

  it('covers the whole box and crops for cover', () => {
    const rect = frameRect(200, 200, { width: 1920, height: 1080 }, 'cover');
    expect(rect.height).toBe(200);
    expect(rect.width).toBeCloseTo(355.56, 2);
    expect(rect.left).toBeCloseTo(-77.78, 2);
  });

  it('fills the box while the size is not known yet', () => {
    expect(frameRect(120, 80, null, 'contain')).toEqual({ left: 0, top: 0, width: 120, height: 80 });
  });
});

describe('macrodeck.video-stream', () => {
  const PROVIDER = 'com.example.cameras::door';
  let port: FakePort;
  let client: VideoStreamClient;
  let surface: VideoStreamSurface;
  let container: HTMLElement;
  let handle: UiNodeRenderHandle | null;
  let hidden: boolean;

  function host(withVideoStreams = true): UiRenderHost {
    return {
      localization: {
        translate: (scope, key, args) => args ? `${key}(${Object.values(args).join(' / ')})` : `${scope}:${key}`,
      },
      resourceUrl: () => null,
      now: () => Date.now(),
      culture: () => 'en-US',
      simpleRendering: () => false,
      fontFamily: () => null,
      fontReady: () => true,
      emit: () => undefined,
      ...(withVideoStreams ? { videoStreams: () => surface } : {}),
    };
  }

  function node(properties: Record<string, unknown> = { stream: { provider: PROVIDER, id: 'front' } }): UiNode {
    return { id: 'video', type: 'macrodeck.video-stream', properties, children: [] } as UiNode;
  }

  function mount(tree = node(), renderHost = host()): HTMLElement {
    handle = renderUiNode(container, tree, { width: 200, height: 200 }, null, 200, renderHost);
    return container.querySelector('.widget-video-stream') as HTMLElement;
  }

  async function answerOpen(sessionId = 's1'): Promise<void> {
    port.last('OpenVideoStream').resolve({ sessionId, revision: 0, state: 'opening' });
    await flush();
  }

  function setDocumentHidden(value: boolean): void {
    hidden = value;
    document.dispatchEvent(new Event('visibilitychange'));
  }

  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 8, 28, 12, 0, 0));
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    port = new FakePort();
    port.catalog = [{
      id: PROVIDER, name: 'Door', streams: [
        { id: 'front', name: 'Front door', width: 1920, height: 1080, hasAudio: false, state: 'connected' },
      ],
    }];
    client = new VideoStreamClient(port);
    surface = client.surface('deck');
    container = document.createElement('div');
    document.body.appendChild(container);
    handle = null;
  });

  afterEach(() => {
    handle?.destroy();
    container.remove();
    delete (document as unknown as { hidden?: boolean }).hidden;
    jasmine.clock().uninstall();
  });

  it('opens a session for the referenced stream when shown', () => {
    mount();

    expect(port.last('OpenVideoStream').payload).toEqual({
      providerId: PROVIDER, streamId: 'front', acceptedTransports: ['mjpeg'],
    });
  });

  it('shows a placeholder and opens nothing without a stream', () => {
    const root = mount(node({}));

    expect(port.sent('OpenVideoStream')).toEqual([]);
    expect(root.textContent).toContain('Deck.VideoStream.NoStream');
  });

  it('draws only the placeholder where the reader has no video streams', () => {
    const root = mount(node(), host(false));

    expect(port.sent('OpenVideoStream')).toEqual([]);
    expect(root.getAttribute('data-status')).toBe('none');
  });

  it('plays the description the provider answered with', async () => {
    const root = mount();
    await answerOpen();

    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));

    const image = root.querySelector('img') as HTMLImageElement;
    expect(image.getAttribute('src')).toBe('http://camera/stream.mjpg');
    expect(root.getAttribute('data-status')).toBe('connecting');
  });

  it('closes its session when the view goes away', async () => {
    mount();
    await answerOpen();

    handle!.destroy();
    handle = null;

    expect(port.sent('CloseVideoStream').map(request => request.payload)).toEqual([{ sessionId: 's1' }]);
  });

  it('suspends while covered and stops downloading, and resumes when shown again', async () => {
    const root = mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));

    surface.setHidden('screensaver', true);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);

    expect(port.sent('SuspendVideoStream').length).toBe(1);
    expect((root.querySelector('img') as HTMLImageElement).hasAttribute('src')).toBeFalse();

    port.push('VideoStreamSessionChangedEvent', changed('s1', 2, 'suspended'));
    surface.setHidden('screensaver', false);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
    expect(port.sent('ResumeVideoStream').length).toBe(1);

    port.push('VideoStreamSessionChangedEvent', changed('s1', 3, 'active'));
    expect((root.querySelector('img') as HTMLImageElement).getAttribute('src')).toBe('http://camera/stream.mjpg');
  });

  it('changes nothing for a covering shorter than the debounce', async () => {
    const root = mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));

    surface.setHidden('screensaver', true);
    surface.setHidden('screensaver', false);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);

    expect(port.sent('SuspendVideoStream')).toEqual([]);
    expect((root.querySelector('img') as HTMLImageElement).getAttribute('src')).toBe('http://camera/stream.mjpg');
  });

  it('closes its session while the page is hidden and opens a new one when it is back', async () => {
    mount();
    await answerOpen();

    setDocumentHidden(true);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
    expect(port.sent('CloseVideoStream').length).toBe(1);

    setDocumentHidden(false);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
    expect(port.sent('OpenVideoStream').length).toBe(2);
  });

  it('opens again after the provider went away and came back', async () => {
    const root = mount();
    await answerOpen();

    port.push('VideoStreamSessionClosedEvent', { sessionId: 's1', reason: 'provider_removed' });
    expect(root.getAttribute('data-status')).toBe('unavailable');

    jasmine.clock().tick(VIDEO_RETRY_MIN_MS);
    expect(port.sent('OpenVideoStream').length).toBe(2);
  });

  it('waits for the catalog to change when the stream does not exist', async () => {
    const root = mount();
    port.last('OpenVideoStream').reject({ code: 'unknown_stream' });
    await flush();

    expect(root.getAttribute('data-status')).toBe('not-found');
    jasmine.clock().tick(60_000);
    expect(port.sent('OpenVideoStream').length).toBe(1);

    port.push('VideoStreamCatalogChangedEvent', {});
    await flush();
    expect(port.sent('OpenVideoStream').length).toBe(2);
  });

  it('says it cannot play a stream offered in no transport this reader plays', async () => {
    const root = mount();
    port.last('OpenVideoStream').reject({ code: 'transport_not_accepted' });
    await flush();

    expect(root.getAttribute('data-status')).toBe('unsupported');
    expect(root.textContent).toContain('Errors.VideoStream.TransportNotAccepted');
  });

  it('reserves the stream ratio before the first frame and frames the picture by fit', async () => {
    client.catalog();
    await flush();

    const contained = mount();
    const frame = contained.querySelector('.widget-video-stream-frame') as HTMLElement;
    expect(frame.style.height).toBe('112.5px');
    expect(frame.style.top).toBe('43.75px');

    handle!.destroy();
    const covered = mount(node({ stream: { provider: PROVIDER, id: 'front' }, fit: 'cover' }));
    const coveredFrame = covered.querySelector('.widget-video-stream-frame') as HTMLElement;
    expect(coveredFrame.style.height).toBe('200px');
    expect(coveredFrame.style.width).toBe('355.56px');
  });

  it('names a playing stream for assistive technology', async () => {
    client.catalog();
    await flush();
    const root = mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));
    const image = root.querySelector('img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { configurable: true, value: 640 });
    Object.defineProperty(image, 'naturalHeight', { configurable: true, value: 360 });
    jasmine.clock().tick(500);

    expect(root.getAttribute('role')).toBe('img');
    expect(root.getAttribute('aria-label')).toBe('Front door');
  });

  it('opens again once the connection to Macro Deck is back', async () => {
    mount();
    await answerOpen();

    port.setConnected(false);
    expect(port.sent('OpenVideoStream').length).toBe(1);

    port.setConnected(true);
    await flush();
    expect(port.sent('OpenVideoStream').length).toBe(2);
  });

  describe('with browser media APIs', () => {
    type Patched = { target: object; key: string; descriptor: PropertyDescriptor | undefined };
    const patched: Patched[] = [];

    function patch(target: object, key: string, value: unknown): void {
      patched.push({ target, key, descriptor: Object.getOwnPropertyDescriptor(target, key) });
      Object.defineProperty(target, key, { configurable: true, writable: true, value });
    }

    afterEach(() => {
      handle?.destroy();
      handle = null;
      for (const entry of patched.splice(0).reverse()) {
        if (entry.descriptor) Object.defineProperty(entry.target, entry.key, entry.descriptor);
        else delete (entry.target as Record<string, unknown>)[entry.key];
      }
    });

    it('opens nothing until the view is scrolled on screen', () => {
      let report: ((entries: Array<{ isIntersecting: boolean; intersectionRatio: number }>) => void) | null = null;
      patch(document.defaultView!, 'IntersectionObserver', class {
        constructor(callback: typeof report) { report = callback; }
        observe(): void {}
        disconnect(): void {}
      });

      mount();
      expect(port.sent('OpenVideoStream')).toEqual([]);

      report!([{ isIntersecting: true, intersectionRatio: 1 }]);
      jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
      expect(port.sent('OpenVideoStream').length).toBe(1);
    });

    it('falls back to the next transport when the preferred one does not play here', async () => {
      const video = document.defaultView!.HTMLVideoElement.prototype;
      patch(video, 'canPlayType', () => 'maybe');
      patch(video, 'playsInline', true);
      patch(document.defaultView!.HTMLMediaElement.prototype, 'play', () => Promise.reject(new Error('blocked')));
      patch(document.defaultView!.HTMLMediaElement.prototype, 'load', () => undefined);
      patch(document.defaultView!.HTMLMediaElement.prototype, 'pause', () => undefined);

      mount();
      expect(port.last('OpenVideoStream').payload['acceptedTransports']).toEqual(['hls', 'mjpeg']);
      await answerOpen();
      port.push('VideoStreamSessionChangedEvent', {
        sessionId: 's1', revision: 1, state: 'active', reason: 'none',
        description: { transport: 'hls', url: 'http://camera/index.m3u8' },
      });
      await flush();

      expect(port.sent('CloseVideoStream').length).toBe(1);
      jasmine.clock().tick(VIDEO_RETRY_MIN_MS);
      expect(port.last('OpenVideoStream').payload['acceptedTransports']).toEqual(['mjpeg']);
    });

    it('tries every transport again when the one left is refused after a playback failure', async () => {
      const video = document.defaultView!.HTMLVideoElement.prototype;
      patch(video, 'canPlayType', () => 'maybe');
      patch(video, 'playsInline', true);
      patch(document.defaultView!.HTMLMediaElement.prototype, 'play', () => Promise.reject(new Error('blocked')));
      patch(document.defaultView!.HTMLMediaElement.prototype, 'load', () => undefined);
      patch(document.defaultView!.HTMLMediaElement.prototype, 'pause', () => undefined);

      const root = mount();
      await answerOpen();
      port.push('VideoStreamSessionChangedEvent', {
        sessionId: 's1', revision: 1, state: 'active', reason: 'none',
        description: { transport: 'hls', url: 'http://camera/index.m3u8' },
      });
      await flush();
      jasmine.clock().tick(VIDEO_RETRY_MIN_MS);
      port.last('OpenVideoStream').reject({ code: 'transport_not_accepted' });
      await flush();

      expect(root.getAttribute('data-status')).not.toBe('unsupported');
      jasmine.clock().tick(VIDEO_RETRY_MAX_MS);
      expect(port.last('OpenVideoStream').payload['acceptedTransports']).toEqual(['hls', 'mjpeg']);
    });

    describe('over webrtc', () => {
      class FakePeer {
        static created: FakePeer[] = [];
        ontrack: unknown = null;
        onicecandidate: unknown = null;
        oniceconnectionstatechange: unknown = null;
        iceConnectionState = 'new';
        iceGatheringState = 'complete';
        localDescription: unknown = null;
        remote: { sdp?: string } | null = null;
        closed = false;
        constructor() { FakePeer.created.push(this); }
        setRemoteDescription(description: { sdp?: string }) { this.remote = description; return Promise.resolve(); }
        createAnswer() { return Promise.resolve({ type: 'answer', sdp: 'answer-sdp' }); }
        setLocalDescription(description: unknown) { this.localDescription = description; return Promise.resolve(); }
        addIceCandidate() { return Promise.resolve(); }
        addTransceiver() {}
        addEventListener() {}
        removeEventListener() {}
        close() { this.closed = true; }
      }

      function offer(revision: number, state = 'active', sdp = 'offer-1') {
        return {
          sessionId: 's1', revision, state, reason: 'none',
          description: { transport: 'webrtc', payload: sdp },
        };
      }

      beforeEach(() => {
        FakePeer.created = [];
        patch(document.defaultView!, 'RTCPeerConnection', FakePeer);
        patch(document.defaultView!, 'fetch', () => Promise.reject(new Error('offline')));
        patch(document.defaultView!.HTMLMediaElement.prototype, 'pause', () => undefined);
      });

      it('answers the provider offer through the session', async () => {
        mount();
        expect(port.last('OpenVideoStream').payload['acceptedTransports']).toEqual(['webrtc', 'whep', 'mjpeg']);
        await answerOpen();

        port.push('VideoStreamSessionChangedEvent', offer(1));
        await flush();

        expect(FakePeer.created[0].remote?.sdp).toBe('offer-1');
        expect(port.last('SignalVideoStream').payload).toEqual({
          sessionId: 's1', signal: { type: 'answer', payload: 'answer-sdp' },
        });
      });

      it('opens a new session when a resume brings back the offer it already answered', async () => {
        mount();
        await answerOpen();
        port.push('VideoStreamSessionChangedEvent', offer(1));
        await flush();

        surface.setHidden('screensaver', true);
        jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
        expect(FakePeer.created[0].closed).toBeTrue();
        port.push('VideoStreamSessionChangedEvent', offer(2, 'suspended'));

        surface.setHidden('screensaver', false);
        jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
        port.push('VideoStreamSessionChangedEvent', offer(3));

        expect(port.sent('CloseVideoStream').length).toBe(1);
        expect(port.sent('OpenVideoStream').length).toBe(2);
      });

      it('opens a new session when shown again after a suspend the host never applied', async () => {
        mount();
        await answerOpen();
        port.push('VideoStreamSessionChangedEvent', offer(1));
        await flush();

        surface.setHidden('screensaver', true);
        jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
        surface.setHidden('screensaver', false);
        jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);

        expect(port.sent('OpenVideoStream').length).toBe(2);
      });
    });
  });

  it('keeps working without asking again when the stream list cannot be read', async () => {
    port.catalogFails = true;
    mount();
    await flush();
    jasmine.clock().tick(1_000);
    await flush();

    expect(port.sent('GetVideoStreams').length).toBe(1);
    expect(port.sent('OpenVideoStream').length).toBe(1);
  });

  it('asks for no stream list on a client that shows no video', async () => {
    port.push('VideoStreamCatalogChangedEvent', {});
    port.setConnected(false);
    port.setConnected(true);
    await flush();

    expect(port.sent('GetVideoStreams')).toEqual([]);
  });

  it('opens a stream of a provider that lists no streams', async () => {
    port.catalog = [{ id: PROVIDER, name: 'Door', streams: [] }];
    client.catalog();
    await flush();

    mount();

    expect(port.sent('OpenVideoStream').length).toBe(1);
  });

  it('shows it is connecting again while a covered stream resumes', async () => {
    const root = mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));
    const image = root.querySelector('img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { configurable: true, value: 640 });
    Object.defineProperty(image, 'naturalHeight', { configurable: true, value: 360 });
    jasmine.clock().tick(500);
    expect(root.getAttribute('data-status')).toBe('playing');

    surface.setHidden('screensaver', true);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
    port.push('VideoStreamSessionChangedEvent', changed('s1', 2, 'suspended'));
    surface.setHidden('screensaver', false);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);

    expect(root.getAttribute('data-status')).toBe('connecting');
  });

  it('restarts the picture when a reconnecting stream is active again', async () => {
    const root = mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));
    const image = root.querySelector('img') as HTMLImageElement;

    port.push('VideoStreamSessionChangedEvent', { ...changed('s1', 2, 'reconnecting'), reason: 'source_lost' });
    expect(image.hasAttribute('src')).toBeFalse();
    expect(root.getAttribute('data-status')).toBe('connecting');

    port.push('VideoStreamSessionChangedEvent', { ...changed('s1', 3, 'active'), reason: 'source_recovered' });
    expect(image.getAttribute('src')).toBe('http://camera/stream.mjpg');
  });

  it('restarts the picture after a suspend the host applied only once the view was back', async () => {
    const root = mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));

    surface.setHidden('screensaver', true);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
    surface.setHidden('screensaver', false);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
    port.push('VideoStreamSessionChangedEvent', changed('s1', 2, 'suspended'));
    const image = root.querySelector('img') as HTMLImageElement;
    expect(image.hasAttribute('src')).toBeFalse();

    expect(port.sent('ResumeVideoStream').length).toBe(1);
    port.push('VideoStreamSessionChangedEvent', changed('s1', 3, 'active'));
    expect(image.getAttribute('src')).toBe('http://camera/stream.mjpg');
  });

  it('opens nothing for a view the layout gave no area', () => {
    handle = renderUiNode(container, node(), { width: 200, height: 0 }, null, 200, host());

    expect(port.sent('OpenVideoStream')).toEqual([]);

    handle.update(node(), { width: 200, height: 120 }, null, 200);
    expect(port.sent('OpenVideoStream').length).toBe(1);
  });

  it('tells assistive technology what the view is waiting for', async () => {
    client.catalog();
    await flush();

    const root = mount();

    expect(root.getAttribute('aria-label'))
      .toBe('Deck.VideoStream.AccessibleLabel(Front door / macrodeck.app:Deck.VideoStream.Connecting)');
  });

  it('reads the stream list again later when reading it after a change failed', async () => {
    const root = mount();
    port.last('OpenVideoStream').reject({ code: 'unknown_stream' });
    await flush();
    port.catalogFails = true;

    port.push('VideoStreamCatalogChangedEvent', {});
    await flush();
    expect(root.getAttribute('data-status')).toBe('not-found');

    port.catalogFails = false;
    jasmine.clock().tick(VIDEO_STREAM_CATALOG_RETRY_MS);
    await flush();
    expect(port.sent('OpenVideoStream').length).toBe(2);
  });

  it('gives its session up once it has been out of sight for a while, and opens a new one when shown', async () => {
    mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));

    surface.setHidden('screensaver', true);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS + VIDEO_OFF_SCREEN_CLOSE_MS);
    expect(port.sent('CloseVideoStream').length).toBe(1);

    surface.setHidden('screensaver', false);
    jasmine.clock().tick(VIDEO_VISIBILITY_DEBOUNCE_MS);
    expect(port.sent('OpenVideoStream').length).toBe(2);
  });

  it('says it is connecting when the host suspends a stream that is on screen', async () => {
    const root = mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));
    const image = root.querySelector('img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { configurable: true, value: 640 });
    Object.defineProperty(image, 'naturalHeight', { configurable: true, value: 360 });
    jasmine.clock().tick(500);

    port.push('VideoStreamSessionChangedEvent', changed('s1', 2, 'suspended'));

    expect(root.getAttribute('data-status')).toBe('connecting');
  });

  it('keeps the picture ratio when the stream changes resolution', async () => {
    const root = mount();
    await answerOpen();
    port.push('VideoStreamSessionChangedEvent', changed('s1', 1, 'active'));
    const image = root.querySelector('img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { configurable: true, value: 1600 });
    Object.defineProperty(image, 'naturalHeight', { configurable: true, value: 900 });
    jasmine.clock().tick(500);

    Object.defineProperty(image, 'naturalWidth', { configurable: true, value: 800 });
    Object.defineProperty(image, 'naturalHeight', { configurable: true, value: 800 });
    jasmine.clock().tick(500);

    const frame = root.querySelector('.widget-video-stream-frame') as HTMLElement;
    expect(frame.style.width).toBe('200px');
    expect(frame.style.height).toBe('200px');
  });

  it('says the source is not available while its provider is gone, and plays once it is back', async () => {
    const root = mount();
    await answerOpen();
    port.catalog = [];
    port.push('VideoStreamSessionClosedEvent', { sessionId: 's1', reason: 'provider_removed' });
    port.push('VideoStreamCatalogChangedEvent', {});
    await flush();
    jasmine.clock().tick(VIDEO_RETRY_MIN_MS);

    expect(root.getAttribute('data-status')).toBe('no-source');
    expect(root.textContent).toContain('Errors.VideoStream.UnknownProvider');
    expect(port.sent('OpenVideoStream').length).toBe(1);

    port.catalog = [{ id: PROVIDER, name: 'Door', streams: [
      { id: 'front', name: 'Front door', width: 1920, height: 1080, hasAudio: false, state: 'connected' },
    ] }];
    port.push('VideoStreamCatalogChangedEvent', {});
    await flush();
    expect(port.sent('OpenVideoStream').length).toBe(2);
  });

  it('drops a message from an earlier failure when it shows another state', async () => {
    const root = mount();
    port.last('OpenVideoStream').reject({ code: 'transport_not_accepted', message: 'refused by the provider' });
    await flush();
    port.catalog = [];
    port.push('VideoStreamCatalogChangedEvent', {});
    await flush();

    expect(root.textContent).not.toContain('refused by the provider');
  });

  it('draws the fallback on a reader that does not know the type', () => {
    const registry = createUiComponentRegistry(...UI_CORE_COMPONENTS);
    const tree = {
      ...node(),
      fallback: { id: 'fallback', type: 'ui.text', properties: { text: 'Update required' }, children: [] },
    } as UiNode;

    handle = renderUiNode(container, tree, { width: 200, height: 200 }, null, 200, host(), { registry });

    expect(container.textContent).toContain('Update required');
    expect(port.sent('OpenVideoStream')).toEqual([]);
  });
});
