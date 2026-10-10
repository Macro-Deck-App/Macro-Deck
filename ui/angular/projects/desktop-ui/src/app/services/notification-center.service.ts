import { Injectable, computed, inject, signal } from '@angular/core';
import { UserNotification, UserNotificationsChangedEvent } from '@macro-deck/runtime';
import { ApiService } from '@shared';

const MAX_BADGE_COUNT = 99;

@Injectable({ providedIn: 'root' })
export class NotificationCenterService {
  private readonly api = inject(ApiService);

  private readonly _notifications = signal<UserNotification[]>([]);
  private readonly _isLoading = signal(false);
  private readonly _hasLoaded = signal(false);

  readonly notifications = this._notifications.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly hasLoaded = this._hasLoaded.asReadonly();

  readonly count = computed(() => this._notifications().length);
  readonly hasNotifications = computed(() => this.count() > 0);

  readonly hasActiveProgress = computed(() => this._notifications().some(n => !!n.progress));

  readonly hasUnread = computed(() => this._notifications().some(n => n.read === false));

  readonly hasDismissable = computed(() => this._notifications().some(n => !n.progress));

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
        this._notifications.set(event.notifications ?? []);
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
    void this.api.dismissNotification(id);
  }

  dismissAll(): void {
    void this.api.dismissAllNotifications();
  }

  markRead(id: string): void {
    this.setRead(n => n.id === id);
    void this.confirm(this.api.markNotificationRead(id));
  }

  markAllRead(): void {
    this.setRead(() => true);
    void this.confirm(this.api.markAllNotificationsRead());
  }

  private async confirm(request: Promise<{ success: boolean }>): Promise<void> {
    try {
      if (!(await request).success) {
        await this.load();
      }
    } catch {
      await this.load();
    }
  }

  private setRead(matches: (notification: UserNotification) => boolean): void {
    this.generation++;
    this._notifications.update(list =>
      list.map(n => (n.read === false && matches(n) ? { ...n, read: true } : n)));
  }

  private async fetch(): Promise<void> {
    this._isLoading.set(true);
    const gen = this.generation;
    try {
      const response = await this.api.getNotifications();
      if (gen === this.generation) {
        this._notifications.set(response?.notifications ?? []);
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
