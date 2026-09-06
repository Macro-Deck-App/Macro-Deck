import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { AppStrings, StoreCatalogItemBody, StoreCatalogSection, StoreExtensionKind, StoreRegistryStatusBody } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

export interface StoreCatalogQuery {
  kinds?: StoreExtensionKind[];
  search?: string;
  section?: StoreCatalogSection;
}

export const STORE_CATALOG_PAGE_SIZE = 24;

const STORE_CATALOG_MAX_TAKE = 100;

@Injectable({ providedIn: 'root' })
export class StoreCatalogService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly items = signal<StoreCatalogItemBody[]>([]);
  readonly total = signal(0);
  readonly registry = signal<StoreRegistryStatusBody | null>(null);
  readonly isLoading = signal(false);
  readonly isLoadingMore = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly hasMore = computed(() => this.items().length < this.total());

  // Null until a consumer has said what it wants to browse. A reload before that would have to guess
  // the query, and the guess - every kind, unsorted - is one the store never asks for.
  private lastQuery: StoreCatalogQuery | null = null;

  // A query issued while another is in flight must win, not be coalesced onto the earlier one: the
  // filter and sort controls change the query on every click, and the answer to the superseded
  // question is the wrong list. Responses carrying a stale generation are dropped.
  private generation = 0;

  constructor() {
    this.api.onNotification('StoreCatalogChangedEvent').subscribe(() => {
      void this.reload();
    });

    this.api.onNotification<{ registry: StoreRegistryStatusBody }>('StoreRegistryStatusChangedEvent')
      .subscribe(event => {
        this.registry.set(event.registry);
      });

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        // untracked: reload() reads items() to size the refetch, and an effect that took a
        // dependency on the signal it goes on to write would retrigger itself forever.
        untracked(() => {
          void this.reload();
        });
      }
    });
  }

  async load(query?: StoreCatalogQuery): Promise<void> {
    const effective = query ?? this.lastQuery;
    if (!effective) {
      return;
    }

    const previous = this.lastQuery;
    this.lastQuery = effective;
    const generation = ++this.generation;
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const response = await this.api.getStoreCatalog({
        ...effective,
        skip: 0,
        take: STORE_CATALOG_PAGE_SIZE,
      });
      if (generation !== this.generation) {
        return;
      }

      this.items.set(response.items ?? []);
      this.total.set(response.total ?? 0);
      this.registry.set(response.registry ?? null);
    } catch (error) {
      if (generation === this.generation) {
        // items still describe `previous`, so lastQuery has to as well: paging on from a list the
        // failed query never produced would merge two different queries into one grid.
        this.lastQuery = previous;
        this.reportFailure(error);
      }
    } finally {
      if (generation === this.generation) {
        this.isLoading.set(false);
      }
    }
  }

  async loadMore(): Promise<void> {
    // isLoading covers a load or reload in flight: both replace the list wholesale, so a page
    // appended across one would be thrown away the moment it lands.
    if (!this.lastQuery || !this.hasMore() || this.isLoadingMore() || this.isLoading()) {
      return;
    }

    const generation = this.generation;
    this.isLoadingMore.set(true);
    this.loadError.set(null);
    try {
      const response = await this.api.getStoreCatalog({
        ...this.lastQuery,
        skip: this.items().length,
        take: STORE_CATALOG_PAGE_SIZE,
      });
      if (generation !== this.generation) {
        return;
      }

      this.items.update(loaded => merge(loaded, response.items ?? []));
      this.total.set(response.total ?? 0);
      this.registry.set(response.registry ?? null);
    } catch (error) {
      if (generation === this.generation) {
        this.reportFailure(error);
      }
    } finally {
      this.isLoadingMore.set(false);
    }
  }

  async refreshRegistry(): Promise<void> {
    try {
      const response = await this.api.refreshStoreRegistry();
      this.registry.set(response.registry);
      if (response.success) {
        await this.reload();
      }
    } catch (error) {
      console.error('Failed to refresh the store registry:', error);
    }
  }

  private async reload(): Promise<void> {
    const query = this.lastQuery;
    if (!query) {
      return;
    }

    const loaded = Math.max(STORE_CATALOG_PAGE_SIZE, this.items().length);
    const generation = ++this.generation;
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const pages: StoreCatalogItemBody[] = [];
      let total = 0;
      let registry: StoreRegistryStatusBody | null = null;
      for (let skip = 0; skip < loaded; skip += STORE_CATALOG_MAX_TAKE) {
        const response = await this.api.getStoreCatalog({
          ...query,
          skip,
          take: Math.min(STORE_CATALOG_MAX_TAKE, loaded - skip),
        });
        if (generation !== this.generation) {
          return;
        }

        pages.push(...(response.items ?? []));
        total = response.total ?? 0;
        registry = response.registry ?? null;
      }

      this.items.set(merge([], pages));
      this.total.set(total);
      this.registry.set(registry);
    } catch (error) {
      if (generation === this.generation) {
        this.reportFailure(error);
      }
    } finally {
      if (generation === this.generation) {
        this.isLoading.set(false);
      }
    }
  }

  private reportFailure(error: unknown): void {
    console.error('Failed to load the store catalog:', error);
    this.loadError.set(error instanceof Error
      ? error.message
      : this.localization.translateKey(AppStrings.Errors.StoreCatalog.LoadFailed));
    // The items stay untouched - an empty list here would misreport "no extensions" for what is
    // really "could not check" - but the registry status must flip to stale so the banner is
    // truthful about what is now a possibly-outdated snapshot.
    const previous = this.registry();
    if (previous) {
      this.registry.set({ ...previous, stale: true });
    }
  }
}

function merge(loaded: StoreCatalogItemBody[], incoming: StoreCatalogItemBody[]): StoreCatalogItemBody[] {
  const seen = new Set(loaded.map(item => `${item.kind}:${item.id}`));
  const merged = [...loaded];
  for (const item of incoming) {
    const key = `${item.kind}:${item.id}`;
    if (!seen.has(key)) {
      seen.add(key);
      merged.push(item);
    }
  }
  return merged;
}
