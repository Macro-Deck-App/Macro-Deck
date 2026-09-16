import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { StoreRatingSummaryBody } from '@macro-deck/runtime';
import { ApiService } from '@shared';

export const STORE_RATINGS_MAX_IDS_PER_REQUEST = 100;
export const STORE_RATINGS_LIFETIME_MS = 5 * 60 * 1000;

@Injectable({ providedIn: 'root' })
export class StoreRatingsService {
  private readonly api = inject(ApiService);

  readonly ratings = signal<ReadonlyMap<string, StoreRatingSummaryBody>>(new Map());

  private readonly fetchedAt = new Map<string, number>();
  private readonly inFlight = new Set<string>();
  private generation = 0;

  constructor() {
    this.api.onNotification('StoreCatalogChangedEvent').subscribe(() => this.clear());

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        untracked(() => this.clear());
      }
    });
  }

  summary(packageId: string): StoreRatingSummaryBody | null {
    return this.ratings().get(packageId) ?? null;
  }

  async ensure(packageIds: readonly string[]): Promise<void> {
    const now = Date.now();
    const wanted = [...new Set(packageIds)].filter(id => {
      if (!id || this.inFlight.has(id)) {
        return false;
      }
      const fetched = this.fetchedAt.get(id);
      return fetched === undefined || now - fetched >= STORE_RATINGS_LIFETIME_MS;
    });
    if (wanted.length === 0) {
      return;
    }

    const chunks: string[][] = [];
    for (let index = 0; index < wanted.length; index += STORE_RATINGS_MAX_IDS_PER_REQUEST) {
      chunks.push(wanted.slice(index, index + STORE_RATINGS_MAX_IDS_PER_REQUEST));
    }
    await Promise.all(chunks.map(chunk => this.fetch(chunk)));
  }

  invalidate(packageId: string): void {
    this.fetchedAt.delete(packageId);
    if (this.ratings().has(packageId)) {
      this.ratings.update(current => {
        const next = new Map(current);
        next.delete(packageId);
        return next;
      });
    }
  }

  private clear(): void {
    this.generation++;
    this.fetchedAt.clear();
    this.inFlight.clear();
    this.ratings.set(new Map());
  }

  private async fetch(ids: string[]): Promise<void> {
    const generation = this.generation;
    for (const id of ids) {
      this.inFlight.add(id);
    }

    try {
      const response = await this.api.getStoreRatings(ids);
      if (generation !== this.generation || !response.available) {
        return;
      }

      const fetchedAt = Date.now();
      this.ratings.update(current => {
        const next = new Map(current);
        for (const id of ids) {
          this.fetchedAt.set(id, fetchedAt);
          const summary = response.ratings?.[id];
          if (summary && summary.ratingCount > 0) {
            next.set(id, summary);
          } else {
            next.delete(id);
          }
        }
        return next;
      });
    } catch (error) {
      console.error('Failed to load store ratings:', error);
    } finally {
      if (generation === this.generation) {
        for (const id of ids) {
          this.inFlight.delete(id);
        }
      }
    }
  }
}
