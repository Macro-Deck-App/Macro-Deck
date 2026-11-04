import { isSecureContext } from '@macro-deck/runtime';

export interface WakeLockHandle {
  release(): Promise<void>;
  onRelease(callback: () => void): void;
  onBlockedChange?(callback: (blocked: boolean) => void): void;
}

export interface WakeLockBackend {
  request(): Promise<WakeLockHandle>;
}

interface NavigatorWakeLockSentinel {
  release(): Promise<void>;
  addEventListener(type: 'release', listener: () => void): void;
}

interface NavigatorWakeLockApi {
  request(type: 'screen'): Promise<NavigatorWakeLockSentinel>;
}

export function browserWakeLockBackend(): WakeLockBackend | null {
  if (typeof navigator === 'undefined' || !isSecureContext()) return null;
  // `navigator.wakeLock` is not in every lib.dom snapshot, so it is reached through a narrow cast.
  const api = (navigator as unknown as { wakeLock?: NavigatorWakeLockApi }).wakeLock;
  if (!api) return null;

  return {
    request: () => api.request('screen').then(sentinel => ({
      release: () => sentinel.release(),
      onRelease: (callback: () => void) => sentinel.addEventListener('release', callback),
    })),
  };
}

const CLIPS: ReadonlyArray<{ src: string; type: string }> = [
  { src: './keep-awake.mp4', type: 'video/mp4; codecs="avc1.42E01E, mp4a.40.2"' },
  { src: './keep-awake.webm', type: 'video/webm; codecs="vp8, vorbis"' },
];

const GESTURES = ['pointerup', 'touchend', 'click', 'keydown'];

function supportsInlinePlayback(video: HTMLVideoElement): boolean {
  // Before iOS 10 an iPhone ignores playsinline and opens every video in the fullscreen player.
  return 'playsInline' in video || !/iPhone|iPod/.test(navigator.userAgent);
}

function playableClip(video: HTMLVideoElement): string | null {
  for (let index = 0; index < CLIPS.length; index++) {
    if (video.canPlayType(CLIPS[index].type) !== '') return CLIPS[index].src;
  }
  return null;
}

// NotAllowedError is the autoplay rule; AbortError is a pause, such as Alexa taking audio focus,
// landing before playback started. Both wait for the next tap.
function waitsForGesture(error: unknown): boolean {
  const name = typeof error === 'object' && error !== null ? (error as { name?: unknown }).name : undefined;
  return name === 'NotAllowedError' || name === 'AbortError';
}

export function mediaWakeLockBackend(
  createVideo: () => HTMLVideoElement = () => document.createElement('video'),
): WakeLockBackend | null {
  if (typeof document === 'undefined' || typeof navigator === 'undefined') return null;
  const probe = createVideo();
  if (typeof probe.canPlayType !== 'function' || !supportsInlinePlayback(probe)) return null;
  const src = playableClip(probe);
  if (src === null) return null;

  return { request: () => holdMedia(createVideo(), src) };
}

function holdMedia(video: HTMLVideoElement, src: string): Promise<WakeLockHandle> {
  return new Promise<WakeLockHandle>((resolve, reject) => {
    let settled = false;
    let stopped = false;
    let blocked = true;
    let armed = false;
    let releaseListener: (() => void) | null = null;
    let blockedListener: ((blocked: boolean) => void) | null = null;

    const handle: WakeLockHandle = {
      release: () => {
        stop();
        return Promise.resolve();
      },
      onRelease: callback => { releaseListener = callback; },
      onBlockedChange: callback => {
        blockedListener = callback;
        callback(blocked);
      },
    };

    const settle = (): void => {
      if (settled) return;
      settled = true;
      resolve(handle);
    };

    const setBlocked = (next: boolean): void => {
      if (stopped || blocked === next) return;
      blocked = next;
      setArmed(next);
      if (blockedListener !== null) blockedListener(next);
    };

    const attempt = (): void => {
      if (stopped) return;
      let result: Promise<void> | undefined;
      try {
        result = video.play() as Promise<void> | undefined;
      } catch {
        lose();
        return;
      }
      if (result === undefined || typeof result.then !== 'function') {
        // Engines older than the promise-returning play() report the outcome only through events.
        settle();
        setArmed(blocked);
        return;
      }
      result.then(
        () => {
          settle();
          setBlocked(false);
        },
        error => {
          if (stopped) return;
          if (!waitsForGesture(error)) {
            lose();
            return;
          }
          settle();
          setArmed(true);
        });
    };

    const lose = (): void => {
      if (stopped) return;
      stop();
      if (!settled) {
        settled = true;
        reject(new Error('media playback failed'));
        return;
      }
      if (releaseListener !== null) releaseListener();
    };

    const onPlaying = (): void => setBlocked(false);
    // Alexa speaking or another app taking audio focus pauses the clip; that is an interruption to
    // recover from on the next touch, not a refusal.
    const onInterrupted = (): void => {
      if (document.hidden) return;
      setBlocked(true);
    };
    const onVisibilityChange = (): void => {
      if (!document.hidden || stopped) return;
      stop();
      settle();
      if (releaseListener !== null) releaseListener();
    };

    function setArmed(on: boolean): void {
      if (armed === on || (on && stopped)) return;
      armed = on;
      for (let index = 0; index < GESTURES.length; index++) {
        if (on) document.addEventListener(GESTURES[index], attempt, true);
        else document.removeEventListener(GESTURES[index], attempt, true);
      }
    }

    function stop(): void {
      if (stopped) return;
      setArmed(false);
      stopped = true;
      video.removeEventListener('playing', onPlaying);
      video.removeEventListener('pause', onInterrupted);
      video.removeEventListener('ended', onInterrupted);
      video.removeEventListener('error', lose);
      document.removeEventListener('visibilitychange', onVisibilityChange);
      video.pause();
      if (video.parentNode !== null) video.parentNode.removeChild(video);
    }

    video.loop = true;
    video.muted = false;
    video.preload = 'auto';
    video.setAttribute('playsinline', '');
    video.setAttribute('webkit-playsinline', '');
    video.setAttribute('aria-hidden', 'true');
    video.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;opacity:0;pointer-events:none;';
    video.addEventListener('playing', onPlaying);
    video.addEventListener('pause', onInterrupted);
    video.addEventListener('ended', onInterrupted);
    video.addEventListener('error', lose);
    document.addEventListener('visibilitychange', onVisibilityChange);
    video.src = src;
    document.body.appendChild(video);
    attempt();
  });
}

// Silk closes an idle page whether or not the screen is held, so there the clip runs as well and is
// the part that has to hold; a refused or lost native lock does not end it.
function combinedWakeLockBackend(native: WakeLockBackend, media: WakeLockBackend): WakeLockBackend {
  return {
    request: () => {
      const nativeHandle = native.request().then(handle => handle, () => null);
      const releaseNative = (): void => {
        void nativeHandle.then(handle => {
          if (handle !== null) void handle.release();
        });
      };
      return media.request().then(
        mediaHandle => ({
          release: () => {
            releaseNative();
            return mediaHandle.release();
          },
          onRelease: (callback: () => void) => mediaHandle.onRelease(() => {
            releaseNative();
            callback();
          }),
          onBlockedChange: (callback: (blocked: boolean) => void) => {
            if (mediaHandle.onBlockedChange) mediaHandle.onBlockedChange(callback);
            else callback(false);
          },
        }),
        error => {
          releaseNative();
          throw error;
        });
    },
  };
}

function isSilk(): boolean {
  return typeof navigator !== 'undefined' && /\bSilk\//.test(navigator.userAgent);
}

export function preferredWakeLockBackend(
  native: WakeLockBackend | null = browserWakeLockBackend(),
  media: () => WakeLockBackend | null = mediaWakeLockBackend,
): WakeLockBackend | null {
  if (native !== null && !isSilk()) return native;
  const fallback = media();
  if (native === null) return fallback;
  return fallback === null ? native : combinedWakeLockBackend(native, fallback);
}

export type WakeLockStatus =
  | 'insecureOrigin'
  | 'unsupported'
  | 'off'
  | 'pending'
  | 'active'
  | 'awaitingGesture'
  | 'suspended'
  | 'denied';

export class WakeLock {
  private readonly storageKey: string;
  private status: WakeLockStatus;
  private enabledPreference: boolean;
  private gate = false;
  private forced = false;
  private pending = false;
  private sentinel: WakeLockHandle | null = null;
  private releasingHandle: WakeLockHandle | null = null;
  private readonly listeners: Array<() => void> = [];

  constructor(
    clientType: string,
    private readonly backend: WakeLockBackend | null = preferredWakeLockBackend(),
  ) {
    this.storageKey = `md.wake-lock.${clientType}.enabled`;
    this.enabledPreference = this.readPreference();
    this.status = this.initialStatus();

    if (typeof document !== 'undefined' && document.addEventListener) {
      document.addEventListener('visibilitychange', this.onVisibilityChange);
    }
  }

  dispose(): void {
    if (typeof document !== 'undefined' && document.removeEventListener) {
      document.removeEventListener('visibilitychange', this.onVisibilityChange);
    }
    this.releaseSentinel();
  }

  private readonly onVisibilityChange = (): void => {
    if (document.hidden) return;
    // Coming back to a visible document is the one moment a refusal is worth retrying: the platform
    // refuses a hidden document outright, which is not the user denying anything.
    if (this.status === 'denied') this.setStatus('off');
    this.runGuard();
  };

  currentStatus(): WakeLockStatus {
    return this.status;
  }

  enabled(): boolean {
    return this.enabledPreference;
  }

  onChange(listener: () => void): () => void {
    this.listeners.push(listener);
    return () => {
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners.splice(at, 1);
    };
  }

  setEnabled(on: boolean): void {
    if (this.enabledPreference === on) return;
    this.enabledPreference = on;
    this.writePreference(on);

    if (!on) {
      this.releaseSentinel();
      this.setIdleStatusIfOperable();
      this.notify();
      return;
    }
    this.runGuard();
    this.notify();
  }

  // Held while a screensaver shows whatever the preference says: a screensaver on a display the OS
  // then turns off is pointless. A refusal stays a refusal, the same as for the preference.
  setForced(on: boolean): void {
    if (this.forced === on) return;
    this.forced = on;

    if (!on && !this.enabledPreference) {
      this.releaseSentinel();
      this.setIdleStatusIfOperable();
      this.notify();
      return;
    }
    this.runGuard();
  }

  setGate(active: boolean): void {
    if (this.gate === active) return;
    this.gate = active;

    if (!active) {
      this.releaseSentinel();
      this.setIdleStatusIfOperable();
      this.notify();
      return;
    }
    this.runGuard();
  }

  private runGuard(): void {
    if (!this.canOperate() || !this.shouldAcquire()) return;

    const backend = this.backend as WakeLockBackend;
    this.pending = true;
    this.setStatus('pending');

    backend.request().then(
      handle => {
        this.pending = false;
        if (!this.shouldAcquire()) {
          // The world moved while the request was in flight - the deck was left, or the preference
          // was turned back off. Holding it anyway is how a kiosk keeps a screen awake for nobody.
          void handle.release();
          this.setIdleStatusIfOperable();
          return;
        }
        this.sentinel = handle;
        handle.onRelease(() => this.handleSentinelReleased(handle));
        if (handle.onBlockedChange) {
          handle.onBlockedChange(blocked => {
            if (this.sentinel === handle) this.setStatus(blocked ? 'awaitingGesture' : 'active');
          });
        } else {
          this.setStatus('active');
        }
      },
      () => {
        this.pending = false;
        this.setStatus('denied');
      });
  }

  private shouldAcquire(): boolean {
    return (this.enabledPreference || this.forced)
      && this.gate
      && !document.hidden
      && this.sentinel === null
      && !this.pending
      && this.status !== 'denied'
      && this.backend !== null;
  }

  private handleSentinelReleased(handle: WakeLockHandle): void {
    if (this.releasingHandle === handle) {
      this.releasingHandle = null;
      return;
    }
    if (this.sentinel !== handle) return;
    this.sentinel = null;
    // Never re-request from here. Only record why it is gone: a hidden document explains itself,
    // anything else reads as a refusal.
    this.setStatus(document.hidden ? 'suspended' : 'denied');
  }

  private releaseSentinel(): void {
    const handle = this.sentinel;
    if (handle === null) return;
    this.sentinel = null;
    this.releasingHandle = handle;
    void handle.release();
  }

  private setIdleStatusIfOperable(): void {
    // Guarded on the in-flight flag rather than on the status reading 'pending'. They differ in
    // exactly one case - a request that resolved into a closed gate, which clears the flag and then
    // asks for an idle status - and reading the status there left the row saying "acquiring" for
    // the rest of the session, with no lock held and nothing on its way.
    if (this.canOperate() && !this.pending && this.status !== 'denied') {
      this.setStatus('off');
    }
  }

  private canOperate(): boolean {
    return this.backend !== null;
  }

  private initialStatus(): WakeLockStatus {
    if (this.backend === null) return isSecureContext() ? 'unsupported' : 'insecureOrigin';
    return 'off';
  }

  private setStatus(status: WakeLockStatus): void {
    if (status === this.status) return;
    this.status = status;
    this.notify();
  }

  private notify(): void {
    for (let index = 0; index < this.listeners.length; index++) this.listeners[index]();
  }

  private readPreference(): boolean {
    try {
      return window.localStorage.getItem(this.storageKey) === '1';
    } catch {
      return false;
    }
  }

  private writePreference(value: boolean): void {
    try {
      if (value) window.localStorage.setItem(this.storageKey, '1');
      else window.localStorage.removeItem(this.storageKey);
    } catch {
      // Storage refused; the lock still works, the device just forgets the choice on reload.
    }
  }
}
