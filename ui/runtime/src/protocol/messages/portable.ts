import { LocalizedText } from '../../localization/localized-text';
import { ResultResponse } from './common';
import { IpcFolder } from './folder';
import { IpcProfile } from './profile';
import { IpcWidget } from './widget';

export interface ExportArchiveRequest {
  includeIcons: boolean;
  includeSecrets: boolean;
  password?: string;
}

export type ExportProfileRequest = ExportArchiveRequest;

export interface ImportProfileResponse extends ResultResponse {
  profile?: IpcProfile;
}

export interface ExportFolderRequest extends ExportArchiveRequest {
  includeSubfolders: boolean;
}

export interface ImportFolderResponse extends ResultResponse {
  folder?: IpcFolder;
  folderCount: number;
}

export interface ExportWidgetsRequest extends ExportArchiveRequest {
  folderId: string;
  widgetIds: string[];
}

export interface ImportWidgetsResponse extends ResultResponse {
  widgets: IpcWidget[];
}

export type ArchiveIntegrationAvailability = 'Ready' | 'NotConfigured' | 'Disabled' | 'Missing';

export interface ArchiveIntegration {
  id: string;
  name: LocalizedText;
  version: string;
  requiresConfiguration: boolean;
  availability: ArchiveIntegrationAvailability;
}

export interface ArchiveSummary {
  kind: 'Profile' | 'Folder' | 'Widgets';
  name: string;
  appVersion: string;
  createdAt: string;
  encrypted: boolean;
  includesSecrets: boolean;
  folderCount: number;
  widgetCount: number;
  iconCount: number;
  scriptCount: number;
  variableCount: number;
  secretCount: number;
  integrations: ArchiveIntegration[];
}

export interface InspectArchiveResponse extends ResultResponse {
  archive?: ArchiveSummary;
}
