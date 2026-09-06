import { Injectable, effect, inject, signal, untracked } from '@angular/core';

import { WidgetType } from '@macro-deck/runtime';
import { ApiService, WidgetTypeInfo } from '../transport';

@Injectable({ providedIn: 'root' })
export class WidgetTypeCatalogService {
  private readonly api = inject(ApiService);

  private readonly catalog = signal<WidgetTypeInfo[]>([]);
  private loaded = false;
  private inFlight: Promise<void> | null = null;

  readonly types = this.catalog.asReadonly();

  constructor() {
    this.api.onWidgetTypeCatalogChanged().subscribe(event => this.catalog.set(event.types ?? []));

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        // untracked so reload()'s own reads do not become dependencies of the effect that calls it.
        untracked(() => void this.reload());
      }
    });
  }

  async load(): Promise<void> {
    if (this.loaded) return;

    this.inFlight ??= this.runLoad().finally(() => {
      this.inFlight = null;
    });
    return this.inFlight;
  }

  private async runLoad(): Promise<void> {
    try {
      const response = await this.api.getWidgetTypes();
      if (response.success) {
        this.catalog.set(response.types ?? []);
        this.loaded = true;
      }
    } catch (error) {
      console.error('Failed to load widget types:', error);
    }
  }

  async reload(): Promise<void> {
    this.loaded = false;
    await this.load();
  }

  async infoFor(type: WidgetType): Promise<WidgetTypeInfo | null> {
    await this.load();
    return this.catalog().find(entry => entry.id === type) ?? null;
  }
}
