import { Injectable, inject } from '@angular/core';
import { IconPrefetchTarget } from '@macro-deck/runtime';
import { IconImageService } from './icon-image.service';

@Injectable({ providedIn: 'root' })
export class IconPrefetchService {
  private static readonly CONCURRENCY = 3;
  private static readonly RETRY_DELAY_MS = 1500;
  private static readonly MAX_RETRIES = 20;

  private readonly iconImage = inject(IconImageService);
  private readonly requested = new Set<string>();
  private pending: Array<{ provider: () => readonly IconPrefetchTarget[] | null; retries: number }> = [];
  private readonly queue: string[] = [];
  private active = 0;
  private scheduled = false;

  prefetch(
    targets: () => readonly IconPrefetchTarget[] | null,
    loader?: (url: string) => Promise<void>
  ): void {
    this.loader = loader ?? this.loader;
    this.pending.push({ provider: targets, retries: 0 });
    this.schedule();
  }

  private loader: (url: string) => Promise<void> = url => new Promise(resolve => {
    const image = new Image();
    image.onload = () => resolve();
    image.onerror = () => resolve();
    image.src = url;
  });

  private schedule(delayMs = 0): void {
    if (this.scheduled) {
      return;
    }
    this.scheduled = true;

    const start = () => {
      this.scheduled = false;
      this.drainPending();
      const slots = IconPrefetchService.CONCURRENCY - this.active;
      for (let i = 0; i < slots; i++) {
        this.next();
      }
      if (this.pending.length > 0) {
        this.schedule(IconPrefetchService.RETRY_DELAY_MS);
      }
    };

    if (delayMs === 0 && typeof requestIdleCallback === 'function') {
      requestIdleCallback(() => start());
    } else {
      setTimeout(start, delayMs || IconPrefetchService.RETRY_DELAY_MS);
    }
  }

  private drainPending(): void {
    const retry: typeof this.pending = [];
    for (const entry of this.pending.splice(0)) {
      const targets = entry.provider();
      if (targets === null) {
        if (entry.retries < IconPrefetchService.MAX_RETRIES) {
          retry.push({ provider: entry.provider, retries: entry.retries + 1 });
        }
        continue;
      }
      for (const target of targets) {
        const url = this.iconImage.getIconUrl(target.iconId, target.size);
        if (url && !this.requested.has(url)) {
          this.requested.add(url);
          this.queue.push(url);
        }
      }
    }
    this.pending = retry;
  }

  private next(): void {
    const url = this.queue.shift();
    if (!url) {
      return;
    }
    this.active++;
    void this.loader(url).then(() => {
      this.active--;
      this.next();
    });
  }
}
