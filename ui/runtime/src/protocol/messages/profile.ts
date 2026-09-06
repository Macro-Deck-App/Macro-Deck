import { ResultResponse } from './common';

export interface IpcProfileLayoutConstraint {
  rows: number;
  columns: number;
  minRows: number;
  maxRows: number;
  minColumns: number;
  maxColumns: number;
  rowsLocked: boolean;
  columnsLocked: boolean;
  layoutId?: string;
  layoutName?: string;
  deviceName?: string;
  widgetSpacingHonoured?: boolean;
  cornerRadiusHonoured?: boolean;
  customFolderViewsSupported?: boolean;
}

export type IpcProfileLayoutCompatibilityStatus = 'ok' | 'conflictingDevices' | 'exceedsLayout';

export interface IpcProfileLayoutCompatibility {
  status: IpcProfileLayoutCompatibilityStatus;
  deviceNames: string[];
}

export interface IpcProfileLayout {
  rows: number;
  columns: number;
  rowsLocked: boolean;
  columnsLocked: boolean;
  constraint?: IpcProfileLayoutConstraint;
  compatibility?: IpcProfileLayoutCompatibility;
}

export interface IpcProfile {
  id: string;
  name: string;
  order: number;
  layoutType: string;
  isVirtual: boolean;
  sourceIntegrationId?: string;
  layout: IpcProfileLayout;
  defaultRows: number;
  defaultColumns: number;
  defaultBackgroundColor?: string;
  defaultWidgetSpacing?: number;
  defaultWidgetBorderRadius?: number;
}

export interface GetProfilesResponse {
  profiles: IpcProfile[];
}

export interface CreateProfileRequest {
  name: string;
  defaultRows?: number;
  defaultColumns?: number;
  defaultBackgroundColor?: string;
  defaultWidgetSpacing?: number;
  defaultWidgetBorderRadius?: number;
}

export interface CreateProfileResponse extends ResultResponse {
  profile?: IpcProfile;
}

export interface UpdateProfileRequest {
  id: string;
  name?: string;
  order?: number;
  defaultRows?: number;
  defaultColumns?: number;
  defaultBackgroundColor?: string;
  defaultWidgetSpacing?: number;
  defaultWidgetBorderRadius?: number;
}

export interface UpdateProfileResponse extends ResultResponse {
  profile?: IpcProfile;
}

export interface DeleteProfileRequest {
  id: string;
}

export interface DeleteProfileResponse extends ResultResponse {}

export interface ProfileCreatedEvent {
  profile: IpcProfile;
}

export interface ProfileUpdatedEvent {
  profile: IpcProfile;
}

export interface ProfileDeletedEvent {
  profileId: string;
}
