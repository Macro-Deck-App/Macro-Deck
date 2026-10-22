import { DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppStrings, IntegrationCapabilityDataChangedEvent } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { debounceTime, filter } from 'rxjs';

export class CapabilityDataLoader<T extends { found: boolean; available: boolean }> {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly destroyRef = inject(DestroyRef);
  private request = 0;
  private integrationId = '';

  readonly data = signal<T | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  constructor(kind: string, private readonly fetch: (integrationId: string) => Promise<T>) {
    this.api.onNotification<IntegrationCapabilityDataChangedEvent>('IntegrationCapabilityDataChangedEvent')
      .pipe(
        filter(event => event.kind === kind && event.integrationIds.includes(this.integrationId)),
        debounceTime(150),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => void this.load(this.integrationId));
  }

  async load(integrationId: string): Promise<void> {
    this.integrationId = integrationId;
    if (!integrationId) {
      return;
    }

    const request = ++this.request;
    this.loading.set(this.data() === null);
    try {
      const response = await this.fetch(integrationId);
      if (request !== this.request) {
        return;
      }
      this.data.set(response);
      this.error.set(null);
    } catch {
      if (request === this.request && this.data() === null) {
        this.error.set(this.localization.translateKey(AppStrings.Integrations.Detail.CapabilityLoadFailed));
      }
    } finally {
      if (request === this.request) {
        this.loading.set(false);
      }
    }
  }
}
