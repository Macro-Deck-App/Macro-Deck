import {
  AppStrings,
  AppStringsKey,
  NotificationActionStyle,
  NotificationProgressDto,
  NotificationSourceCategory,
} from '@macro-deck/runtime';

import type { ButtonVariant } from '../components/button/button.component';

const SOURCE_KEYS: Record<NotificationSourceCategory, AppStringsKey> = {
  system: AppStrings.Feedback.Notification.Source.System,
  'built-in-integration': AppStrings.Feedback.Notification.Source.BuiltInIntegration,
  plugin: AppStrings.Feedback.Notification.Source.Plugin,
};

const ACTION_VARIANTS: Record<NotificationActionStyle, ButtonVariant> = {
  primary: 'primary',
  danger: 'danger',
  default: 'secondary',
};

export interface NotificationSourceView {
  categoryKey: AppStringsKey;
  name: string | null;
}

export function notificationSource(category: NotificationSourceCategory, name: string): NotificationSourceView {
  return {
    categoryKey: SOURCE_KEYS[category] ?? SOURCE_KEYS.plugin,
    name: category === 'system' || !name ? null : name,
  };
}

export function notificationActionVariant(style: NotificationActionStyle): ButtonVariant {
  return ACTION_VARIANTS[style] ?? 'secondary';
}

export function notificationProgressPercent(progress: NotificationProgressDto | undefined): number | null {
  if (!progress || !progress.total || progress.total <= 0) {
    return null;
  }

  return Math.max(0, Math.min(100, Math.round((progress.processed / progress.total) * 100)));
}
