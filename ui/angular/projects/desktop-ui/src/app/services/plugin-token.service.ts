import { Injectable, effect, inject, signal } from '@angular/core';
import { AppStrings, CreatePluginTokenRequest, CreatePluginTokenResponse, DeletePluginTokenResponse, PluginAccessToken, PluginSessionInfo, RevokePluginTokenResponse, TerminatePluginSessionResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService } from '@shared';

@Injectable({ providedIn: 'root' })
export class PluginTokenService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly tokens = signal<PluginAccessToken[]>([]);
  readonly sessions = signal<PluginSessionInfo[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  private inFlightLoad: Promise<void> | null = null;

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
    this.isLoading.set(true);
    this.loadError.set(null);
    try {
      const [tokensResponse, sessionsResponse] = await Promise.all([
        this.api.getPluginTokens(),
        this.api.getPluginSessions(),
      ]);
      this.tokens.set(tokensResponse.tokens ?? []);
      this.sessions.set(sessionsResponse.sessions ?? []);
    } catch (error) {
      console.error('Failed to load plugin tokens:', error);
      this.loadError.set(error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Errors.PluginToken.LoadFailed));
    } finally {
      this.isLoading.set(false);
    }
  }

  async createToken(request: CreatePluginTokenRequest): Promise<CreatePluginTokenResponse> {
    const response = await this.api.createPluginToken(request);
    if (response.token) {
      this.upsertToken(response.token);
    }
    return response;
  }

  async revokeToken(id: string): Promise<RevokePluginTokenResponse> {
    const response = await this.api.revokePluginToken(id);
    if (response.success) {
      const revokedAt = new Date().toISOString();
      this.tokens.update(list => list.map(token =>
        token.id === id ? { ...token, revokedAt, activeSessionCount: 0 } : token));
      this.sessions.update(list => list.map(session =>
        session.tokenId === id ? { ...session, state: 'dropped' as const } : session));
    }
    return response;
  }

  async deleteToken(id: string): Promise<DeletePluginTokenResponse> {
    const response = await this.api.deletePluginToken(id);
    if (response.success) {
      this.tokens.update(list => list.filter(token => token.id !== id));
    }
    return response;
  }

  async terminateSession(sessionId: string): Promise<TerminatePluginSessionResponse> {
    const response = await this.api.terminatePluginSession(sessionId);
    if (response.success) {
      this.sessions.update(list => list.map(session =>
        session.sessionId === sessionId ? { ...session, state: 'dropped' as const } : session));
    }
    return response;
  }

  private subscribeToEvents(): void {
    this.api.onNotification('PluginTokensChangedEvent').subscribe(() => {
      void this.load();
    });

    this.api.onNotification('PluginSessionsChangedEvent').subscribe(() => {
      void this.load();
    });
  }

  private upsertToken(token: PluginAccessToken): void {
    this.tokens.update(list => {
      const idx = list.findIndex(t => t.id === token.id);
      if (idx === -1) return [...list, token];
      const next = list.slice();
      next[idx] = token;
      return next;
    });
  }
}
