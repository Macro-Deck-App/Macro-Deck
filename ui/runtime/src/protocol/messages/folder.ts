import { ResultResponse } from './common';
import { IpcWidget } from './widget';
import { ApplicationIdentityKind } from './system';

export interface IpcFolder {
  id: string;
  name: string;
  profileId?: string;
  parentId?: string;
  order: number;
  rows?: number;
  columns?: number;
  backgroundColor?: string;
  widgetSpacing?: number;
  widgetBorderRadius?: number;
  isDefault?: boolean;
  viewId?: string;
  viewConfiguration?: string;
  widgets: IpcWidget[];
}

export interface GetFoldersRequest {}

export interface CreateFolderRequest {
  profileId: string;
  name: string;
  parentId?: string;
  folderViewId?: string;
  folderViewConfiguration?: string;
}

export interface UpdateFolderRequest {
  id: string;
  name?: string;
  parentId?: string;
  order?: number;
  rows?: number;
  columns?: number;
  backgroundColor?: string;
  widgetSpacing?: number;
  widgetBorderRadius?: number;
  isDefault?: boolean;
  folderViewId?: string;
  folderViewConfiguration?: string;
}

export interface DeleteFolderRequest {
  id: string;
}

export interface DuplicateFolderRequest {
  id: string;
}

export interface GetFoldersResponse {
  folders: IpcFolder[];
}

export interface CreateFolderResponse extends ResultResponse {
  folder?: IpcFolder;
}

export interface UpdateFolderResponse extends ResultResponse {
  folder?: IpcFolder;
}

export interface DeleteFolderResponse extends ResultResponse {}

export interface DuplicateFolderResponse extends ResultResponse {
  folder?: IpcFolder;
}

export interface FolderCreatedEvent {
  folder: IpcFolder;
}

export interface FolderUpdatedEvent {
  folder: IpcFolder;
}

export interface FolderDeletedEvent {
  folderId: string;
}

export interface FolderNavigationEvent {
  command: string;
  folderId?: string;
  profileId?: string;
  navigationToken?: string;
}

export interface IpcFolderPlacement {
  id: string;
  parentId: string | null;
  order: number;
  isDefault: boolean;
}

export interface MoveFolderRequest {
  id: string;
  targetId: string;
  position: 'before' | 'after' | 'inside';
}

export interface MoveFolderResponse extends ResultResponse {
  folders?: IpcFolderPlacement[];
}

export interface FoldersReorderedEvent {
  profileId: string;
  folders: IpcFolderPlacement[];
}

export interface FolderFocusRule {
  folderId: string;
  folderName: string;
  profileId: string;

  ruleId: string;
  enabled: boolean;
  applicationIdentity: string;
  identityKind: ApplicationIdentityKind;

  deviceId: string;
  deviceName?: string;
  deviceExists: boolean;

  returnOnFocusLoss: boolean;
}

export interface GetFolderFocusRulesRequest {}

export interface GetFolderFocusRulesResponse {
  rules: FolderFocusRule[];
}

export interface SetFolderFocusRuleRequest {
  folderId: string;
  ruleId?: string;
  enabled: boolean;
  applicationIdentity: string;
  identityKind: ApplicationIdentityKind;
  deviceId: string;
  returnOnFocusLoss: boolean;
}

export interface SetFolderFocusRuleResponse extends ResultResponse {
  rule?: FolderFocusRule;
}

export interface DeleteFolderFocusRuleRequest {
  folderId: string;
  ruleId: string;
}

export interface DeleteFolderFocusRuleResponse extends ResultResponse {}

export interface FolderFocusRuleChangedEvent {
  folderId: string;
  rules: FolderFocusRule[];
}

export interface FolderFocusRuleRemovedEvent {
  folderId: string;
  ruleId: string;
}
