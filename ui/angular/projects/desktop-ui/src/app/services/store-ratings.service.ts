import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { StoreRatingSummaryBody } from '@macro-deck/runtime';
import { ApiService } from '@shared';

export const STORE_RATINGS_MAX_IDS_PER_REQUEST = 100;
export const STORE_RATINGS_LIFETIME_MS = 5 * 60 * 1000;

interface FetchTrack {
  readonly fetchedAt: Map<string, number>;
  readonly inFlight: Set<string>;
}

@Injectable({ providedIn: 'root' })
export class StoreRatingsService {
  private readonly api = inject(ApiService);

  readonly ratings = signal<ReadonlyMap<string, StoreRatingSummaryBody>>(new Map());
  readonly installs = signal<ReadonlyMap<string, number>>(new Map());

  private readonly ratingsTrack: FetchTrack = { fetchedAt: new Map(), inFlight: new Set() };
  private readonly installsTrack: FetchTrack = { fetchedAt: new Map(), inFlight: new Set() };
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
    await Promise.all([
      ...this.chunks(this.stale(this.ratingsTrack, packageIds, now)).map(chunk => this.fetchRatings(chunk)),
      ...this.chunks(this.stale(this.installsTrack, packageIds, now)).map(chunk => this.fetchInstalls(chunk)),
    ]);
  }

  async ensureInstalls(packageIds: readonly string[]): Promise<void> {
    const stale = this.stale(this.installsTrack, packageIds, Date.now());
    await Promise.all(this.chunks(stale).map(chunk => this.fetchInstalls(chunk)));
  }

  invalidate(packageId: string): void {
    this.ratingsTrack.fetchedAt.delete(packageId);
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
    for (const track of [this.ratingsTrack, this.installsTrack]) {
      track.fetchedAt.clear();
      track.inFlight.clear();
    }
    this.ratings.set(new Map());
    this.installs.set(new Map());
  }

  private stale(track: FetchTrack, packageIds: readonly string[], now: number): string[] {
    return [...new Set(packageIds)].filter(id => {
      if (!id || track.inFlight.has(id)) {
        return false;
      }
      const fetched = track.fetchedAt.get(id);
      return fetched === undefined || now - fetched >= STORE_RATINGS_LIFETIME_MS;
    });
  }

  private chunks(ids: string[]): string[][] {
    const chunks: string[][] = [];
    for (let index = 0; index < ids.length; index += STORE_RATINGS_MAX_IDS_PER_REQUEST) {
      chunks.push(ids.slice(index, index + STORE_RATINGS_MAX_IDS_PER_REQUEST));
    }
    return chunks;
  }

  private fetchRatings(ids: string[]): Promise<void> {
    return this.fetch(this.ratingsTrack, ids, 'store ratings', async () => {
      const response = await this.api.getStoreRatings(ids);
      if (!response.available) {
        return null;
      }
      return () => this.ratings.update(current => {
        const next = new Map(current);
        for (const id of ids) {
          const summary = response.ratings?.[id];
          if (summary && summary.ratingCount > 0) {
            next.set(id, summary);
          } else {
            next.delete(id);
          }
        }
        return next;
      });
    });
  }

  private fetchInstalls(ids: string[]): Promise<void> {
    return this.fetch(this.installsTrack, ids, 'store install counts', async () => {
      const response = await this.api.getStoreInstalls(ids);
      if (!response.available) {
        return null;
      }
      return () => this.installs.update(current => {
        const next = new Map(current);
        for (const id of ids) {
          const count = response.installs?.[id];
          if (typeof count === 'number' && count > 0) {
            next.set(id, count);
          } else {
            next.delete(id);
          }
        }
        return next;
      });
    });
  }

  private async fetch(track: FetchTrack,
    ids: string[],
    what: string,
    load: () => Promise<(() => void) | null>): Promise<void> {
    const generation = this.generation;
    for (const id of ids) {
      track.inFlight.add(id);
    }

    try {
      const apply = await load();
      if (generation !== this.generation || !apply) {
        return;
      }

      const fetchedAt = Date.now();
      for (const id of ids) {
        track.fetchedAt.set(id, fetchedAt);
      }
      apply();
    } catch (error) {
      console.error(`Failed to load ${what}:`, error);
    } finally {
      if (generation === this.generation) {
        for (const id of ids) {
          track.inFlight.delete(id);
        }
      }
    }
  }
}
