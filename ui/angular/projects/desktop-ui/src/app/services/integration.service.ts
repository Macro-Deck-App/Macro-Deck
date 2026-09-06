import { Injectable, inject, signal, computed } from '@angular/core';
import { IntegrationIssueSeverity, IntegrationIssuesChangedEvent, IpcIntegration, IpcProvidedCapability, resolveLocalizedText } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

export interface Integration {
  id: string;
  name: string;
  version: string;
  isInternal: boolean;
  enabled: boolean;
  actionCount: number;
  variableCount: number;
  supportsConfigFlow: boolean;
  allowsMultipleConfigurations: boolean;
  configuredEntryCount: number;
  hasIcon: boolean;
  iconVersion: string | null;
  issueCount: number;
  issueSeverity: IntegrationIssueSeverity | null;
  isInitialized: boolean;
  variablesDependOnConfiguration: boolean;
  providedCapabilities: IpcProvidedCapability[];
}

@Injectable({
  providedIn: 'root'
})
export class IntegrationService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly integrations = signal<Integration[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly selectedIntegrationId = signal<string | null>(null);

  readonly selectedIntegration = computed(() => {
    const id = this.selectedIntegrationId();
    if (!id) return null;
    return this.integrations().find(i => i.id === id) ?? null;
  });

  readonly enabledCount = computed(() =>
    this.integrations().filter(i => i.enabled).length
  );

  constructor() {
    this.subscribeToEvents();
  }

  private subscribeToEvents(): void {
    this.api.onNotification<IntegrationIssuesChangedEvent>('IntegrationIssuesChangedEvent').subscribe(event => {
      this.integrations.update(integrations =>
        integrations.map(i =>
          i.id === event.integrationId
            ? { ...i, issueCount: event.issueCount, issueSeverity: event.severity }
            : i
        )
      );
    });
  }

  async loadIntegrations(): Promise<void> {
    this.isLoading.set(true);
    this.loadError.set(null);

    try {
      const response = await this.api.getIntegrations();
      const integrations = (response.integrations || []).map(this.mapIntegration);
      this.integrations.set(integrations);
    } catch (error) {
      console.error('Failed to load integrations:', error);
      this.loadError.set('Failed to load integrations');
    } finally {
      this.isLoading.set(false);
    }
  }

  selectIntegration(id: string | null): void {
    this.selectedIntegrationId.set(id);
  }

  async toggleIntegration(id: string): Promise<void> {
    const integration = this.integrations().find(i => i.id === id);
    if (!integration) return;

    const newEnabled = !integration.enabled;

    this.integrations.update(integrations =>
      integrations.map(i =>
        i.id === id ? { ...i, enabled: newEnabled } : i
      )
    );

    try {
      const response = await this.api.setIntegrationEnabled({ id, enabled: newEnabled });
      if (!response.success) {
        this.integrations.update(integrations =>
          integrations.map(i =>
            i.id === id ? { ...i, enabled: !newEnabled } : i
          )
        );
        console.error('Failed to toggle integration:', response.error?.message);
      }
    } catch (error) {
      this.integrations.update(integrations =>
        integrations.map(i =>
          i.id === id ? { ...i, enabled: !newEnabled } : i
        )
      );
      console.error('Failed to toggle integration:', error);
    }
  }

  private readonly mapIntegration = (integration: IpcIntegration): Integration => ({
    id: integration.id,
    name: resolveLocalizedText(integration.name, this.localization),
    version: integration.version,
    isInternal: integration.isInternal ?? true,
    enabled: integration.enabled,
    actionCount: integration.actionCount ?? 0,
    variableCount: integration.variableCount ?? 0,
    supportsConfigFlow: integration.supportsConfigFlow ?? false,
    allowsMultipleConfigurations: integration.allowsMultipleConfigurations ?? true,
    configuredEntryCount: integration.configuredEntryCount ?? 0,
    hasIcon: integration.hasIcon ?? false,
    iconVersion: integration.iconVersion ?? null,
    issueCount: integration.issueCount ?? 0,
    issueSeverity: integration.issueSeverity ?? null,
    isInitialized: integration.isInitialized ?? false,
    variablesDependOnConfiguration: integration.variablesDependOnConfiguration ?? false,
    providedCapabilities: integration.providedCapabilities ?? [],
  });

  async refreshConfigEntries(id: string): Promise<void> {
    try {
      const response = await this.api.getConfigEntries(id);
      this.integrations.update(integrations =>
        integrations.map(i =>
          i.id === id ? { ...i, configuredEntryCount: response.entries.length } : i
        )
      );
    } catch (error) {
      console.error('Failed to refresh config entries:', error);
    }
  }
}
