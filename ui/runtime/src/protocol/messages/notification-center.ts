export type UserNotificationSeverity = 'Info' | 'Warning' | 'Error';

export type UserNotificationKind =
  | 'Error'
  | 'Update'
  | 'Integration'
  | 'IconImport'
  | 'Security'
  | 'General';

export type UserNotificationActionKind =
  | 'None'
  | 'OpenIntegration'
  | 'OpenLogs'
  | 'OpenIconPacks'
  | 'OpenUpdateSettings'
  | 'OpenExtensionStore'
  | 'RestartApplication'
  | 'OpenUpdateDetails'
  | 'InstallUpdate'
  | 'DismissNotification';

export interface UserNotificationAction {
  kind: UserNotificationActionKind;
  target?: string;
}

export interface UserNotificationProgress {
  processed: number;
  total: number | null;
}

export interface UserNotification {
  id: string;
  sequence: number;
  timestamp: string;
  severity: UserNotificationSeverity;
  kind: UserNotificationKind;
  title: string;
  message?: string;
  sourceId?: string;
  sourceName?: string;
  action?: UserNotificationAction;
  actions?: UserNotificationAction[];
  progress?: UserNotificationProgress | null;
  cancelKey?: string | null;
}

export interface GetUserNotificationsResponse {
  notifications: UserNotification[];
}

export interface UserNotificationsChangedEvent {
  notifications: UserNotification[];
}
