import { Injectable, computed, inject, signal } from '@angular/core';

import { AppStrings, EventDefinition, EventDefinitionDto, EventProviderGroup, TriggerEventResponse, groupEventsByProvider, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { mapActionParameterDef } from '../domain/action-parameter-mapping.util';

@Injectable({ providedIn: 'root' })
export class EventCatalogService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  private readonly _rawEvents = signal<EventDefinitionDto[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);

  private inFlight: Promise<void> | null = null;
  private loaded = false;

  readonly events = computed<EventDefinition[]>(() =>
    this._rawEvents().map(event => ({
      ...event,
      providerName: resolveLocalizedText(event.providerName, this.localization),
      name: resolveLocalizedText(event.name, this.localization),
      description: resolveLocalizedText(event.description, this.localization) || undefined,
      category: resolveLocalizedText(event.category, this.localization) || undefined,
      configurationParameters: (event.configurationParameters ?? [])
        .map(p => mapActionParameterDef(p, this.localization)),
      payloadParameters: (event.payloadParameters ?? [])
        .map(p => mapActionParameterDef(p, this.localization)),
    })),
  );
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();

  load(): Promise<void> {
    if (this.loaded) return Promise.resolve();
    return (this.inFlight ??= this.fetch());
  }

  reload(): Promise<void> {
    this.loaded = false;
    this.inFlight = null;
    return this.load();
  }

  find(qualifiedId: string | undefined): EventDefinition | undefined {
    if (!qualifiedId) return undefined;
    return this.events().find(event => event.id === qualifiedId);
  }

  groups(): EventProviderGroup[] {
    return groupEventsByProvider(this.events());
  }

  trigger(eventId: string, parameters: Record<string, unknown>): Promise<TriggerEventResponse> {
    return this.api.triggerEvent({ eventId, parameters });
  }

  private async fetch(): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      const response = await this.api.getEventDefinitions();
      this._rawEvents.set(response.events ?? []);
      this.loaded = true;
    } catch {
      this._error.set(this.localization.translateKey(AppStrings.Errors.Event.LoadFailed));
      this._rawEvents.set([]);
    } finally {
      this._loading.set(false);
      this.inFlight = null;
    }
  }
}
