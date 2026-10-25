import { Injectable, WritableSignal, computed, inject, signal } from '@angular/core';
import { UserNotification, UserNotificationsChangedEvent } from '@macro-deck/runtime';
import { ApiService } from '@shared';

const MAX_BADGE_COUNT = 99;

interface Overlay {
  sequence: number;
  confirmed: boolean;
}

type Overlays = ReadonlyMap<string, Overlay>;

@Injectable({ providedIn: 'root' })
export class NotificationCenterService {
  private readonly api = inject(ApiService);

  private readonly _snapshot = signal<UserNotification[]>([]);
  private readonly _dismissing = signal<Overlays>(new Map());
  private readonly _reading = signal<Overlays>(new Map());
  private readonly _isLoading = signal(false);
  private readonly _hasLoaded = signal(false);

  readonly notifications = computed(() => {
    const dismissing = this._dismissing();
    const reading = this._reading();
    return this._snapshot()
      .filter(n => !masks(dismissing, n))
      .map(n => (n.read === false && masks(reading, n) ? { ...n, read: true } : n));
  });
  readonly isLoading = this._isLoading.asReadonly();
  readonly hasLoaded = this._hasLoaded.asReadonly();

  readonly count = computed(() => this.notifications().length);
  readonly hasNotifications = computed(() => this.count() > 0);

  readonly hasActiveProgress = computed(() => this.notifications().some(n => !!n.progress));

  readonly hasUnread = computed(() => this.notifications().some(n => n.read === false));

  readonly hasDismissable = computed(() => this.notifications().some(n => !n.progress));

  readonly badgeText = computed(() => {
    const count = this.count();
    return count > MAX_BADGE_COUNT ? `${MAX_BADGE_COUNT}+` : `${count}`;
  });

  private loading: Promise<void> | null = null;

  private generation = 0;

  constructor() {
    this.api
      .onNotification<UserNotificationsChangedEvent>('UserNotificationsChangedEvent')
      .subscribe(event => {
        this.generation++;
        this.applySnapshot(event.notifications ?? []);
      });

    this.api.connectionState$.subscribe(state => {
      if (state === 'connected') {
        void this.load();
      }
    });
  }

  async load(): Promise<void> {
    this.loading ??= this.fetch().finally(() => (this.loading = null));

    return this.loading;
  }

  dismiss(id: string): void {
    const entries = this.notifications().filter(n => n.id === id && !n.progress);
    if (entries.length > 0) {
      void this.settle(this._dismissing, this.mask(this._dismissing, entries), this.api.dismissNotification(id));
    }
  }

  dismissAll(): void {
    const entries = this.notifications().filter(n => !n.progress);
    if (entries.length > 0) {
      void this.settle(this._dismissing, this.mask(this._dismissing, entries), this.api.dismissAllNotifications());
    }
  }

  markRead(id: string): void {
    const entries = this.notifications().filter(n => n.id === id && n.read === false);
    if (entries.length > 0) {
      void this.settle(this._reading, this.mask(this._reading, entries), this.api.markNotificationRead(id));
    }
  }

  markAllRead(): void {
    const entries = this.notifications().filter(n => n.read === false);
    if (entries.length > 0) {
      void this.settle(this._reading, this.mask(this._reading, entries), this.api.markAllNotificationsRead());
    }
  }

  resolveLocally(matches: (notification: UserNotification) => boolean): void {
    const entries = this.notifications().filter(matches);
    if (entries.length > 0) {
      this.confirm(this._dismissing, this.mask(this._dismissing, entries));
    }
  }

  private mask(overlays: WritableSignal<Overlays>, entries: UserNotification[]): Overlays {
    const added = new Map(entries.map(n => [n.id, { sequence: n.sequence, confirmed: false }]));
    overlays.update(current => new Map([...current, ...added]));
    return added;
  }

  private async settle(
    overlays: WritableSignal<Overlays>,
    added: Overlays,
    request: Promise<{ success: boolean }>,
  ): Promise<void> {
    let success: boolean;
    try {
      success = !!(await request)?.success;
    } catch {
      this.unmask(overlays, added);
      await this.load();
      return;
    }

    if (success) {
      this.confirm(overlays, added);
      return;
    }

    // The host had nothing to change, so the entries are already in that state there. A fetch that
    // started before this answer can still carry them, hence a fresh one before the mask lifts.
    await this.fetch();
    this.unmask(overlays, added);
  }

  private confirm(overlays: WritableSignal<Overlays>, added: Overlays): void {
    overlays.update(current => {
      const next = new Map(current);
      for (const [id, overlay] of added) {
        if (current.get(id)?.sequence === overlay.sequence) {
          next.set(id, { ...overlay, confirmed: true });
        }
      }
      return next;
    });
    this.prune(this._snapshot());
  }

  private unmask(overlays: WritableSignal<Overlays>, added: Overlays): void {
    overlays.update(current => {
      const next = new Map(current);
      for (const [id, overlay] of added) {
        if (current.get(id)?.sequence === overlay.sequence) {
          next.delete(id);
        }
      }
      return next;
    });
  }

  private applySnapshot(notifications: UserNotification[]): void {
    this.prune(notifications);
    this._snapshot.set(notifications);
  }

  private prune(notifications: UserNotification[]): void {
    const byId = new Map(notifications.map(n => [n.id, n]));
    pruneOverlays(this._dismissing, byId, entry => !entry);
    pruneOverlays(this._reading, byId, entry => !entry || entry.read !== false);
  }

  private async fetch(): Promise<void> {
    this._isLoading.set(true);
    const gen = this.generation;
    try {
      const response = await this.api.getNotifications();
      if (gen === this.generation) {
        this.applySnapshot(response?.notifications ?? []);
      }
      this._hasLoaded.set(true);
    } catch {
      // The host drops its listeners while it restarts, so this call fails by design there. Every
      // caller fires and forgets, and an unhandled rejection would surface as a page error; the
      // reconnect that follows loads the list again.
    } finally {
      this._isLoading.set(false);
    }
  }
}

// A higher sequence on the same id is the host raising the entry again, which outranks the mask.
function masks(overlays: Overlays, notification: UserNotification): boolean {
  const overlay = overlays.get(notification.id);
  return !!overlay && notification.sequence <= overlay.sequence;
}

function pruneOverlays(
  overlays: WritableSignal<Overlays>,
  byId: ReadonlyMap<string, UserNotification>,
  reflected: (entry: UserNotification | undefined) => boolean,
): void {
  const current = overlays();
  const stale = [...current].filter(([id, overlay]) => {
    const entry = byId.get(id);
    return (entry && entry.sequence > overlay.sequence) || (overlay.confirmed && reflected(entry));
  });
  if (stale.length === 0) {
    return;
  }

  const next = new Map(current);
  for (const [id] of stale) {
    next.delete(id);
  }
  overlays.set(next);
}
