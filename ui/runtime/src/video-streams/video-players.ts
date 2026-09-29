import { VideoStreamDescriptionMessage, VideoStreamSignalMessage } from '../protocol/messages/video-stream';

export const VideoStreamTransports = {
  WebRtc: 'webrtc',
  Whep: 'whep',
  Hls: 'hls',
  Mjpeg: 'mjpeg',
} as const;

export const VIDEO_FIRST_FRAME_TIMEOUT_MS = 15_000;
const FIRST_FRAME_POLL_MS = 250;
const ICE_GATHERING_LIMIT_MS = 2_000;

export interface VideoPlayerCallbacks {
  firstFrame(): void;

  resized(): void;

  failed(): void;

  sendSignal(signal: VideoStreamSignalMessage): void;
}

export interface VideoPlayer {
  readonly transport: string;

  readonly element: HTMLElement;

  start(description: VideoStreamDescriptionMessage): void;

  stop(): void;

  signal(signal: VideoStreamSignalMessage): void;

  playing(): boolean;

  intrinsicSize(): { width: number; height: number } | null;
}

export function playableTransports(doc: Document): string[] {
  const view = doc.defaultView as unknown as { RTCPeerConnection?: (new () => object) & { prototype: object }; fetch?: unknown } | null;
  const transports: string[] = [];

  const peer = view?.RTCPeerConnection;
  if (typeof peer === 'function' && 'addTransceiver' in peer.prototype && typeof view?.fetch === 'function') {
    transports.push(VideoStreamTransports.WebRtc, VideoStreamTransports.Whep);
  }

  const video = doc.createElement('video');
  // An engine that can decode HLS but predates muted inline autoplay would play it fullscreen or not at all.
  if (typeof video.canPlayType === 'function' && video.canPlayType('application/vnd.apple.mpegurl') !== ''
    && 'playsInline' in video) {
    transports.push(VideoStreamTransports.Hls);
  }

  transports.push(VideoStreamTransports.Mjpeg);
  return transports;
}

export function createVideoPlayer(doc: Document, transport: string, callbacks: VideoPlayerCallbacks): VideoPlayer | null {
  switch (transport) {
    case VideoStreamTransports.Mjpeg: return new ImagePlayer(doc, callbacks);
    case VideoStreamTransports.Hls: return new NativeVideoPlayer(doc, callbacks);
    case VideoStreamTransports.Whep: return new PeerPlayer(doc, callbacks, true);
    case VideoStreamTransports.WebRtc: return new PeerPlayer(doc, callbacks, false);
    default: return null;
  }
}

abstract class PlayerBase {
  private generation = 0;
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private seen = false;

  protected constructor(protected readonly callbacks: VideoPlayerCallbacks) {}

  abstract intrinsicSize(): { width: number; height: number } | null;

  playing(): boolean {
    return this.generation % 2 === 1;
  }

  protected begin(): number {
    this.halt();
    this.generation++;
    this.seen = false;
    const generation = this.generation;
    const started = Date.now();
    let lastSize = '';
    this.pollTimer = setInterval(() => {
      if (generation !== this.generation) return;
      const size = this.intrinsicSize();
      if (size !== null) {
        const key = `${size.width}x${size.height}`;
        if (!this.seen) {
          this.seen = true;
          lastSize = key;
          this.callbacks.firstFrame();
        } else if (key !== lastSize) {
          lastSize = key;
          this.callbacks.resized();
        }
      } else if (!this.seen && Date.now() - started >= VIDEO_FIRST_FRAME_TIMEOUT_MS) {
        this.fail(generation);
      }
    }, FIRST_FRAME_POLL_MS);
    return generation;
  }

  protected halt(): void {
    this.stopPolling();
    if (this.playing()) this.generation++;
  }

  protected current(generation: number): boolean {
    return generation === this.generation && this.playing();
  }

  protected fail(generation: number): void {
    if (!this.current(generation)) return;
    this.halt();
    this.callbacks.failed();
  }

  private stopPolling(): void {
    if (this.pollTimer === null) return;
    clearInterval(this.pollTimer);
    this.pollTimer = null;
  }
}

class ImagePlayer extends PlayerBase implements VideoPlayer {
  readonly transport = VideoStreamTransports.Mjpeg;
  readonly element: HTMLImageElement;
  private generationAtStart = 0;

  constructor(doc: Document, callbacks: VideoPlayerCallbacks) {
    super(callbacks);
    this.element = doc.createElement('img');
    this.element.setAttribute('alt', '');
    this.element.setAttribute('draggable', 'false');
    this.element.addEventListener('error', () => this.fail(this.generationAtStart));
  }

  start(description: VideoStreamDescriptionMessage): void {
    if (!description.url) {
      this.callbacks.failed();
      return;
    }
    this.generationAtStart = this.begin();
    this.element.src = description.url;
  }

  stop(): void {
    this.halt();
    this.element.removeAttribute('src');
  }

  signal(): void {}

  intrinsicSize(): { width: number; height: number } | null {
    return this.element.naturalWidth > 0 && this.element.naturalHeight > 0
      ? { width: this.element.naturalWidth, height: this.element.naturalHeight }
      : null;
  }
}

function mutedInlineVideo(doc: Document): HTMLVideoElement {
  const video = doc.createElement('video');
  video.muted = true;
  video.autoplay = true;
  video.setAttribute('muted', '');
  video.setAttribute('playsinline', '');
  video.setAttribute('webkit-playsinline', '');
  video.setAttribute('disablepictureinpicture', '');
  return video;
}

function videoSize(video: HTMLVideoElement): { width: number; height: number } | null {
  return video.videoWidth > 0 && video.videoHeight > 0 ? { width: video.videoWidth, height: video.videoHeight } : null;
}

class NativeVideoPlayer extends PlayerBase implements VideoPlayer {
  readonly transport = VideoStreamTransports.Hls;
  readonly element: HTMLVideoElement;
  private generationAtStart = 0;

  constructor(doc: Document, callbacks: VideoPlayerCallbacks) {
    super(callbacks);
    this.element = mutedInlineVideo(doc);
    this.element.addEventListener('error', () => this.fail(this.generationAtStart));
  }

  start(description: VideoStreamDescriptionMessage): void {
    if (!description.url) {
      this.callbacks.failed();
      return;
    }
    const generation = this.generationAtStart = this.begin();
    this.element.src = description.url;
    playOrFail(this.element, () => this.fail(generation));
  }

  stop(): void {
    this.halt();
    this.element.pause();
    this.element.removeAttribute('src');
    this.element.load();
  }

  signal(): void {}

  intrinsicSize(): { width: number; height: number } | null {
    return videoSize(this.element);
  }
}

function playOrFail(video: HTMLVideoElement, fail: () => void): void {
  const playing = video.play() as Promise<void> | undefined;
  if (playing && typeof playing.catch === 'function') playing.catch(fail);
}

class PeerPlayer extends PlayerBase implements VideoPlayer {
  readonly transport: string;
  readonly element: HTMLVideoElement;
  private peer: RTCPeerConnection | null = null;
  private resource: string | null = null;
  private authorization: string | null = null;
  private remoteReady: Promise<void> = Promise.resolve();

  constructor(private readonly doc: Document, callbacks: VideoPlayerCallbacks, private readonly whep: boolean) {
    super(callbacks);
    this.transport = whep ? VideoStreamTransports.Whep : VideoStreamTransports.WebRtc;
    this.element = mutedInlineVideo(doc);
  }

  start(description: VideoStreamDescriptionMessage): void {
    const generation = this.begin();
    this.closePeer();

    const view = this.doc.defaultView as Window & { RTCPeerConnection: typeof RTCPeerConnection };
    let peer: RTCPeerConnection;
    try {
      peer = new view.RTCPeerConnection();
    } catch {
      this.fail(generation);
      return;
    }
    this.peer = peer;
    peer.ontrack = event => {
      if (!this.current(generation)) return;
      const stream = event.streams && event.streams[0] ? event.streams[0] : new MediaStream([event.track]);
      if (this.element.srcObject !== stream) this.element.srcObject = stream;
      playOrFail(this.element, () => this.fail(generation));
    };
    peer.oniceconnectionstatechange = () => {
      if (peer.iceConnectionState === 'failed') this.fail(generation);
    };

    const negotiated = this.whep ? this.negotiateWhep(peer, description, generation) : this.answerOffer(peer, description, generation);
    negotiated.catch(() => this.fail(generation));
  }

  stop(): void {
    this.halt();
    this.closePeer();
  }

  signal(signal: VideoStreamSignalMessage): void {
    const peer = this.peer;
    if (!peer || this.whep || signal.type !== 'candidate') return;
    let candidate: RTCIceCandidateInit;
    try {
      candidate = JSON.parse(signal.payload) as RTCIceCandidateInit;
    } catch {
      return;
    }
    this.remoteReady = this.remoteReady.then(() => peer.addIceCandidate(candidate)).catch(() => undefined);
  }

  intrinsicSize(): { width: number; height: number } | null {
    return videoSize(this.element);
  }

  private async answerOffer(peer: RTCPeerConnection, description: VideoStreamDescriptionMessage, generation: number): Promise<void> {
    if (!description.payload) throw new Error('No offer.');
    peer.onicecandidate = event => {
      if (event.candidate && this.current(generation)) {
        this.callbacks.sendSignal({ type: 'candidate', payload: JSON.stringify(event.candidate.toJSON()) });
      }
    };
    const remote = peer.setRemoteDescription({ type: 'offer', sdp: description.payload });
    this.remoteReady = remote.catch(() => undefined);
    await remote;
    const answer = await peer.createAnswer();
    await peer.setLocalDescription(answer);
    if (this.current(generation)) this.callbacks.sendSignal({ type: 'answer', payload: answer.sdp ?? '' });
  }

  private async negotiateWhep(peer: RTCPeerConnection, description: VideoStreamDescriptionMessage, generation: number): Promise<void> {
    if (!description.url) throw new Error('No endpoint.');
    peer.addTransceiver('video', { direction: 'recvonly' });
    peer.addTransceiver('audio', { direction: 'recvonly' });
    await peer.setLocalDescription(await peer.createOffer());
    await gatheringComplete(peer);
    if (!this.current(generation)) return;

    const view = this.doc.defaultView!;
    const headers: Record<string, string> = { 'Content-Type': 'application/sdp' };
    this.authorization = description.parameters?.['authorization'] ?? null;
    if (this.authorization) headers['Authorization'] = this.authorization;
    const response = await view.fetch(description.url, { method: 'POST', headers, body: peer.localDescription?.sdp ?? '' });
    if (!response.ok) throw new Error('The WHEP endpoint refused the offer.');
    const location = response.headers.get('Location');
    const resource = location ? new URL(location, description.url).toString() : null;
    const answer = await response.text();
    if (!this.current(generation)) {
      this.deleteResource(resource);
      return;
    }
    this.resource = resource;
    await peer.setRemoteDescription({ type: 'answer', sdp: answer });
  }

  private closePeer(): void {
    if (this.peer) {
      this.peer.ontrack = null;
      this.peer.onicecandidate = null;
      this.peer.oniceconnectionstatechange = null;
      this.peer.close();
      this.peer = null;
    }
    this.element.pause();
    this.element.srcObject = null;
    this.deleteResource();
  }

  private deleteResource(resource = this.resource): void {
    if (resource === null) return;
    if (resource === this.resource) this.resource = null;
    const headers: Record<string, string> = {};
    if (this.authorization) headers['Authorization'] = this.authorization;
    void this.doc.defaultView!.fetch(resource, { method: 'DELETE', headers }).catch(() => undefined);
  }
}

function gatheringComplete(peer: RTCPeerConnection): Promise<void> {
  if (peer.iceGatheringState === 'complete') return Promise.resolve();
  return new Promise(resolve => {
    const done = () => {
      peer.removeEventListener('icegatheringstatechange', check);
      clearTimeout(timer);
      resolve();
    };
    const check = () => {
      if (peer.iceGatheringState === 'complete') done();
    };
    const timer = setTimeout(done, ICE_GATHERING_LIMIT_MS);
    peer.addEventListener('icegatheringstatechange', check);
  });
}
