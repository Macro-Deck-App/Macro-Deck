import { Injectable, inject, signal } from '@angular/core';
import { StoreCategoryBody, StoreExtensionKind } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

const FALLBACK_LANGUAGE = 'en';

export interface StoreCategoryScope {
  kinds?: StoreExtensionKind[];
  supportedOnly?: boolean;
}

export function storeCategoryName(category: StoreCategoryBody, culture: string): string {
  const language = culture.split(/[-_]/)[0].toLowerCase();
  return category.names[language] ?? category.names[FALLBACK_LANGUAGE] ?? category.id;
}

@Injectable({ providedIn: 'root' })
export class StoreCategoryService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly categories = signal<StoreCategoryBody[]>([]);
  readonly settled = signal(false);

  private loadedScope: string | null = null;
  private scope: StoreCategoryScope = {};
  private generation = 0;

  constructor() {
    this.api.onNotification('StoreCatalogChangedEvent').subscribe(() => {
      if (this.loadedScope !== null) {
        void this.reload();
      }
    });
  }

  async load(scope: StoreCategoryScope = {}): Promise<void> {
    const key = JSON.stringify([scope.kinds ?? [], !!scope.supportedOnly]);
    if (key === this.loadedScope) {
      return;
    }
    this.scope = scope;
    this.loadedScope = key;
    await this.reload();
  }

  async ensure(): Promise<void> {
    if (this.loadedScope === null) {
      await this.load();
    }
  }

  async reload(): Promise<void> {
    const generation = ++this.generation;
    try {
      const response = await this.api.getStoreCategories(this.scope.kinds, this.scope.supportedOnly || undefined);
      if (generation === this.generation) {
        this.categories.set(response.categories ?? []);
        this.settled.set(true);
      }
    } catch {
      // An older host has no categories endpoint; any failure shows no categories and retries on the next load.
      if (generation === this.generation) {
        this.loadedScope = null;
        this.categories.set([]);
        this.settled.set(true);
      }
    }
  }

  find(id: string): StoreCategoryBody | null {
    return this.categories().find(category => category.id === id) ?? null;
  }

  displayName(id: string): string {
    const category = this.find(id);
    return category ? storeCategoryName(category, this.localization.culture()) : id;
  }
}
