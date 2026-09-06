import { Injectable, computed, inject, signal } from '@angular/core';
import { LogEntriesAppendedNotification, LogEntry, LogQuery, LogSourceSummary } from '@macro-deck/runtime';
import { ApiService } from '@shared';

interface LogPage {
  entries: LogEntry[];
  olderCursor?: string;
  isLive?: boolean;
}

@Injectable({ providedIn: 'root' })
export class LogService {
  static readonly WINDOW_LIMIT = 2000;

  private static readonly PAGE_SIZE = 200;

  private readonly api = inject(ApiService);

  private readonly _entries = signal<LogEntry[]>([]);
  private readonly _sources = signal<LogSourceSummary[]>([]);
  private readonly _isLoading = signal(false);
  private readonly _isLoadingOlder = signal(false);
  private readonly _hasLoaded = signal(false);
  private readonly _hasMore = signal(false);

  readonly entries = this._entries.asReadonly();
  readonly sources = this._sources.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly isLoadingOlder = this._isLoadingOlder.asReadonly();
  readonly hasLoaded = this._hasLoaded.asReadonly();
  readonly hasMore = this._hasMore.asReadonly();

  readonly isStreaming = computed(() => this.watchers() > 0 && this.api.connectionStateSignal() === 'connected');

  private readonly watchers = signal(0);

  private started = false;
  private query: LogQuery = {};
  private pages: LogPage[] = [];
  private tailAnchor?: string;
  private seen = new Set<string>();
  private queryToken = 0;
  private loading: Promise<void> | null = null;
  private loadingOlder: Promise<void> | null = null;

  watch(): void {
    this.watchers.update(count => count + 1);
    this.start();
  }

  unwatch(): void {
    const remaining = Math.max(0, this.watchers() - 1);
    this.watchers.set(remaining);
    if (remaining === 0) {
      void this.api.invoke('UnsubscribeLogs');
      this.reset();
    }
  }

  async setQuery(query: LogQuery): Promise<void> {
    this.query = query;
    this.loading = this.fetch(query).finally(() => (this.loading = null));

    return this.loading;
  }

  async loadOlder(): Promise<void> {
    if (!this._hasMore() || this.olderCursor() === undefined) {
      return;
    }

    this.loadingOlder ??= this.fetchOlder().finally(() => (this.loadingOlder = null));

    return this.loadingOlder;
  }

  async refreshSources(): Promise<void> {
    return this.loadSources();
  }

  reset(): void {
    this.query = {};
    this.pages = [];
    this._entries.set([]);
    this._sources.set([]);
    this._hasLoaded.set(false);
    this._hasMore.set(false);
    this.seen = new Set();
    this.tailAnchor = undefined;
    this.queryToken++;
  }

  private olderCursor(): string | undefined {
    return this.pages[0]?.olderCursor;
  }

  private start(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    this.api
      .onNotification<LogEntriesAppendedNotification>('LogEntriesAppendedNotification')
      .subscribe(notification => {
        this.mergeSources(notification.sources ?? []);
        this.append(notification.entries ?? []);
      });

    this.api.connectionState$.subscribe(state => {
      if (state === 'connected' && this.watchers() > 0) {
        void this.setQuery(this.query);
      }
    });
  }

  private async fetch(query: LogQuery): Promise<void> {
    const token = ++this.queryToken;
    this._isLoading.set(true);

    await this.unsubscribe();

    try {
      // No level selected at all matches nothing. The host reads an absent level list as "every
      // level", so this cannot be expressed as a query - and asking would be a wasted file scan.
      const response = query.levels?.length === 0
        ? { entries: [], hasMore: false, tailAnchor: this.tailAnchor ?? '' }
        : await this.api.getLogs({ ...query, limit: LogService.PAGE_SIZE });
      if (token !== this.queryToken) {
        return;
      }

      this.pages = [{ entries: response.entries, olderCursor: response.olderCursor }];
      this.seen = new Set(response.entries.map(entry => entry.id));
      this.tailAnchor = response.tailAnchor;
      this._hasMore.set(response.hasMore);
      this._hasLoaded.set(true);
      this.publish();

      await this.subscribe(query);
      // The rail describes the log, not the filter, so a debounced keystroke must not cost a file
      // scan. Sources are read once here and joined by whatever the live stream reveals after that.
      if (this._sources().length === 0) {
        void this.loadSources();
      }
    } finally {
      if (token === this.queryToken) {
        this._isLoading.set(false);
      }
    }
  }

  private async fetchOlder(): Promise<void> {
    const token = this.queryToken;
    this._isLoadingOlder.set(true);
    try {
      const response = await this.api.getLogs({
        ...this.query,
        limit: LogService.PAGE_SIZE,
        before: this.olderCursor(),
      });
      if (token !== this.queryToken) {
        return;
      }

      const fresh = response.entries.filter(entry => !this.seen.has(entry.id));
      for (const entry of fresh) {
        this.seen.add(entry.id);
      }

      this.pages.unshift({ entries: fresh, olderCursor: response.olderCursor });
      this._hasMore.set(response.hasMore);
      this.publish();
    } finally {
      if (token === this.queryToken) {
        this._isLoadingOlder.set(false);
      }
    }
  }

  private async loadSources(): Promise<void> {
    const token = this.queryToken;
    try {
      const response = await this.api.getLogSources();
      if (token === this.queryToken) {
        this._sources.set(response.sources ?? []);
      }
    } catch {
      // The rail is a convenience; failing to count sources must not empty the viewer.
    }
  }

  private async subscribe(query: LogQuery): Promise<void> {
    if (this.watchers() === 0 || this.api.connectionStateSignal() !== 'connected') {
      return;
    }

    await this.api.invoke('SubscribeLogs', query, this.tailAnchor);
  }

  private async unsubscribe(): Promise<void> {
    if (this.api.connectionStateSignal() === 'connected') {
      await this.api.invoke('UnsubscribeLogs');
    }
  }

  private append(entries: LogEntry[]): void {
    const fresh = entries.filter(entry => !this.seen.has(entry.id));
    if (fresh.length === 0) {
      return;
    }

    for (const entry of fresh) {
      this.seen.add(entry.id);
    }

    const newest = this.pages.at(-1);
    if (newest === undefined || !newest.isLive || newest.entries.length >= LogService.PAGE_SIZE) {
      const isFirstLivePage = !this.pages.some(page => page.isLive);
      this.pages.push({ entries: fresh, olderCursor: isFirstLivePage ? this.tailAnchor : undefined, isLive: true });
    } else {
      newest.entries = newest.entries
        .concat(fresh)
        .sort((left, right) => left.timestamp.localeCompare(right.timestamp));
    }

    this.trim();
    this.publish();
  }

  private mergeSources(sources: LogSourceSummary[]): void {
    if (sources.length === 0 || !this._hasLoaded()) {
      return;
    }

    this._sources.update(current => {
      const merged = [...current];
      for (const source of sources) {
        const known = merged.some(
          summary => summary.source === source.source && summary.sourceId === source.sourceId);
        if (!known) {
          merged.push({ ...source });
        }
      }

      return merged;
    });
  }

  private trim(): void {
    let held = this.pages.reduce((total, page) => total + page.entries.length, 0);
    while (held > LogService.WINDOW_LIMIT && this.pages.length > 1) {
      const dropped = this.pages.shift();
      for (const entry of dropped?.entries ?? []) {
        this.seen.delete(entry.id);
      }

      held -= dropped?.entries.length ?? 0;
      this._hasMore.set(this.olderCursor() !== undefined);
    }
  }

  private publish(): void {
    this._entries.set(this.pages.flatMap(page => page.entries));
  }
}
