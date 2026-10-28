import { Injectable, inject, signal } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';
import { ClaudeDesktopExtensionDownload, CreateMcpTokenRequest, CreateMcpTokenResponse, McpConnection, McpEndpoint, McpProviderPolicy, McpRiskMode, McpToken, McpTokenActionResponse } from '../shared/transport/mcp-messages';

@Injectable({ providedIn: 'root' })
export class McpSettingsService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly endpoint = signal<McpEndpoint | null>(null);
  readonly tokens = signal<McpToken[]>([]);
  readonly connections = signal<McpConnection[]>([]);
  readonly providers = signal<McpProviderPolicy[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const [endpoint, tokens, connections, policy] = await Promise.all([
        this.api.getMcpEndpoint(),
        this.api.getMcpTokens(),
        this.api.getMcpConnections(),
        this.api.getMcpPolicy(),
      ]);
      this.endpoint.set(endpoint);
      this.tokens.set(tokens.tokens ?? []);
      this.connections.set(connections.connections ?? []);
      this.providers.set(policy.providers ?? []);
    } catch {
      this.loadError.set(this.localization.translateKey(AppStrings.Settings.Mcp.LoadFailed));
    } finally {
      this.loading.set(false);
    }
  }

  async createToken(request: CreateMcpTokenRequest): Promise<CreateMcpTokenResponse> {
    const response = await this.api.createMcpToken(request);
    if (response.success) {
      await this.load();
    }
    return response;
  }

  async revokeToken(id: string): Promise<McpTokenActionResponse> {
    const response = await this.api.revokeMcpToken(id);
    await this.load();
    return response;
  }

  async setPolicy(integrationId: string, mode: McpRiskMode | null): Promise<McpTokenActionResponse> {
    const response = await this.api.setMcpPolicy({ integrationId, mode });
    if (response.success) {
      this.providers.update(providers => providers.map(provider =>
        provider.integrationId === integrationId ? { ...provider, override: mode } : provider));
    }
    return response;
  }

  downloadClaudeDesktopExtension(id: string, token: string): Promise<ClaudeDesktopExtensionDownload> {
    return this.api.downloadClaudeDesktopExtension(id, token);
  }

  async deleteToken(id: string): Promise<McpTokenActionResponse> {
    const response = await this.api.deleteMcpToken(id);
    await this.load();
    return response;
  }
}
