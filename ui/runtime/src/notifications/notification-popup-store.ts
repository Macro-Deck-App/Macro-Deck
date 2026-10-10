import {
  ActiveNotification,
  NotificationClosedEvent,
} from '../protocol/messages/notifications';

const TOMBSTONE_LIFETIME_MS = 5 * 60 * 1000;
const TOMBSTONE_LIMIT = 200;

export class NotificationPopupStore {
  private items: { [id: string]: ActiveNotification } = {};
  private tombstones: { [id: string]: number } = {};
  private expiries: { [id: string]: number } = {};
  private listeners: Array<() => void> = [];

  constructor(private readonly now: () => number = () => Date.now()) {}

  subscribe(listener: () => void): () => void {
    this.listeners.push(listener);
    return () => {
      this.listeners = this.listeners.filter(l => l !== listener);
    };
  }

  shown(notification: ActiveNotification): void {
    if (this.accept(notification)) this.emit();
  }

  updated(notification: ActiveNotification): void {
    if (this.accept(notification)) this.emit();
  }

  closed(event: NotificationClosedEvent): void {
    const had = this.items[event.id] !== undefined;
    delete this.items[event.id];
    delete this.expiries[event.id];
    this.bury(event.id);
    if (had) this.emit();
  }

  replace(notifications: ActiveNotification[]): void {
    const keep: { [id: string]: boolean } = {};
    let changed = false;

    for (const notification of notifications) {
      keep[notification.id] = true;
      if (this.accept(notification)) changed = true;
    }

    for (const id of Object.keys(this.items)) {
      if (!keep[id]) {
        delete this.items[id];
        delete this.expiries[id];
        changed = true;
      }
    }

    if (changed) this.emit();
  }

  expireDue(): string[] {
    const now = this.now();
    const expired = Object.keys(this.items).filter(id => {
      const at = this.expiries[id];
      return at !== undefined && at <= now;
    });

    for (const id of expired) {
      delete this.items[id];
      delete this.expiries[id];
      this.bury(id);
    }

    if (expired.length > 0) this.emit();
    return expired;
  }

  nextExpiry(): number | null {
    let next: number | null = null;
    for (const id of Object.keys(this.expiries)) {
      const time = this.expiries[id];
      if (next === null || time < next) next = time;
    }
    return next;
  }

  list(): ActiveNotification[] {
    return Object.keys(this.items)
      .map(id => this.items[id])
      .filter(n => n.popup !== 'none')
      .sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt) || (a.id < b.id ? -1 : 1));
  }

  get(id: string): ActiveNotification | undefined {
    return this.items[id];
  }

  private trackExpiry(notification: ActiveNotification): void {
    if (notification.expiresInMs !== undefined) {
      this.expiries[notification.id] = this.now() + notification.expiresInMs;
    } else if (notification.expiresAt !== undefined) {
      this.expiries[notification.id] = Date.parse(notification.expiresAt);
    } else {
      delete this.expiries[notification.id];
    }
  }

  private accept(notification: ActiveNotification): boolean {
    this.pruneTombstones();
    if (this.tombstones[notification.id] !== undefined) return false;

    const existing = this.items[notification.id];
    if (existing !== undefined && notification.revision <= existing.revision) return false;

    this.items[notification.id] = notification;
    this.trackExpiry(notification);
    return true;
  }

  private bury(id: string): void {
    this.tombstones[id] = this.now();
    this.pruneTombstones();
  }

  private pruneTombstones(): void {
    const now = this.now();
    const ids = Object.keys(this.tombstones);

    for (const id of ids) {
      if (now - this.tombstones[id] > TOMBSTONE_LIFETIME_MS) delete this.tombstones[id];
    }

    const remaining = Object.keys(this.tombstones);
    if (remaining.length > TOMBSTONE_LIMIT) {
      remaining
        .sort((a, b) => this.tombstones[a] - this.tombstones[b])
        .slice(0, remaining.length - TOMBSTONE_LIMIT)
        .forEach(id => delete this.tombstones[id]);
    }
  }

  private emit(): void {
    for (const listener of this.listeners.slice()) listener();
  }
}
