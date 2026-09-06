import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { AppStrings, PairedPlugin, PendingPluginPairingRequest, TransportError } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

@Injectable({ providedIn: 'root' })
export class PluginPairingService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly pendingRequests = signal<PendingPluginPairingRequest[]>([]);
  readonly pairedPlugins = signal<PairedPlugin[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  private inFlightLoad: Promise<void> | null = null;

  readonly unavailable = signal(false);

  readonly current = computed<PendingPluginPairingRequest | null>(() => {
    const now = Date.now();
    const live = this.pendingRequests().filter(request => new Date(request.expiresAt).getTime() > now);
    if (live.length === 0) {
      return null;
    }
    return live.reduce((oldest, request) =>
      new Date(request.createdAt).getTime() < new Date(oldest.createdAt).getTime() ? request : oldest);
  });

  readonly queuedCount = computed(() => {
    const current = this.current();
    if (!current) {
      return 0;
    }
    const now = Date.now();
    return this.pendingRequests()
      .filter(request => request.requestId !== current.requestId)
      .filter(request => new Date(request.expiresAt).getTime() > now)
      .length;
  });

  constructor() {
    this.subscribeToEvents();

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

  private async runLoad(): Promise<void> {
    if (this.unavailable()) {
      return;
    }

    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const [requestsResponse, registrationsResponse] = await Promise.all([
        this.api.getPluginPairingRequests(),
        this.api.getPairedPlugins(),
      ]);
      this.pendingRequests.set(requestsResponse.requests ?? []);
      this.pairedPlugins.set(registrationsResponse.registrations ?? []);
    } catch (error) {
      if (error instanceof TransportError && error.status === 403) {
        this.unavailable.set(true);
        this.pendingRequests.set([]);
        this.pairedPlugins.set([]);
        return;
      }
      console.error('Failed to load plugin pairing state:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.PluginPairing.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  async approve(requestId: string, replaceExistingRegistration: boolean): Promise<boolean> {
    const response = await this.api.approvePluginPairingRequest(requestId, { replaceExistingRegistration });
    if (response.success) {
      this.removePending(requestId);
    }
    return response.success;
  }

  async reject(requestId: string): Promise<boolean> {
    const response = await this.api.rejectPluginPairingRequest(requestId);
    if (response.success) {
      this.removePending(requestId);
    }
    return response.success;
  }

  expireLocally(requestId: string): void {
    this.removePending(requestId);
  }

  async revoke(pluginId: string): Promise<void> {
    await this.api.revokePairedPlugin(pluginId);
    this.pairedPlugins.update(list => list.filter(plugin => plugin.pluginId !== pluginId));
  }

  private removePending(requestId: string): void {
    this.pendingRequests.update(list => list.filter(request => request.requestId !== requestId));
  }

  private subscribeToEvents(): void {
    this.api.onNotification('PluginPairingRequestsChangedEvent').subscribe(() => {
      void this.load();
    });

    // Revoking a paired registration through the Plugin credentials tab (rather than through this
    // service's own revoke()) still needs the pairing cache repaired, so it does not go on showing a
    // plugin that is no longer paired.
    this.api.onNotification('PluginTokensChangedEvent').subscribe(() => {
      void this.load();
    });
  }
}
