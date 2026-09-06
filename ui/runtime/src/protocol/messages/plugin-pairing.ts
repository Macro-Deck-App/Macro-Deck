import { ResultResponse } from './common';

export interface PluginPairingClientInfo {
  executablePath: string | null;
  processId: number | null;
  sdkVersion: string | null;
}

export interface PendingPluginPairingRequest {
  requestId: string;
  pluginId: string;
  displayName: string;
  client: PluginPairingClientInfo | null;
  createdAt: string;
  expiresAt: string;
  replacesExistingRegistration: boolean;
  existingRegistrationOrigin: string | null;
  existingRegistrationCreatedAt: string | null;
  arrivedOnPublicListener: boolean;
}

export interface GetPluginPairingRequestsResponse {
  requests: PendingPluginPairingRequest[];
}

export interface ApprovePluginPairingRequestRequest {
  replaceExistingRegistration: boolean;
}

export interface PluginPairingActionResponse extends ResultResponse {}

export interface PairedPlugin {
  pluginId: string;
  displayName: string;
  createdAt: string;
  lastSeenAt: string | null;
  online: boolean;
}

export interface GetPairedPluginsResponse {
  registrations: PairedPlugin[];
}
