import { ReadableStore, store, WritableStore } from '@macro-deck/runtime';

export type AppScreen =
  | 'starting'
  | 'keyRingLocked'
  | 'setupRequired'
  | 'signedOut'
  | 'connecting'
  | 'deck';

export interface AppConditions {
  keyRingLocked: boolean;
  setupRequired: boolean;
  authenticated: boolean;
  connected: boolean;
  deckRendered: boolean;
  probed: boolean;
}

export const INITIAL_CONDITIONS: AppConditions = {
  keyRingLocked: false,
  setupRequired: false,
  authenticated: false,
  connected: false,
  deckRendered: false,
  probed: false,
};

export function screenFor(conditions: AppConditions): AppScreen {
  if (!conditions.probed) return 'starting';
  if (conditions.keyRingLocked) return 'keyRingLocked';
  if (conditions.setupRequired) return 'setupRequired';
  if (!conditions.authenticated) return 'signedOut';
  if (conditions.connected || conditions.deckRendered) return 'deck';
  return 'connecting';
}

export class AppState {
  private readonly conditionsStore: WritableStore<AppConditions> = store(INITIAL_CONDITIONS);
  private readonly screenStore: WritableStore<AppScreen> = store<AppScreen>('starting');

  get conditions(): ReadableStore<AppConditions> {
    return this.conditionsStore;
  }

  get screen(): ReadableStore<AppScreen> {
    return this.screenStore;
  }

  set(change: Partial<AppConditions>): void {
    const current = this.conditionsStore.get();
    let next = current;

    for (const key in change) {
      if (!Object.prototype.hasOwnProperty.call(change, key)) continue;
      const value = (change as Record<string, unknown>)[key];
      if ((current as unknown as Record<string, unknown>)[key] === value) continue;
      if (next === current) next = { ...current };
      (next as unknown as Record<string, unknown>)[key] = value;
    }

    if (next === current) return;
    this.conditionsStore.set(next);
    this.screenStore.set(screenFor(next));
  }
}
