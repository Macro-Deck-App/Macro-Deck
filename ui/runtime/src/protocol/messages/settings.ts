import { LogEntryLevel } from './logs';

export type ThemeMode = 'light' | 'dark' | 'system';

export interface GetAppearanceSettingsResponse {
  themeMode: ThemeMode;
  accentColor: string;
}

export interface UpdateAppearanceSettingsRequest {
  themeMode: ThemeMode;
  accentColor: string;
}

export interface UpdateAppearanceSettingsResponse {
  themeMode: ThemeMode;
  accentColor: string;
}

export interface AppearanceChangedEvent {
  themeMode: ThemeMode;
  accentColor: string;
}

export interface GetLoggingSettingsResponse {
  minimumLevel: LogEntryLevel;
  defaultMinimumLevel: LogEntryLevel;
}

export interface UpdateLoggingSettingsRequest {
  minimumLevel: LogEntryLevel;
}

export interface UpdateLoggingSettingsResponse {
  minimumLevel: LogEntryLevel;
  defaultMinimumLevel: LogEntryLevel;
}

export interface GetAutostartSettingsResponse {
  supported: boolean;
  enabled: boolean;
  openMinimized: boolean;
}

export interface UpdateAutostartSettingsRequest {
  enabled: boolean;
  openMinimized: boolean;
}

export interface UpdateAutostartSettingsResponse {
  success: boolean;
  error: string | null;
  supported: boolean;
  enabled: boolean;
  openMinimized: boolean;
}

export type TlsMode = 'Replace' | 'Additional';
export type ActiveTlsMode = 'Disabled' | TlsMode;
export type TlsCertificateSource = 'SelfSigned' | 'Custom' | 'LocalCa';

export interface GetNetworkSettingsResponse {
  publicPort: number;
  defaultPublicPort: number;
  activePublicPort: number;
  overriddenByEnvironment: boolean;
  configuredPortIgnored: boolean;
  publicListenerUnavailable: boolean;
  restartRequired: boolean;
  restartSupported: boolean;
  restartUnsupportedReason: string | null;
  minimumPublicPort: number;
  maximumPublicPort: number;

  tlsEnabled: boolean;
  tlsMode: TlsMode;
  tlsHttpsPort: number;
  defaultTlsHttpsPort: number;

  activeTlsEnabled: boolean;
  activeTlsMode: ActiveTlsMode;
  activeTlsHttpsPort: number | null;

  tlsFailure: string;
  tlsRejection: string;

  tlsCertificateConfigured: boolean;
  tlsCertificateSource: TlsCertificateSource | null;
  tlsCertificateSubject: string | null;
  tlsCertificateFingerprint: string | null;
  tlsCertificateNotBefore: string | null;
  tlsCertificateNotAfter: string | null;
  tlsCertificateExpired: boolean;
  tlsCertificateNotYetValid: boolean;

  tlsCertificateIssuedByAuthority: boolean;
  tlsAuthorityConfigured: boolean;
  tlsAuthoritySubject: string | null;
  tlsAuthorityFingerprint: string | null;
  tlsAuthorityNotBefore: string | null;
  tlsAuthorityNotAfter: string | null;
}

export interface UpdateNetworkSettingsRequest {
  publicPort: number;
  tlsEnabled?: boolean;
  tlsMode?: TlsMode;
  tlsHttpsPort?: number;
}

export interface UpdateNetworkSettingsResponse extends GetNetworkSettingsResponse {
  success: boolean;
  error: string | null;
}

export interface UpdateNetworkTlsCertificateRequest {
  certificatePem: string;
  privateKeyPem: string;
}

export interface RestartApplicationRequest {
  reason?: string;
}

export interface RestartApplicationResponse {
  success: boolean;
  supported: boolean;
  error: string | null;
}

export interface GetDataDirectoryResponse {
  path: string;
  canOpen: boolean;
}

export interface OpenDataDirectoryResponse {
  success: boolean;
  error: string | null;
}

export type AdbDeviceState =
  | 'Unknown'
  | 'Device'
  | 'Unauthorized'
  | 'Offline'
  | 'Authorizing'
  | 'NoPermissions'
  | 'Bootloader'
  | 'Recovery'
  | 'Sideload'
  | 'Disconnected';

export interface AdbDevice {
  serial: string;
  state: AdbDeviceState;
  model: string | null;
  manufacturer: string | null;
  authorized: boolean;
  isDefault: boolean;
  tunnelEstablished: boolean;
  tunnelDevicePort: number | null;
  tunnelError: string | null;
}

export interface GetAdbSettingsResponse {
  enabled: boolean;
  executablePath: string | null;
  resolvedExecutablePath: string | null;
  executableSource: string;
  adbVersion: string | null;
  serverReachable: boolean;
  serverStartedByMacroDeck: boolean;
  usbConnectionsEnabled: boolean;
  defaultDeviceSerial: string | null;
  activePublicPort: number;
  deviceSidePortCandidates: number[];
  devices: AdbDevice[];
  lastError: string | null;
  lastErrorAt: string | null;
  previousShutdownWasUnclean: boolean;
  staleTunnelsCleaned: number;
  supported: boolean;
  unsupportedReason: string | null;
}

export interface UpdateAdbSettingsRequest {
  enabled?: boolean;
  executablePath?: string;
  usbConnectionsEnabled?: boolean;
  defaultDeviceSerial?: string;
}

export interface UpdateAdbSettingsResponse extends GetAdbSettingsResponse {
  success: boolean;
  error: string | null;
}

export interface RestartAdbServerResponse extends GetAdbSettingsResponse {
  success: boolean;
  error: string | null;
}

export interface DownloadAdbPlatformToolsResponse extends GetAdbSettingsResponse {
  success: boolean;
  error: string | null;
}

export interface AdbStateChangedEvent {
  changedAt: string;
}

export interface GetDeveloperSettingsResponse {
  enabled: boolean;
}

export interface UpdateDeveloperSettingsRequest {
  enabled?: boolean;
}

export interface DeveloperSettingsChangedEvent {
  enabled: boolean;
}

/**
 * The first-launch onboarding wizard (issue #893). Armed by account setup and cleared once the user
 * skips or finishes it, so the wizard is owed exactly once per installation.
 */
export interface GetOnboardingStateRequest {}

export interface GetOnboardingStateResponse {
  pending: boolean;
}

export interface CompleteOnboardingRequest {}

export interface CompleteOnboardingResponse {
  pending: boolean;
}

export interface UpdateDeveloperSettingsResponse {
  enabled: boolean;
}

export interface GetLockScreenSettingsResponse {
  enabled: boolean;
}

export interface UpdateLockScreenSettingsRequest {
  enabled: boolean;
}

export interface UpdateLockScreenSettingsResponse {
  enabled: boolean;
}
