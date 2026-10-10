import {
  resolveLocalizedText,
  type ActiveNotification,
  type LocalizationTranslator,
  type NotificationActionStyle,
} from '@macro-deck/runtime';
import type { NotificationFeed } from './notification-feed';
import { sourceOf } from './notification-text';
import { createToast, toastLayer, type ButtonVariant, type ToastAction, type ToastKind } from './ui';

export const MAX_VISIBLE_NOTIFICATION_TOASTS = 3;

interface Mounted {
  revision: number;
  element: HTMLElement;
}

function variantOf(style: NotificationActionStyle): ButtonVariant {
  if (style === 'primary') return 'primary';
  if (style === 'danger') return 'danger';
  return 'secondary';
}

export class NotificationToasts {
  private mounted: { [id: string]: Mounted } = {};

  constructor(
    private readonly feed: NotificationFeed,
    private readonly localization: LocalizationTranslator,
  ) {
    feed.store.subscribe(() => this.paint());
    this.paint();
  }

  repaint(): void {
    for (const id of Object.keys(this.mounted)) this.remove(id);
    this.paint();
  }

  private paint(): void {
    const wanted = this.feed.store.list()
      .filter(notification => notification.popup === 'toast')
      .slice(0, MAX_VISIBLE_NOTIFICATION_TOASTS);

    const keep: { [id: string]: boolean } = {};
    for (const notification of wanted) keep[notification.id] = true;
    for (const id of Object.keys(this.mounted)) {
      if (!keep[id]) this.remove(id);
    }

    for (const notification of wanted) {
      const existing = this.mounted[notification.id];
      if (existing === undefined) {
        const element = this.build(notification);
        toastLayer().appendChild(element);
        this.mounted[notification.id] = { revision: notification.revision, element };
      } else if (existing.revision !== notification.revision) {
        const element = this.build(notification);
        if (existing.element.parentNode !== null) existing.element.parentNode.replaceChild(element, existing.element);
        this.mounted[notification.id] = { revision: notification.revision, element };
      }
    }
  }

  private remove(id: string): void {
    const entry = this.mounted[id];
    if (entry === undefined) return;
    if (entry.element.parentNode !== null) entry.element.parentNode.removeChild(entry.element);
    delete this.mounted[id];
  }

  private build(notification: ActiveNotification): HTMLElement {
    const text = (value: ActiveNotification['title'] | undefined) => resolveLocalizedText(value, this.localization);
    const actions: ToastAction[] = notification.actions.map(action => ({
      label: text(action.label),
      variant: variantOf(action.style),
      onPress: () => { void this.feed.respond(notification.id, action.id); },
    }));

    const progress = notification.progress;
    const message = notification.message === undefined ? '' : text(notification.message);

    return createToast(
      text(notification.title),
      {
        kind: notification.severity as ToastKind,
        detail: message,
        source: sourceOf(notification, this.localization),
        progress: progress === undefined
          ? undefined
          : { processed: progress.processed, total: progress.total === undefined ? null : progress.total },
        actions,
        dismissible: notification.dismissible,
      },
      () => { void this.feed.respond(notification.id, null); });
  }
}
