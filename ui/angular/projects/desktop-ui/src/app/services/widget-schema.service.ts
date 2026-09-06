import { Injectable, inject } from '@angular/core';

import { WidgetType } from '@macro-deck/runtime';
import { ApiService } from '@shared';

const WIRE_TYPE_NAMES: Record<WidgetType, string> = {
  [WidgetType.ActionButton]: 'ActionButton',
  [WidgetType.MusicPlayer]: 'MusicPlayer',
  [WidgetType.Slider]: 'Slider',
  [WidgetType.Weather]: 'Weather',
  [WidgetType.HistoryGraph]: 'HistoryGraph',
  [WidgetType.Clock]: 'Clock',
};

@Injectable({ providedIn: 'root' })
export class WidgetSchemaService {
  private readonly api = inject(ApiService);

  private readonly cache = new Map<string, object | null>();
  private inFlight: Promise<Record<string, unknown>> | null = null;

  async schemaFor(type: WidgetType): Promise<object | null> {
    // A plugin type has no entry in WIRE_TYPE_NAMES - its id is its wire name (issue #843), unlike a
    // built-in type's kebab-case domain id.
    const wireType = WIRE_TYPE_NAMES[type] ?? type;

    if (this.cache.has(wireType)) {
      return this.cache.get(wireType) ?? null;
    }

    let schemas: Record<string, unknown>;
    try {
      schemas = await this.fetchSchemas();
    } catch {
      return null;
    }

    // The whole map came back successfully, so every key in it - and the confirmed absence of any
    // other key - is trustworthy enough to cache, not just the one type this call asked about.
    for (const [key, value] of Object.entries(schemas)) {
      this.cache.set(key, (value as object | null) ?? null);
    }
    if (!this.cache.has(wireType)) {
      this.cache.set(wireType, null);
    }

    return this.cache.get(wireType) ?? null;
  }

  private fetchSchemas(): Promise<Record<string, unknown>> {
    if (!this.inFlight) {
      this.inFlight = this.api.getWidgetDataSchemas()
        .then(response => {
          if (!response.success || !response.schemas) {
            throw new Error(response.error?.message ?? 'Failed to load widget data schemas');
          }
          return response.schemas;
        })
        .finally(() => {
          this.inFlight = null;
        });
    }
    return this.inFlight;
  }
}
