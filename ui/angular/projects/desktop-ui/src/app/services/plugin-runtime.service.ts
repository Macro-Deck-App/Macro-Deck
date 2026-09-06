import { Injectable, effect, inject, signal } from '@angular/core';
import { AppStrings, PluginRuntimeInfo, PluginRuntimeOperationResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

@Injectable({ providedIn: 'root' })
export class PluginRuntimeService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly plugins = signal<PluginRuntimeInfo[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly busy = signal<ReadonlySet<string>>(new Set());

  private inFlightLoad: Promise<void> | null = null;

  constructor() {
    this.api.onNotification('PluginRuntimeChangedEvent').subscribe(() => {
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

  private async runLoad(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const response = await this.api.getPluginRuntime();
      this.plugins.set(response.plugins ?? []);
    } catch (error) {
      console.error('Failed to load plugin runtime state:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.PluginRuntime.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  async start(pluginId: string): Promise<PluginRuntimeOperationResponse> {
    return this.runMutation(pluginId, () => this.api.startPlugin(pluginId));
  }

  async stop(pluginId: string): Promise<PluginRuntimeOperationResponse> {
    return this.runMutation(pluginId, () => this.api.stopPlugin(pluginId));
  }

  async restart(pluginId: string): Promise<PluginRuntimeOperationResponse> {
    return this.runMutation(pluginId, () => this.api.restartPlugin(pluginId));
  }

  private async runMutation(
    pluginId: string,
    call: () => Promise<PluginRuntimeOperationResponse>,
  ): Promise<PluginRuntimeOperationResponse> {
    this.setBusy(pluginId, true);
    try {
      const response = await call();
      // The push notification the supervisor fires on a real change may already have raced this
      // reply in; loading again is cheap and guarantees the cache reflects the outcome either way.
      void this.load();
      return response;
    } finally {
      this.setBusy(pluginId, false);
    }
  }

  private setBusy(pluginId: string, value: boolean): void {
    this.busy.update(current => {
      const next = new Set(current);
      if (value) {
        next.add(pluginId);
      } else {
        next.delete(pluginId);
      }
      return next;
    });
  }
}
