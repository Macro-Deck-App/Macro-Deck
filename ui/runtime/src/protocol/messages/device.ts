import { ResultResponse } from './common';
import { LocalizedText } from '../../localization/localized-text';

export type DeviceClientType = 'web-client' | 'admin-ui' | 'native' | 'provider' | 'unknown';

export type DeviceFormFactor = 'phone' | 'tablet' | 'desktop' | 'unknown';

export interface Device {
  id: string;
  name: string;
  nameIsCustom: boolean;
  proposedName?: string;
  clientType: DeviceClientType;
  formFactor: DeviceFormFactor;
  platform?: string;
  browser?: string;
  appVersion?: string;
  online: boolean;
  connectionCount: number;
  hasActiveSession: boolean;
  lastSeenAt: string;
  createdAt: string;
  startupProfileId?: string;
  startupProfileName?: string;

  providerId?: string;
  providerName?: LocalizedText;
  providerDeviceId?: string;
  model?: string;
  manufacturer?: string;
  layoutReference?: string;
}

export interface GetDevicesResponse {
  devices: Device[];
}

export interface RenameDeviceRequest {
  name: string;
}

export interface RenameDeviceResponse extends ResultResponse {
  device?: Device;
}

export interface SetDeviceStartupProfileRequest {
  profileId: string | null;
}

export interface SetDeviceStartupProfileResponse extends ResultResponse {
  device?: Device;
}

export interface OpenProfileOnDeviceRequest {
  profileId: string;
}

export interface OpenProfileOnDeviceResponse extends ResultResponse {}

export interface LogoutDeviceResponse extends ResultResponse {}

export interface RemoveDeviceResponse extends ResultResponse {}

export interface DeviceChangedEvent {
  device: Device;
}

export interface DeviceRemovedEvent {
  deviceId: string;
}

export interface DeviceSessionRevokedEvent {}

export interface DeviceLoginInfo {
  deviceId?: string;
  deviceSecret?: string;
  clientType?: DeviceClientType;
  proposedName?: string;
  platform?: string;
  browser?: string;
  formFactor?: DeviceFormFactor;
  appVersion?: string;
}

export interface DeviceCredential {
  deviceId: string;
  deviceSecret?: string;
  startupProfileId?: string;
}
