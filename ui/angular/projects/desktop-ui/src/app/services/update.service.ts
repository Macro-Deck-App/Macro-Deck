import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { ApiService } from '@shared';

// A Tauri command rejects with whatever string the Rust side returned, so an install failure can carry
// an explanation worth showing verbatim; anything else has no user-facing text.
export function installErrorMessage(error: unknown): string | null {
  const message = typeof error === 'string' ? error : null;
  return message && message.trim().length > 0 ? message.trim() : null;
}

const IDLE_STATE: ShellUpdateState = {
  phase: 'idle',
  supported: false,
  currentVersion: '',
  version: null,
  notes: null,
  publishedAt: null,
  channel: null,
  betaInstalled: false,
  installStrategy: 'inApp',
  downloadUrl: null,
  partialCheck: null,
  error: null,
  progress: null,
  lastCheckedAt: null,
};

@Injectable({ providedIn: 'root' })
export class UpdateService {
  private readonly api = inject(ApiService);

  private readonly state = signal<ShellUpdateState>(IDLE_STATE);

  private readonly installAttempted = signal(false);

  readonly hasBridge = typeof window.macroDeckShell !== 'undefined';

  private installInFlight: Promise<void> | null = null;
  private readonly installRequested = signal(false);

  readonly phase = computed(() => this.state().phase);
  readonly supported = computed(() => this.state().supported);
  readonly currentVersion = computed(() => this.state().currentVersion);
  readonly version = computed(() => this.state().version);
  readonly notes = computed(() => this.state().notes);
  readonly publishedAt = computed(() => this.state().publishedAt);
  readonly channel = computed(() => this.state().channel);
  readonly betaInstalled = computed(() => this.state().betaInstalled);
  readonly installStrategy = computed(() => this.state().installStrategy);
  readonly downloadUrl = computed(() => this.state().downloadUrl);
  readonly partialCheck = computed(() => this.state().partialCheck);
  readonly error = computed(() => this.state().error);
  readonly progressPercent = computed(() => this.state().progress?.percent ?? null);
  readonly lastCheckedAt = computed(() => this.state().lastCheckedAt);

  readonly isDownloading = computed(() => this.phase() === 'downloading' || this.installRequested());
  readonly canInstall = computed(() => this.phase() === 'available' || this.phase() === 'downloaded');
  readonly externalDownload = computed(() => this.installStrategy() === 'externalDownload');
  readonly installFailed = computed(() => this.phase() === 'failed' && this.installAttempted());

  constructor() {
    const bridge = window.macroDeckShell;

    if (typeof bridge?.getUpdateState === 'function') {
      bridge
        .getUpdateState()
        .then(state => this.applyState(state))
        .catch(() => {
        });
    }

    // Resolves to an unlisten function that is deliberately never called: this
    // service outlives every view that could own tearing it down (see the
    // class doc), so there is nothing meaningful to unsubscribe from.
    if (typeof bridge?.onUpdateState === 'function') {
      bridge
        .onUpdateState(state => this.applyState(state))
        .catch(() => {
        });
    }

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        void this.requestCheck();
      }
    });
  }

  private applyState(state: ShellUpdateState): void {
    if (state.phase === 'installing') {
      this.installAttempted.set(true);
    }
    this.state.set(state);
  }

  async check(): Promise<void> {
    const bridge = window.macroDeckShell;
    if (typeof bridge?.checkForUpdate !== 'function') {
      return;
    }
    try {
      await bridge.checkForUpdate();
    } catch {
    }
  }

  private async requestCheck(): Promise<void> {
    const bridge = window.macroDeckShell;
    if (typeof bridge?.requestUpdateCheck !== 'function') {
      return;
    }
    try {
      await bridge.requestUpdateCheck();
    } catch {
    }
  }

  async install(): Promise<void> {
    if (this.installInFlight) {
      return this.installInFlight;
    }
    const bridge = window.macroDeckShell;
    if (typeof bridge?.installUpdate !== 'function') {
      return;
    }
    this.installRequested.set(true);
    this.installAttempted.set(true);
    const promise = bridge
      .installUpdate()
      .catch((error: unknown) => {
        this.state.update(current => ({
          ...current,
          phase: 'failed',
          error: installErrorMessage(error),
        }));
      })
      .finally(() => {
        this.installInFlight = null;
        this.installRequested.set(false);
      });
    this.installInFlight = promise;
    return promise;
  }

  async cancelDownload(): Promise<void> {
    const bridge = window.macroDeckShell;
    if (typeof bridge?.cancelUpdateDownload !== 'function') {
      return;
    }
    try {
      await bridge.cancelUpdateDownload();
      this.installRequested.set(false);
      this.installInFlight = null;
      // The shell pushes its own onUpdateState once cancellation lands, but that
      // is a round-trip away - clearing the progress here rather than waiting
      // for it is what makes the cancel button feel like it did something.
      this.state.update(current => ({ ...current, phase: 'available', progress: null }));
    } catch {
    }
  }

  openDownloadPage(): void {
    const url = this.downloadUrl();
    const openExternal = window.macroDeckShell?.openExternal;
    if (url && typeof openExternal === 'function') {
      void openExternal(url);
    }
  }

  async setChannel(channel: 'stable' | 'beta'): Promise<ShellUpdateChannelStatus | null> {
    const bridge = window.macroDeckShell;
    if (typeof bridge?.setUpdateChannel !== 'function') {
      return null;
    }
    const status = await bridge.setUpdateChannel(channel);
    void this.check();
    return status;
  }

  async setMode(mode: 'off' | 'notifyOnly' | 'automatic'): Promise<ShellUpdateModeStatus | null> {
    const bridge = window.macroDeckShell;
    if (typeof bridge?.setUpdateMode !== 'function') {
      return null;
    }
    return bridge.setUpdateMode(mode);
  }

  async getModeStatus(): Promise<ShellUpdateModeStatus | null> {
    const bridge = window.macroDeckShell;
    if (typeof bridge?.getUpdateMode !== 'function') {
      return null;
    }
    try {
      return await bridge.getUpdateMode();
    } catch {
      return null;
    }
  }
}
