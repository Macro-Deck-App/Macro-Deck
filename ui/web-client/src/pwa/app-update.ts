export type AppUpdatePhase =
  | 'unsupported' | 'idle' | 'checking' | 'upToDate'
  | 'available' | 'applying' | 'reloading' | 'checkFailed' | 'applyFailed';

export interface UpdateSource {
  readonly enabled: boolean;
  onVersionReady(listener: (version: string) => void): void;
  onUnrecoverable(listener: () => void): void;
  checkForUpdate(): Promise<boolean>;
  activateUpdate(): Promise<void>;
}

export interface AppUpdateOptions {
  reload?: () => void;
  storage?: Storage | null;
  document?: Document;
}

const RELOAD_ATTEMPTS_KEY = 'md.update.reloadAttempts';
const MAX_RELOAD_ATTEMPTS = 3;

// The client runs unattended on a tablet kiosk, so a ready update must never reload out from under an
// in-progress press: it waits for the document to go hidden (screen off / backgrounded) before
// activating, or reloads immediately if it is already hidden. activateNow is the explicit control
// for a device that never goes hidden on its own. Entirely inert when the source is not enabled.
export class AppUpdate {
  private currentPhase: AppUpdatePhase = 'idle';
  private readonly listeners: Array<(phase: AppUpdatePhase) => void> = [];

  private activating = false;
  private reloadedForUnrecoverable = false;
  private pendingHiddenListener: (() => void) | null = null;
  private visibilityListener: (() => void) | null = null;

  // Reload attempts are counted per target version, so a version that cannot be activated stops
  // being retried without blocking the next one that ships.
  private pendingVersion: string | null = null;

  private readonly doc: Document;
  private readonly storage: Storage | null;
  private readonly reloadPage: () => void;

  constructor(private readonly source: UpdateSource, options?: AppUpdateOptions) {
    const settings = options || {};
    this.doc = settings.document || document;
    this.storage = settings.storage !== undefined ? settings.storage : readableReloadBudgetStorage();
    this.reloadPage = settings.reload || function () { location.reload(); };

    if (!source.enabled) {
      this.currentPhase = 'unsupported';
      return;
    }

    const self = this;
    source.onVersionReady(function (version) { self.onVersionReady(version); });
    source.onUnrecoverable(function () { self.onUnrecoverable(); });

    this.visibilityListener = function () {
      if (!self.doc.hidden) void self.backgroundCheck();
    };
    this.doc.addEventListener('visibilitychange', this.visibilityListener);
  }

  phase(): AppUpdatePhase {
    return this.currentPhase;
  }

  updatePending(): boolean {
    return this.currentPhase === 'available';
  }

  onPhaseChange(listener: (phase: AppUpdatePhase) => void): void {
    this.listeners.push(listener);
  }

  check(): Promise<void> {
    if (!this.source.enabled) return Promise.resolve();
    this.setPhase('checking');
    const self = this;
    return this.source.checkForUpdate().then(
      function (hasUpdate) {
        // `hasUpdate === true` is deliberately not handled here: the version-ready signal is
        // authoritative and may already have moved the phase to `available` by the time this
        // resolves.
        if (!hasUpdate && self.currentPhase === 'checking') self.setPhase('upToDate');
      },
      function () {
        if (self.currentPhase === 'checking') self.setPhase('checkFailed');
      },
    );
  }

  // The reconnect and tab-visible triggers must never flip the row to checking - a kiosk tablet would
  // flicker on every screen wake - never downgrade an already available update, and never surface a
  // failure nobody asked to see: only an idle row is allowed to settle into upToDate.
  backgroundCheck(): Promise<void> {
    if (!this.source.enabled) return Promise.resolve();
    const self = this;
    return this.source.checkForUpdate().then(
      function (hasUpdate) {
        if (!hasUpdate && self.currentPhase === 'idle') self.setPhase('upToDate');
      },
      // a background check nobody asked for reports nothing
      function () {},
    );
  }

  activateNow(): Promise<void> {
    this.clearPendingHiddenListener();
    return this.activateAndReload();
  }

  destroy(): void {
    if (this.visibilityListener) {
      this.doc.removeEventListener('visibilitychange', this.visibilityListener);
      this.visibilityListener = null;
    }
    this.clearPendingHiddenListener();
  }

  private onVersionReady(version: string): void {
    this.pendingVersion = version;
    this.setPhase('available');

    if (this.doc.hidden) {
      void this.activateAndReload();
      return;
    }

    this.clearPendingHiddenListener();
    const self = this;
    const listener = function (): void {
      if (self.doc.hidden) {
        self.clearPendingHiddenListener();
        void self.activateAndReload();
      }
    };
    this.pendingHiddenListener = listener;
    this.doc.addEventListener('visibilitychange', listener);
  }

  private clearPendingHiddenListener(): void {
    if (this.pendingHiddenListener) {
      this.doc.removeEventListener('visibilitychange', this.pendingHiddenListener);
      this.pendingHiddenListener = null;
    }
  }

  private activateAndReload(): Promise<void> {
    if (this.activating) return Promise.resolve();
    this.activating = true;
    this.setPhase('applying');

    const self = this;
    return this.source.activateUpdate().then(
      function () { self.afterActivation(false); },
      function () { self.afterActivation(true); },
    );
  }

  private afterActivation(activationFailed: boolean): void {
    // Released on both outcomes, not just failure: a *successful* activation still falls through to
    // `reload()`, which can itself refuse once the budget for this target is exhausted - leaving the
    // page where it is, same as a failed activation does. Releasing the latch only on failure would
    // leave it stuck after that budget-exhausted success, silently dropping every later activation
    // attempt for the rest of the session.
    this.activating = false;

    // The reload still runs either way: even a rejected activation can leave the worker in a state
    // where reloading is the right recovery step, and the attempt budget keeps that from looping
    // forever. Only the reported phase changes.
    this.setPhase(activationFailed ? 'applyFailed' : 'reloading');
    this.reload();
  }

  private onUnrecoverable(): void {
    if (this.reloadedForUnrecoverable) return;
    this.reloadedForUnrecoverable = true;
    this.setPhase('reloading');
    this.reload();
  }

  // Each reload can raise the same condition again, so a version that never activates would reload an
  // unattended kiosk forever. Attempts are counted per target across reloads: a few still happen, then
  // the client stays on the version it has and reports applyFailed until a different one ships.
  private reload(): void {
    const target = this.pendingVersion === null ? 'unrecoverable' : this.pendingVersion;
    const storage = this.storage;
    if (storage) {
      try {
        const raw = storage.getItem(RELOAD_ATTEMPTS_KEY);
        const recorded = raw ? (JSON.parse(raw) as { target?: string; attempts?: number }) : null;
        const attempts = recorded && recorded.target === target && typeof recorded.attempts === 'number'
          ? recorded.attempts
          : 0;
        if (attempts >= MAX_RELOAD_ATTEMPTS) {
          this.setPhase('applyFailed');
          return;
        }
        storage.setItem(RELOAD_ATTEMPTS_KEY, JSON.stringify({ target: target, attempts: attempts + 1 }));
      } catch (error) {
        // Private browsing refuses storage; an uncounted reload is better than no reload at all.
      }
    }
    this.reloadPage();
  }

  private setPhase(phase: AppUpdatePhase): void {
    if (this.currentPhase === phase) return;
    this.currentPhase = phase;
    for (let index = 0; index < this.listeners.length; index++) {
      this.listeners[index](phase);
    }
  }
}

// Session storage, not local: the budget should expire with the tab. One that survived a power cycle
// would leave a kiosk that exhausted it stuck on the old version for good, with nobody to clear it.
function readableReloadBudgetStorage(): Storage | null {
  try {
    return window.sessionStorage;
  } catch (error) {
    return null;
  }
}
