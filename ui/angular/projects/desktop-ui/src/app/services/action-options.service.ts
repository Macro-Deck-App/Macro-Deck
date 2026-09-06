import { Injectable, inject } from '@angular/core';
import { ActionParameterOption, AppStrings, GetActionParameterOptionsRequest, GetActionParameterOptionsResponse, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

interface CacheEntry {
  response: GetActionParameterOptionsResponse;
  expiresAt: number;
}

export interface ResolvedActionParameterOption {
  value: string;
  label: string;
  metadata?: Record<string, string>;
}

@Injectable({ providedIn: 'root' })
export class ActionOptionsService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly cache = new Map<string, CacheEntry>();
  private readonly inFlight = new Map<string, Promise<GetActionParameterOptionsResponse>>();
  private readonly latest = new Map<string, object>();

  async getOptions(
    request: GetActionParameterOptionsRequest,
    options?: { forceRefresh?: boolean },
  ): Promise<GetActionParameterOptionsResponse> {
    const key = this.cacheKey(request);

    if (!options?.forceRefresh) {
      const cached = this.cache.get(key);
      if (cached && cached.expiresAt > Date.now()) {
        return cached.response;
      }

      const pending = this.inFlight.get(key);
      if (pending) {
        return pending;
      }
    }

    const token = {};
    this.latest.set(key, token);
    return this.fetch(key, token, request, !options?.forceRefresh);
  }

  private fetch(
    key: string,
    token: object,
    request: GetActionParameterOptionsRequest,
    trackInFlight: boolean,
  ): Promise<GetActionParameterOptionsResponse> {
    const promise = this.run(key, token, request, trackInFlight);
    if (trackInFlight) {
      this.inFlight.set(key, promise);
    }
    return promise;
  }

  async loadLabeledOptions(
    request: GetActionParameterOptionsRequest,
    options?: { forceRefresh?: boolean },
  ): Promise<{
    options: ResolvedActionParameterOption[];
    error?: string;
  }> {
    try {
      const response = await this.getOptions(request, options);
      if (response.error) {
        return { options: [], error: resolveLocalizedText(response.error.message, this.localization) };
      }
      return {
        options: response.options.map(o => ({
          value: o.value,
          label: resolveLocalizedText(o.label, this.localization) || o.value,
          metadata: o.metadata,
        })),
      };
    } catch {
      return { options: [], error: this.localization.translateKey(AppStrings.ActionBuilder.Param.LoadOptionsFailed) };
    }
  }

  invalidate(integrationId: string, actionId: string, parameterName?: string): void {
    const prefix = parameterName === undefined
      ? `${integrationId}|${actionId}|`
      : `${integrationId}|${actionId}|${parameterName}|`;
    for (const key of this.cache.keys()) {
      if (key.startsWith(prefix)) {
        this.cache.delete(key);
      }
    }
  }

  private async run(
    key: string,
    token: object,
    request: GetActionParameterOptionsRequest,
    trackInFlight: boolean,
  ): Promise<GetActionParameterOptionsResponse> {
    try {
      const response = await this.api.getActionParameterOptions(request);
      const cacheSeconds = response.cacheSeconds ?? 0;
      // Only the most recently *started* fetch for this key may write the cache, so a slow
      // pre-reload response cannot land after - and overwrite - the reload's own result.
      if (!response.error && cacheSeconds > 0 && this.latest.get(key) === token) {
        this.cache.set(key, {
          response,
          expiresAt: Date.now() + cacheSeconds * 1000,
        });
      }
      return response;
    } finally {
      // A later fetch has already claimed the key, so an older one leaving must not drop its token -
      // that is what keeps the guard above meaningful. Only one tracked fetch exists per key at a
      // time, since a second unforced call joins the first instead of starting its own.
      if (this.latest.get(key) === token) {
        this.latest.delete(key);
      }
      if (trackInFlight) {
        this.inFlight.delete(key);
      }
    }
  }

  private cacheKey(request: GetActionParameterOptionsRequest): string {
    // The first three fields are what invalidate() prefix-matches on; keep them leading.
    return [
      request.integrationId,
      request.actionId,
      request.parameterName,
      request.eventId ?? '',
      request.eventParameterKind ?? '',
      request.optionsSourceId ?? '',
      (request.widgetTypes ?? []).join(','),
      request.filter ?? '',
      JSON.stringify(request.currentParameters ?? {}),
    ].join('|');
  }
}

export type { ActionParameterOption };
