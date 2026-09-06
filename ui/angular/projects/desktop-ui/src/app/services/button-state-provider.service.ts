import { Injectable, inject } from '@angular/core';
import { GetActionButtonStateOptionsRequest, GetActionButtonStateOptionsResponse, ProvidedState, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

@Injectable({ providedIn: 'root' })
export class ButtonStateProviderService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly cache = new Map<string, ProvidedState[]>();
  private readonly inFlight = new Map<string, Promise<ProvidedState[]>>();

  // Monotonic request counter per block id: a fetched result is only returned while it is still the
  // latest request for that block, so a slow answer for parameters the user has since changed can
  // never overwrite a newer request's result. Centralized here because every caller faces this race.
  private readonly requestSeq = new Map<string, number>();

  async getStates(blockId: string, request: GetActionButtonStateOptionsRequest): Promise<ProvidedState[] | null> {
    const seq = (this.requestSeq.get(blockId) ?? 0) + 1;
    this.requestSeq.set(blockId, seq);

    const key = this.cacheKey(request);
    const cached = this.cache.get(key);
    if (cached) {
      return this.isCurrent(blockId, seq) ? cached : null;
    }

    let pending = this.inFlight.get(key);
    if (!pending) {
      pending = this.fetch(key, request);
      this.inFlight.set(key, pending);
    }

    const result = await pending;
    return this.isCurrent(blockId, seq) ? result : null;
  }

  invalidate(integrationId: string, actionId: string): void {
    const prefix = `${integrationId}|${actionId}|`;
    for (const key of this.cache.keys()) {
      if (key.startsWith(prefix)) {
        this.cache.delete(key);
      }
    }
  }

  private isCurrent(blockId: string, seq: number): boolean {
    return this.requestSeq.get(blockId) === seq;
  }

  private async fetch(key: string, request: GetActionButtonStateOptionsRequest): Promise<ProvidedState[]> {
    try {
      const response: GetActionButtonStateOptionsResponse = await this.api.getActionButtonStateOptions(request);
      const raw = response.error ? [] : (response.states ?? []);
      // Stored state labels must be plain strings: the widget-data schema requires `label: string`,
      // and the host's own `ActionButtonStateJson.AdoptProviderStates` resolves provider labels for
      // the same reason. Resolving here, at the transport boundary, means nothing downstream ever
      // sees an unresolved LocalizedText.
      const states: ProvidedState[] = raw.map(state => ({
        id: state.id,
        label: resolveLocalizedText(state.label, this.localization),
        defaultAppearance: state.defaultAppearance,
      }));
      if (states.length > 0) {
        this.cache.set(key, states);
      }
      return states;
    } finally {
      this.inFlight.delete(key);
    }
  }

  private cacheKey(request: GetActionButtonStateOptionsRequest): string {
    // Cached values hold labels already resolved for the active culture (see `fetch`), so the culture
    // must be part of the key - otherwise a language switch mid-session would keep serving stale,
    // old-language labels out of the cache until the next reload, and those could get persisted into
    // widget data by an editor that adopts them.
    return [
      request.integrationId,
      request.actionId,
      JSON.stringify(request.parameters ?? {}),
      this.localization.culture(),
    ].join('|');
  }
}

export type { ProvidedState };
