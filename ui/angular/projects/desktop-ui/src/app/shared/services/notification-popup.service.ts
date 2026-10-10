import { Injectable, computed, inject, signal } from '@angular/core';
import {
  ActiveNotification,
  ActiveNotificationsEvent,
  NotificationPopupStore,
  RespondToNotificationResponse,
} from '@macro-deck/runtime';

import { ApiService } from '../transport';

// setTimeout stores its delay in a signed 32-bit integer and fires immediately above it.
const MAX_TIMER_MS = 2_147_483_647;

@Injectable({ providedIn: 'root' })
export class NotificationPopupService {
  private readonly api = inject(ApiService);
  private readonly store = new NotificationPopupStore();
  private readonly items = signal<ActiveNotification[]>([]);

  readonly toasts = computed(() => this.items().filter(n => n.popup === 'toast'));

  readonly dialogs = computed(() => this.items().filter(n => n.popup === 'dialog'));

  readonly dialog = computed(() => this.dialogs()[0] ?? null);

  private expiryTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.store.subscribe(() => {
      this.items.set(this.store.list());
      this.scheduleExpiry();
    });

    this.api.onNotificationShown().subscribe(event => this.store.shown(event.notification));
    this.api.onNotificationUpdated().subscribe(event => this.store.updated(event.notification));
    this.api.onNotificationClosed().subscribe(event => this.store.closed(event));
    this.api.onActiveNotifications().subscribe(event => this.applySnapshot(event));

    this.api.connectionState$.subscribe(state => {
      if (state === 'connected') {
        void this.refresh();
      }
    });
  }

  async respond(
    id: string,
    actionId: string | null,
    values: Record<string, unknown> = {},
  ): Promise<RespondToNotificationResponse | null> {
    const response = await this.api.respondToNotification({ id, actionId, values });

    if (response?.status === 'already-closed' || response?.status === 'not-found') {
      this.store.closed({ id, revision: Number.MAX_SAFE_INTEGER, reason: 'programmatic' });
    }

    return response;
  }

  private async refresh(): Promise<void> {
    try {
      const snapshot = await this.api.getActiveNotifications();
      if (snapshot) {
        this.applySnapshot(snapshot);
      }
    } catch {
    }
  }

  private applySnapshot(event: ActiveNotificationsEvent): void {
    this.store.replace(event.notifications ?? []);
  }

  private scheduleExpiry(): void {
    if (this.expiryTimer !== null) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = null;
    }

    const next = this.store.nextExpiry();
    if (next === null) {
      return;
    }

    const delay = Math.min(Math.max(next - Date.now(), 0), MAX_TIMER_MS);
    this.expiryTimer = setTimeout(() => {
      this.expiryTimer = null;
      this.store.expireDue();
      this.scheduleExpiry();
    }, delay);
  }
}
