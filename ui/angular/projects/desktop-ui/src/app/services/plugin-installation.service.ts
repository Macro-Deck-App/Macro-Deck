import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { AppStrings, InstalledPlugin, PluginInstallActionResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { ArchiveSource } from './portability.service';

@Injectable({ providedIn: 'root' })
export class PluginInstallationService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly plugins = signal<InstalledPlugin[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly installedIds = computed(() => new Set(this.plugins().map(p => p.pluginId)));

  private inFlightLoad: Promise<void> | null = null;

  constructor() {
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
      const response = await this.api.getInstalledPlugins();
      this.plugins.set(response.plugins ?? []);
    } catch (error) {
      console.error('Failed to load installed plugins:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.PluginInstallation.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  find(pluginId: string): InstalledPlugin | null {
    return this.plugins().find(plugin => plugin.pluginId === pluginId) ?? null;
  }

  inspect(source: ArchiveSource): Promise<PluginInstallActionResponse> {
    return source.kind === 'file'
      ? this.api.inspectPluginArtifact(source.file)
      : this.api.inspectPluginArtifactPath(source.path);
  }

  async install(source: ArchiveSource,
    force?: boolean,
    allowUnsigned?: boolean): Promise<PluginInstallActionResponse> {
    const response = source.kind === 'file'
      ? await this.api.installPluginArtifact(source.file, force, allowUnsigned)
      : await this.api.installPluginArtifactPath(source.path, force, allowUnsigned);
    if (response.success) {
      await this.load();
    }
    return response;
  }

  async uninstall(pluginId: string, keepData: boolean): Promise<PluginInstallActionResponse> {
    const response = await this.api.uninstallPlugin(pluginId, { keepData });
    if (response.success) {
      await this.load();
    }
    return response;
  }
}
