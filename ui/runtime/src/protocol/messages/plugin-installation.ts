import { ApiError } from './common';

export type PluginSignatureVerification = 'not_signed' | 'unverified' | 'valid' | 'invalid';

export type PluginTrustCategory =
  | 'trusted'
  | 'unsigned'
  | 'malformed'
  | 'signature_invalid'
  | 'content_mismatch'
  | 'untrusted_root'
  | 'wrong_certificate_purpose'
  | 'certificate_not_valid_at_signature'
  | 'revoked'
  | 'revocation_unavailable'
  | 'verification_unavailable';

export type PluginInstallWarningSeverity = 'advisory' | 'blocking';

export interface PluginInstallWarning {
  code: string;
  severity: PluginInstallWarningSeverity;
  message: string;
  subjectId?: string | null;
}

export interface PluginSignatureInfo {
  verification: PluginSignatureVerification;
  message?: string | null;
  algorithm?: string | null;
  keyId?: string | null;
  category?: PluginTrustCategory | string | null;
  certificateId?: string | null;
  trusted?: boolean | null;
}

export interface PluginPublisherInfo {
  name: string;
  id?: string | null;
  url?: string | null;
}

export interface PluginArtifactInfo {
  name: string;
  description?: string | null;
  iconDataUri?: string | null;
  supportedOnThisPlatform: boolean;
}

export interface PluginInstallActionResponse {
  success: boolean;
  pluginId?: string | null;
  version?: string | null;
  previousVersion?: string | null;
  activated: boolean;
  rolledBack: boolean;
  warnings: PluginInstallWarning[];
  error?: ApiError | null;
  publisher?: PluginPublisherInfo | null;
  signature?: PluginSignatureInfo | null;
  artifact?: PluginArtifactInfo | null;
}

export interface InstalledPluginVersion {
  version: string;
  active: boolean;
}

export interface InstalledPlugin {
  pluginId: string;
  name: string;
  description?: string | null;
  publisherName?: string | null;
  license?: string | null;
  homepage?: string | null;
  versions: InstalledPluginVersion[];
  activeVersion?: string | null;
  permissions: string[];
  trustVerdict?: PluginTrustCategory | string | null;
  certificateId?: string | null;
}

export interface GetInstalledPluginsResponse {
  plugins: InstalledPlugin[];
}

export const PLUGIN_INSTALL_ERROR_ALREADY_INSTALLED = 'already_installed';

export const PLUGIN_WARNING_ARTIFACT_UNSIGNED = 'unsigned';
export const PLUGIN_WARNING_SIGNATURE_UNVERIFIED = 'signature_unverified';
