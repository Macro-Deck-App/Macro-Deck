import { Injectable, effect, inject, signal } from '@angular/core';
import { DeveloperSettingsChangedEvent } from '@macro-deck/runtime';
import { ApiService } from '@shared';

@Injectable({ providedIn: 'root' })
export class DeveloperModeService {
  private readonly api = inject(ApiService);

  readonly enabled = signal(false);

  private inFlight: Promise<void> | null = null;

  private generation = 0;

  constructor() {
    this.api
      .onNotification<DeveloperSettingsChangedEvent>('DeveloperSettingsChangedEvent')
      .subscribe(evt => this.apply(evt.enabled === true));

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        void this.load();
      }
    });
  }

  ensureLoaded(): Promise<void> {
    this.inFlight ??= this.load().finally(() => {
      this.inFlight = null;
    });
    return this.inFlight;
  }

  private async load(): Promise<void> {
    const startedAt = this.generation;
    try {
      const status = await this.api.getStoreStatus();
      if (startedAt === this.generation) {
        this.apply(status.developerMode === true);
      }
    } catch (error) {
      console.error('Failed to read the store status:', error);
    }
  }

  private apply(enabled: boolean): void {
    this.generation++;
    this.enabled.set(enabled);
  }
}
