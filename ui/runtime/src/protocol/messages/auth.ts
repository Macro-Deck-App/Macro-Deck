import type { DeviceCredential, DeviceLoginInfo } from './device';

export type AuthScope = 'admin' | 'client';

export interface AuthStatusResponse {
  setupComplete: boolean;
  authenticated: boolean;
  trusted: boolean;
  scope?: AuthScope | null;
  username?: string | null;
}

export interface SetupRequest {
  username: string;
  password: string;
}

export interface LoginRequest {
  username: string;
  password: string;
  scope: AuthScope;
  device?: DeviceLoginInfo;
}

export interface TokenResponse {
  accessToken: string;
  expiresInSeconds: number;
  scope: AuthScope;
  username: string;
  device?: DeviceCredential;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface ChangeUsernameRequest {
  currentPassword: string;
  newUsername: string;
}

export interface PairingCodeResponse {
  code: string;
  expiresAt: string;
}

export interface RedeemDeviceEnrollmentRequest {
  token: string;
  device: DeviceLoginInfo;
}
