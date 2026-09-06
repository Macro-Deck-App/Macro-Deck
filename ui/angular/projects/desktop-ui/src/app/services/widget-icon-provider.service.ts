import { Injectable, inject } from '@angular/core';
import { GetActionProviderIconRequest, GetActionProviderIconResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';

// Per-block request sequencing and in-flight de-duplication, for the same race
// ButtonStateProviderService guards (issue #425). Deliberately uncached beyond that: a button's
// set of states rarely changes, but an icon provider's answer is expected to (the next track, the
// next reading), so every call reaches the host rather than serving a stale earlier success.
@Injectable({ providedIn: 'root' })
export class WidgetIconProviderService {
  private readonly api = inject(ApiService);
  private readonly inFlight = new Map<string, Promise<GetActionProviderIconResponse>>();

  private readonly requestSeq = new Map<string, number>();

  async getIconPreview(
    blockId: string,
    request: GetActionProviderIconRequest,
  ): Promise<GetActionProviderIconResponse | null> {
    const seq = (this.requestSeq.get(blockId) ?? 0) + 1;
    this.requestSeq.set(blockId, seq);

    const key = this.cacheKey(request);
    let pending = this.inFlight.get(key);
    if (!pending) {
      pending = this.fetch(key, request);
      this.inFlight.set(key, pending);
    }

    const result = await pending;
    return this.isCurrent(blockId, seq) ? result : null;
  }

  private isCurrent(blockId: string, seq: number): boolean {
    return this.requestSeq.get(blockId) === seq;
  }

  private async fetch(key: string, request: GetActionProviderIconRequest): Promise<GetActionProviderIconResponse> {
    try {
      return await this.api.getActionProviderIcon(request);
    } finally {
      this.inFlight.delete(key);
    }
  }

  private cacheKey(request: GetActionProviderIconRequest): string {
    return [request.integrationId, request.actionId, JSON.stringify(request.parameters ?? {})].join('|');
  }
}
