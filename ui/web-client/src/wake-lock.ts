import { isSecureContext } from '@macro-deck/runtime';

export interface WakeLockHandle {
  release(): Promise<void>;
  onRelease(callback: () => void): void;
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
  if (typeof navigator === 'undefined') return null;
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

export type WakeLockStatus =
  | 'insecureOrigin'
  | 'unsupported'
  | 'off'
  | 'pending'
  | 'active'
  | 'suspended'
  | 'denied';

export class WakeLock {
  private readonly storageKey: string;
  private status: WakeLockStatus;
  private enabledPreference: boolean;
  private gate = false;
  private pending = false;
  private sentinel: WakeLockHandle | null = null;
  private releasingHandle: WakeLockHandle | null = null;
  private readonly listeners: Array<() => void> = [];

  constructor(
    clientType: string,
    private readonly backend: WakeLockBackend | null = browserWakeLockBackend(),
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
        this.setStatus('active');
        handle.onRelease(() => this.handleSentinelReleased(handle));
      },
      () => {
        this.pending = false;
        this.setStatus('denied');
      });
  }

  private shouldAcquire(): boolean {
    return this.enabledPreference
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
    return this.backend !== null && isSecureContext();
  }

  private initialStatus(): WakeLockStatus {
    if (!isSecureContext()) return 'insecureOrigin';
    if (this.backend === null) return 'unsupported';
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
