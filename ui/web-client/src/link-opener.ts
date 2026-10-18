import { ClientAppStrings, isOpenableWebUrl, store, type ReadableStore, type WritableStore } from '@macro-deck/runtime';
import { createToast, toastLayer } from './ui';

export type OpenWindow = (url: string) => Window | null;

export interface BlockedLink {
  url: string;
}

const BLOCKED_LINK_VISIBLE_MS = 30000;

function browserOpenWindow(url: string): Window | null {
  return window.open(url, '_blank');
}

export class LinkOpener {
  private readonly blockedStore: WritableStore<BlockedLink | null> = store<BlockedLink | null>(null);
  readonly blocked: ReadableStore<BlockedLink | null> = this.blockedStore;

  constructor(private readonly openWindow: OpenWindow = browserOpenWindow) {}

  open(url: unknown): void {
    // The host already checked the scheme; it is checked again because this is what reaches the browser.
    if (!isOpenableWebUrl(url)) return;

    let opened: Window | null = null;
    try {
      opened = this.openWindow(url);
    } catch {
      opened = null;
    }

    if (opened === null) {
      // A popup blocker, or iOS outside the gesture window, refuses it: a tap on a real link is a gesture.
      this.blockedStore.set({ url });
      return;
    }

    try {
      opened.opener = null;
    } catch {
      // A browser that already severed the opener refuses the write.
    }
    this.blockedStore.set(null);
  }

  dismiss(): void {
    this.blockedStore.set(null);
  }
}

export class BlockedLinkBanner {
  private shown: HTMLElement | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor(
    private readonly links: LinkOpener,
    private readonly translate: (key: string) => string,
  ) {
    links.blocked.subscribe(blocked => this.paint(blocked));
    this.paint(links.blocked.get());
  }

  private paint(blocked: BlockedLink | null): void {
    this.remove();
    if (blocked === null) return;

    const toast = createToast(
      this.translate(ClientAppStrings.WebClient.OpenLink.Blocked),
      { kind: 'info', detail: this.translate(ClientAppStrings.WebClient.OpenLink.TapToOpen) },
      () => this.links.dismiss());

    const link = document.createElement('a');
    link.className = 'wc-toast-link';
    link.href = blocked.url;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    link.textContent = blocked.url;
    link.addEventListener('click', () => {
      // Removed after the click rather than during it, so the browser still follows the link.
      setTimeout(() => this.links.dismiss(), 0);
    });

    const text = toast.querySelector('.wc-toast-text');
    (text ?? toast).appendChild(link);

    toastLayer().appendChild(toast);
    this.shown = toast;
    this.timer = setTimeout(() => this.links.dismiss(), BLOCKED_LINK_VISIBLE_MS);
  }

  private remove(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
    if (this.shown !== null && this.shown.parentNode !== null) this.shown.parentNode.removeChild(this.shown);
    this.shown = null;
  }
}
