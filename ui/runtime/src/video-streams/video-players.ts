import { VideoStreamDescriptionMessage } from '../protocol/messages/video-stream';

export const VideoStreamTransports = {
  Hls: 'hls',
  Mjpeg: 'mjpeg',
} as const;

export const VIDEO_FIRST_FRAME_TIMEOUT_MS = 15_000;
const FIRST_FRAME_POLL_MS = 250;

export interface VideoPlayerCallbacks {
  firstFrame(): void;

  resized(): void;

  failed(): void;
}

export interface VideoPlayer {
  readonly transport: string;

  readonly element: HTMLElement;

  start(description: VideoStreamDescriptionMessage): void;

  stop(): void;

  playing(): boolean;

  intrinsicSize(): { width: number; height: number } | null;
}

export function playableTransports(doc: Document): string[] {
  const transports: string[] = [];

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

  intrinsicSize(): { width: number; height: number } | null {
    return videoSize(this.element);
  }
}

function playOrFail(video: HTMLVideoElement, fail: () => void): void {
  const playing = video.play() as Promise<void> | undefined;
  if (playing && typeof playing.catch === 'function') playing.catch(fail);
}
