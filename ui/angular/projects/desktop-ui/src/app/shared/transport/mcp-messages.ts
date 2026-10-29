import { LocalizedText, ResultResponse } from '@macro-deck/runtime';

export interface McpToken {
  id: string;
  name: string;
  createdAt: string;
  lastUsedAt?: string | null;
  revokedAt?: string | null;
  connectionCount: number;
}

export interface McpConnection {
  tokenId: string;
  tokenName?: string | null;
  clientName?: string | null;
  clientVersion?: string | null;
  connectedAt: string;
  lastSeenAt: string;
}

export interface McpEndpoint {
  available: boolean;
  url?: string | null;
  contractVersion: string;
  extensionAvailable?: boolean;
}

export interface GetMcpTokensResponse {
  tokens: McpToken[];
}

export interface GetMcpConnectionsResponse {
  connections: McpConnection[];
}

export interface CreateMcpTokenRequest {
  name: string;
}

export interface CreateMcpTokenResponse extends ResultResponse {
  token?: McpToken | null;
  plaintext?: string | null;
}

export type McpTokenActionResponse = ResultResponse;

export type ClaudeDesktopExtensionDownload =
  | { ok: true; blob: Blob }
  | { ok: false; response: McpTokenActionResponse };

export type McpRiskMode = 'Allow' | 'Confirm' | 'Off';

export interface McpProviderPolicy {
  integrationId: string;
  name: LocalizedText;
  defaultMode: McpRiskMode;
  override?: McpRiskMode | null;
  confirmedActions: LocalizedText[];
}

export interface GetMcpPolicyResponse {
  providers: McpProviderPolicy[];
}

export interface SetMcpPolicyRequest {
  integrationId: string;
  mode: McpRiskMode | null;
}
