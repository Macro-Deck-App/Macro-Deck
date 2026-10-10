import {
  NotificationPopupStore,
  type ActiveNotification,
  type ActiveNotificationsEvent,
  type NotificationClosedEvent,
  type RespondToNotificationRequest,
  type RespondToNotificationResponse,
} from '@macro-deck/runtime';

const MAX_TIMER_MS = 2147483647;

export type NotificationRequest = (type: string, payload: unknown) => Promise<unknown>;

export class NotificationFeed {
  readonly store: NotificationPopupStore;

  private clock: () => number = () => Date.now();
  private timer: ReturnType<typeof setTimeout> | null = null;
  private pending: { [id: string]: Promise<RespondToNotificationResponse | null> } = {};

  constructor(private readonly request: NotificationRequest) {
    this.store = new NotificationPopupStore(() => this.clock());
    this.store.subscribe(() => this.schedule());
  }

  useClock(now: () => number): void {
    this.clock = now;
    this.schedule();
  }

  shown(notification: ActiveNotification): void {
    this.store.shown(notification);
  }

  updated(notification: ActiveNotification): void {
    this.store.updated(notification);
  }

  closed(event: NotificationClosedEvent): void {
    this.store.closed(event);
  }

  snapshot(event: ActiveNotificationsEvent): void {
    this.store.replace(Array.isArray(event.notifications) ? event.notifications : []);
  }

  clear(): void {
    this.store.replace([]);
  }

  load(): Promise<void> {
    return this.request('GetActiveNotifications', []).then(
      answer => {
        if (answer) this.snapshot(answer as ActiveNotificationsEvent);
      },
      () => undefined);
  }

  isPending(id: string): boolean {
    return this.pending[id] !== undefined;
  }

  respond(id: string, actionId: string | null, values?: Record<string, unknown>): Promise<RespondToNotificationResponse | null> {
    const existing = this.pending[id];
    if (existing !== undefined) return existing;

    const body: RespondToNotificationRequest = { id, actionId };
    if (values !== undefined) body.values = values;

    const revision = this.store.get(id)?.revision ?? 0;
    const result = this.request('RespondToNotification', [body]).then(
      answer => {
        const response = (answer ?? null) as RespondToNotificationResponse | null;
        if (response !== null && (response.status === 'already-closed' || response.status === 'not-found')) {
          this.store.closed({ id, revision, reason: 'dismissed' });
        }
        return response;
      },
      () => null,
    ).then(response => {
      delete this.pending[id];
      return response;
    });

    this.pending[id] = result;
    return result;
  }

  private schedule(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }

    const next = this.store.nextExpiry();
    if (next === null || !isFinite(next)) return;

    const delay = Math.min(Math.max(0, next - this.clock()), MAX_TIMER_MS);
    this.timer = setTimeout(() => {
      this.timer = null;
      this.store.expireDue();
      this.schedule();
    }, delay);
  }
}
