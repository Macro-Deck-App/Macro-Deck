import { HostLockStateChangedEvent, HttpClient, ReadableStore, store } from '@macro-deck/runtime';

export class HostLock {
  private readonly stateStore = store<HostLockStateChangedEvent>({
    locked: false,
    lockScreenEnabled: false,
    supported: false,
  });

  private pushedDuringLoad = false;

  readonly state: ReadableStore<HostLockStateChangedEvent> = this.stateStore;

  constructor(private readonly http: HttpClient) {}

  showLockScreen(): boolean {
    const state = this.stateStore.get();
    return state.locked && state.lockScreenEnabled;
  }

  apply(state: HostLockStateChangedEvent): void {
    this.pushedDuringLoad = true;
    this.stateStore.set(state);
  }

  load(): Promise<void> {
    this.pushedDuringLoad = false;
    return this.http.get<HostLockStateChangedEvent>('/api/system/lock-state').then(
      state => {
        // A push that landed while this was in flight is newer than the snapshot answering it.
        // Applying the snapshot on top would roll the state back, and since it only moves on a lock
        // or an unlock, it would stay wrong until the next one - a deck left visible on a locked
        // machine.
        if (!this.pushedDuringLoad) this.stateStore.set(state);
      },
      () => undefined);
  }
}
