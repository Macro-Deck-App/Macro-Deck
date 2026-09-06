import { LocalizedText } from '../../localization/localized-text';
import { ResultResponse } from './common';

export interface MigrationSourceDto {
  id: string;
  name: string;
  defaultPath?: string;
}

export interface MigrationSourcesResponse {
  sources: MigrationSourceDto[];
}

export interface MigrationRequestBody {
  sourceId: string;
  path: string;
  decryptionKey?: string;
  skipDecryption: boolean;
}

export interface MigrationWarningDto {
  kind: string;
  subject: string;
  detail: LocalizedText;
}

export interface MigrationUnsupportedActionDto {
  sourcePlugin: string;
  sourceAction: string;
  occurrences: number;
}

export interface MigrationIntegrationDto {
  id: string;
  title: string;
  carriesCredentials: boolean;
}

export type MigrationCredentialStatus = 'NotPresent' | 'Decrypted' | 'KeyUnavailable' | 'KeyRejected' | 'Skipped';

export interface MigrationSummary {
  sourceId: string;
  sourceName: string;
  profileCount: number;
  folderCount: number;
  widgetCount: number;
  iconCount: number;
  variableCount: number;
  migratedActionCount: number;
  unsupportedActionCount: number;
  credentialStatus: MigrationCredentialStatus;
  integrations: MigrationIntegrationDto[];
  unsupportedActions: MigrationUnsupportedActionDto[];
  warnings: MigrationWarningDto[];
}

export interface MigrationPreviewResponse extends ResultResponse {
  summary?: MigrationSummary;
}

export interface MigrationImportResponse extends ResultResponse {
  summary?: MigrationSummary;
  profileIds: string[];
}
