import { Injectable, WritableSignal, inject, signal, untracked } from '@angular/core';
import { MusicPlayerInstanceDto, MusicPlayerInstancesChangedNotification, MusicPlayerStateChangedNotification, MusicPlayerStatePayload } from '@macro-deck/runtime';
import { ApiService } from '@shared';

export const DISCONNECTED_STATE: MusicPlayerStatePayload = {
  isConnected: false,
  playbackState: 'stopped',
  isPlaying: false,
  shuffleEnabled: false,
  repeatMode: 'off',
};

type InstancesFetchOutcome = 'applied' | 'superseded' | 'failed';

const INSTANCES_RETRY_INITIAL_MS = 1000;
const INSTANCES_RETRY_MAX_MS = 8000;
const INSTANCES_RETRY_ATTEMPTS = 5;

@Injectable({ providedIn: 'root' })
export class MusicPlayerService {
  private readonly api = inject(ApiService);

  readonly instances = signal<MusicPlayerInstanceDto[]>([]);

  private readonly states = new Map<string, WritableSignal<MusicPlayerStatePayload>>();
  readonly knownInstanceIds = signal<ReadonlySet<string>>(new Set());

  private started = false;

  private readonly pulled = new Set<string>();
  private readonly healed = new Set<string>();
  private loadingInstances: Promise<InstancesFetchOutcome> | null = null;

  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryDelayMs = INSTANCES_RETRY_INITIAL_MS;
  private retryAttemptsLeft = INSTANCES_RETRY_ATTEMPTS;

  private instancesGeneration = 0;

  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    this.api
      .onNotification<MusicPlayerStateChangedNotification>('MusicPlayerStateChangedNotification')
      .subscribe(notification => this.applyState(notification.state));

    this.api
      .onNotification<MusicPlayerInstancesChangedNotification>('MusicPlayerInstancesChangedNotification')
      .subscribe(notification => this.setInstances(notification.instances ?? []));

    this.api.connectionState$.subscribe(state => {
      if (state === 'connected') {
        void this.resync();
      }
    });
  }

  async loadInstances(): Promise<InstancesFetchOutcome> {
    // Single-flighted: several widgets mounting at once (or a burst of state pushes) must not each
    // issue their own hub invoke.
    this.loadingInstances ??= this.fetchInstances().finally(() => (this.loadingInstances = null));
    return this.loadingInstances;
  }

  async ensureState(instanceId: string): Promise<void> {
    if (this.pulled.has(instanceId)) {
      return;
    }
    this.pulled.add(instanceId);

    const response = await this.api.getMusicPlayerState(instanceId);
    if (response?.state) {
      this.applyState(response.state);
      return;
    }

    this.pulled.delete(instanceId);
  }

  stateFor(instanceId: string | undefined | null): MusicPlayerStatePayload {
    if (!instanceId) {
      return DISCONNECTED_STATE;
    }
    const state = this.states.get(instanceId);
    if (state) {
      return state();
    }

    // A miss still has to take a dependency, or a caller's computed would cache "disconnected" with
    // nothing that can ever invalidate it - the per-instance signal this read wanted does not exist
    // yet, and creating it here would be a signal write inside that computed (NG0600). knownInstanceIds
    // is bumped exactly when one is created, so reading it makes the first state arrival re-evaluate
    // the caller, which then finds the signal and tracks it from there on (issue #706).
    this.knownInstanceIds();
    return DISCONNECTED_STATE;
  }

  instance(instanceId: string | undefined | null): MusicPlayerInstanceDto | undefined {
    return instanceId ? this.instances().find(i => i.instanceId === instanceId) : undefined;
  }

  artworkUrl(instanceId: string | undefined | null, artworkId?: string | null, size?: number): string | null {
    return instanceId && artworkId ? this.api.getMusicPlayerArtworkUrl(instanceId, artworkId, size) : null;
  }

  integrationIconUrl(integrationId: string | undefined | null): string | null {
    return integrationId ? this.api.getIntegrationIconUrl(integrationId) : null;
  }

  private async resync(): Promise<void> {
    if (this.retryTimer !== null) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
    this.retryDelayMs = INSTANCES_RETRY_INITIAL_MS;
    this.retryAttemptsLeft = INSTANCES_RETRY_ATTEMPTS;
    this.pulled.clear();
    this.healed.clear();
    await this.loadInstances();

    const ids = new Set([...this.instances().map(i => i.instanceId), ...this.states.keys()]);
    await Promise.all([...ids].map(id => this.ensureState(id)));
  }

  private async fetchInstances(): Promise<InstancesFetchOutcome> {
    const generation = this.instancesGeneration;
    const response = await this.api.getMusicPlayerInstances();
    // A null result means the realtime connection wasn't ready or the request failed (e.g. mid-reconnect) - that is
    // "unknown", not "no providers". Keep the previous list so the widget doesn't flip to
    // "Not connected" with no way back (the same distinction the Weather service makes, issue #94),
    // and retry shortly: a single failed pull used to strand the widget until the next reconnect.
    if (!response) {
      this.scheduleInstancesRetry();
      return 'failed';
    }

    if (generation !== this.instancesGeneration) {
      // A push replaced the list while this pull was in flight; it is the fresher answer. The hub
      // dispatches a completion and an invocation from one frame in either order, so an older pull
      // really can resolve last (issue #132).
      return 'superseded';
    }

    this.setInstances(response.instances ?? []);
    this.retryDelayMs = INSTANCES_RETRY_INITIAL_MS;
    this.retryAttemptsLeft = INSTANCES_RETRY_ATTEMPTS;
    return 'applied';
  }

  private scheduleInstancesRetry(): void {
    if (this.retryTimer !== null || this.retryAttemptsLeft <= 0) {
      return;
    }
    this.retryAttemptsLeft--;
    const generation = this.instancesGeneration;
    const delay = this.retryDelayMs;
    this.retryDelayMs = Math.min(this.retryDelayMs * 2, INSTANCES_RETRY_MAX_MS);
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      if (generation !== this.instancesGeneration) {
        return;
      }
      void this.loadInstances();
    }, delay);
  }

  private setInstances(instances: MusicPlayerInstanceDto[]): void {
    this.instancesGeneration++;
    this.instances.set(instances);
    for (const instance of instances) {
      this.healed.delete(instance.instanceId);
    }
  }

  private applyState(state: MusicPlayerStatePayload | undefined): void {
    if (!state?.instanceId) {
      return;
    }
    const instanceId = state.instanceId;
    this.stateSignal(instanceId).set(state);
    this.pulled.add(instanceId);

    if (!this.healed.has(instanceId) && !this.instances().some(i => i.instanceId === instanceId)) {
      void this.healInstances(instanceId);
    }
  }

  private stateSignal(instanceId: string): WritableSignal<MusicPlayerStatePayload> {
    let state = this.states.get(instanceId);
    if (!state) {
      state = signal<MusicPlayerStatePayload>(DISCONNECTED_STATE);
      this.states.set(instanceId, state);
      // May run while a caller's computed is reading applyState indirectly (e.g. via ensureState
      // awaited from an effect) - writing knownInstanceIds must not be attributed to that read.
      untracked(() => this.knownInstanceIds.update(ids => new Set(ids).add(instanceId)));
    }
    return state;
  }

  private async healInstances(instanceId: string): Promise<void> {
    this.healed.add(instanceId);
    const outcome = await this.loadInstances();
    if (outcome === 'failed') {
      this.healed.delete(instanceId);
    }
  }
}
