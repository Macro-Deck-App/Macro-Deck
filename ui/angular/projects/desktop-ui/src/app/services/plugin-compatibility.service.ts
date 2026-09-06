import { Injectable, effect, inject, signal } from '@angular/core';
import { AppStrings, PluginCompatibilityReport } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

@Injectable({ providedIn: 'root' })
export class PluginCompatibilityService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly reports = signal<PluginCompatibilityReport[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  private inFlightLoad: Promise<void> | null = null;

  constructor() {
    this.api.onNotification('PluginCompatibilityChangedEvent').subscribe(() => {
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
      const response = await this.api.getPluginCompatibility();
      this.reports.set(response.plugins ?? []);
    } catch (error) {
      console.error('Failed to load plugin compatibility state:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.PluginCompatibility.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  forPlugin(pluginId: string): PluginCompatibilityReport | null {
    return this.reports().find(report => report.pluginId === pluginId) ?? null;
  }
}
