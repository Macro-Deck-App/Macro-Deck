export interface GetSystemFontsRequest {}

export type FontFaceSlant = 'upright' | 'italic' | 'oblique';

export interface SystemFontFace {
  faceId: string;
  family: string;
  weight: number;
  width: number;
  slant: FontFaceSlant;
  styleName: string;
  remoteRenderable: boolean;
}

export interface GetSystemFontsResponse {
  faces: SystemFontFace[];
}

export interface GetAboutInfoResponse {
  version: string;
  isBeta: boolean;
  isDevelopmentBuild: boolean;
  commit: string | null;
  buildTimestamp: string | null;
  buildNumber: string | null;
  license: string;
  runtimeVersion: string;
  operatingSystem: string;
}

export interface ConnectionEndpoint {
  address: string;
  port: number;
  ssl: boolean;
}

export interface GetConnectionInfoResponse {
  instanceName: string;
  endpoints: ConnectionEndpoint[];
  publicListenerUnavailable: boolean;
  version: string;
}

export type ApplicationIdentityKind = 'ExecutablePath' | 'ProcessName' | 'BundleId';

export interface GetApplicationFocusCapabilityRequest {}

export interface GetApplicationFocusCapabilityResponse {
  supported: boolean;
  unsupportedReason?: string;
  preferredIdentityKind: ApplicationIdentityKind;
}

export interface GetRunningApplicationsRequest {
  filter?: string;
}

export interface RunningApplication {
  identity: string;
  identityKind: ApplicationIdentityKind;
  label: string;
}

export interface GetRunningApplicationsResponse {
  applications: RunningApplication[];
}

export interface GetHostLockStateResponse {
  locked: boolean;
  lockScreenEnabled: boolean;
  supported: boolean;
}

export interface HostLockStateChangedEvent {
  locked: boolean;
  lockScreenEnabled: boolean;
  supported: boolean;
}

export interface DeviceSetupCertificateAuthorityInfo {
  available: boolean;
  downloadPath: string;
  subject: string | null;
  fingerprintSha256: string | null;
  notAfter: string | null;
}

export interface GetDeviceSetupResponse {
  instanceName: string;
  httpsEnabled: boolean;
  httpsPort: number | null;
  certificateAuthority: DeviceSetupCertificateAuthorityInfo;
  hostCertificateSubjectAlternativeNames: string[];
}
