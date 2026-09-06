export interface ProfileLayoutConstraint {
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

export type ProfileLayoutCompatibilityStatus = 'ok' | 'conflictingDevices' | 'exceedsLayout';

export interface ProfileLayoutCompatibility {
  status: ProfileLayoutCompatibilityStatus;
  deviceNames: string[];
}

export interface ProfileLayout {
  rows: number;
  columns: number;
  rowsLocked: boolean;
  columnsLocked: boolean;
  constraint?: ProfileLayoutConstraint;
  compatibility?: ProfileLayoutCompatibility;
}

export interface Profile {
  id: string;
  name: string;
  order: number;
  layoutType: string;
  isVirtual: boolean;
  sourceIntegrationId?: string;
  layout: ProfileLayout;
  defaultRows: number;
  defaultColumns: number;
  defaultBackground: string | null;
  defaultSpacing: number | null;
  defaultBorderRadius: number | null;
}
