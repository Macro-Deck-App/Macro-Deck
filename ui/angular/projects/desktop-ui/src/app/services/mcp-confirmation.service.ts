import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { TransportError } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { McpPendingConfirmation } from '../shared/transport/mcp-messages';

@Injectable({ providedIn: 'root' })
export class McpConfirmationService {
  private readonly api = inject(ApiService);

  readonly pending = signal<McpPendingConfirmation[]>([]);
  readonly unavailable = signal(false);

  private inFlightLoad: Promise<void> | null = null;

  readonly current = computed<McpPendingConfirmation | null>(() => {
    const now = Date.now();
    return this.pending()
      .filter(confirmation => new Date(confirmation.expiresAt).getTime() > now)
      .reduce<McpPendingConfirmation | null>((oldest, confirmation) =>
        !oldest || new Date(confirmation.createdAt).getTime() < new Date(oldest.createdAt).getTime()
          ? confirmation
          : oldest, null);
  });

  constructor() {
    this.api.onNotification('McpConfirmationsChangedEvent').subscribe(() => {
      void this.load();
    });

    effect(() => {
      if (this.api.connectionStateSignal() === 'connected') {
        void this.load();
      }
    });
  }

  load(): Promise<void> {
    this.inFlightLoad ??= this.runLoad().finally(() => {
      this.inFlightLoad = null;
    });
    return this.inFlightLoad;
  }

  async allow(id: string, alwaysAllow: boolean): Promise<boolean> {
    const response = await this.api.allowMcpConfirmation(id, alwaysAllow);
    this.remove(id);
    return response.success;
  }

  async decline(id: string): Promise<boolean> {
    const response = await this.api.declineMcpConfirmation(id);
    this.remove(id);
    return response.success;
  }

  expireLocally(id: string): void {
    this.remove(id);
  }

  private async runLoad(): Promise<void> {
    if (this.unavailable()) {
      return;
    }

    try {
      const response = await this.api.getMcpConfirmations();
      this.pending.set(response.confirmations ?? []);
    } catch (error) {
      if (error instanceof TransportError && error.status === 403) {
        this.unavailable.set(true);
        this.pending.set([]);
        return;
      }
      console.error('Failed to load pending AI client requests:', error);
    }
  }

  private remove(id: string): void {
    this.pending.update(list => list.filter(confirmation => confirmation.id !== id));
  }
}
