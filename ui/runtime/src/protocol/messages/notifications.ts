import { LocalizedText } from '../../localization/localized-text';
import { UiNode } from '../../ui-framework/ui-node.interface';

export type NotificationSeverity = 'info' | 'success' | 'warning' | 'error';

export type NotificationPopupKind = 'none' | 'toast' | 'dialog';

export type NotificationSourceCategory = 'system' | 'built-in-integration' | 'plugin';

export type NotificationActionStyle = 'default' | 'primary' | 'danger';

export type NotificationCloseReason =
  | 'action'
  | 'dismissed'
  | 'timeout'
  | 'programmatic'
  | 'owner-gone'
  | 'host-unavailable';

export interface NotificationActionDto {
  id: string;
  label: LocalizedText;
  style: NotificationActionStyle;
}

export interface NotificationProgressDto {
  processed: number;
  total?: number | null;
}

export interface ActiveNotification {
  id: string;
  revision: number;
  sourceCategory: NotificationSourceCategory;
  sourceId: string;
  sourceName: string;
  title: LocalizedText;
  message?: LocalizedText;
  severity: NotificationSeverity;
  popup: NotificationPopupKind;
  inbox: boolean;
  dismissible: boolean;
  createdAt: string;
  expiresAt?: string;
  expiresInMs?: number;
  actions: NotificationActionDto[];
  content?: UiNode;
  progress?: NotificationProgressDto;
}

export interface NotificationShownEvent {
  notification: ActiveNotification;
}

export interface NotificationUpdatedEvent {
  notification: ActiveNotification;
}

export interface NotificationClosedEvent {
  id: string;
  revision: number;
  reason: NotificationCloseReason;
  actionId?: string;
}

export interface ActiveNotificationsEvent {
  notifications: ActiveNotification[];
}

export interface RespondToNotificationRequest {
  id: string;
  actionId?: string | null;
  values?: Record<string, unknown>;
}

export type RespondToNotificationStatus =
  | 'accepted'
  | 'already-closed'
  | 'not-found'
  | 'forbidden'
  | 'invalid';

export interface RespondToNotificationResponse {
  status: RespondToNotificationStatus;
  message?: string;
}
