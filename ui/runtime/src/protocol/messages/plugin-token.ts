import { ResultResponse } from './common';

export interface PluginRegistrationSummary {
  pluginId: string;
  displayName: string;
  createdAt: string;
  lastSeenAt?: string | null;
  revokedAt?: string | null;
  online: boolean;
}

export interface PluginAccessToken {
  id: string;
  name: string;
  scopes: string[];
  createdAt: string;
  expiresAt?: string | null;
  lastUsedAt?: string | null;
  revokedAt?: string | null;
  registrations: PluginRegistrationSummary[];
  activeSessionCount: number;
}

export interface GetPluginTokensResponse {
  tokens: PluginAccessToken[];
}

export interface CreatePluginTokenRequest {
  name: string;
  expiresInDays?: number | null;
}

export interface CreatePluginTokenResponse {
  token: PluginAccessToken;
  plaintext: string;
}

export interface RevokePluginTokenResponse extends ResultResponse {
  terminatedSessions: number;
}

export interface DeletePluginTokenResponse extends ResultResponse {}

export type PluginSessionOrigin = 'managed' | 'self-registered';

export type PluginSessionState = 'awaiting' | 'connected' | 'dropped';

export interface PluginSessionInfo {
  sessionId: string;
  pluginId: string;
  displayName?: string | null;
  instanceId?: string | null;
  tokenId?: string | null;
  origin: PluginSessionOrigin;
  negotiatedVersion: number;
  state: PluginSessionState;
  connectedAt: string;
  lastSeenAt: string;
}

export interface GetPluginSessionsResponse {
  sessions: PluginSessionInfo[];
}

export interface TerminatePluginSessionResponse extends ResultResponse {}
