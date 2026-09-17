import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { StoreAvailableUpdateBody, StoreUpdatesChangedEvent } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { StoreOperationService } from './store-operation.service';

@Injectable({ providedIn: 'root' })
export class StoreUpdatesService {
  private readonly api = inject(ApiService);
  private readonly operations = inject(StoreOperationService);

  private readonly all = signal<StoreAvailableUpdateBody[]>([]);

  readonly updates = computed(() =>
    this.all().filter(update => update.kind === 'Plugin' || update.kind === 'IconPack'));

  readonly count = computed(() => this.updates().length);

  readonly installing = signal(false);

  constructor() {
    this.api.onNotification<StoreUpdatesChangedEvent>('StoreUpdatesChangedEvent')
      .subscribe(event => this.all.set(event.updates ?? []));

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        void this.load();
      }
    });
  }

  async load(): Promise<void> {
    try {
      const response = await this.api.getStoreUpdates();
      this.all.set(response.updates ?? []);
    } catch (error) {
      console.error('Failed to load store updates:', error);
    }
  }

  async installAll(): Promise<void> {
    if (this.installing()) {
      return;
    }

    this.installing.set(true);
    try {
      await this.api.installStoreUpdates();
      await this.operations.load();
    } finally {
      this.installing.set(false);
    }
  }
}
