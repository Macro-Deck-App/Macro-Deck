import {
  ClientAppStrings,
  type ActiveNotification,
  type LocalizationTranslator,
  type NotificationSourceCategory,
} from '@macro-deck/runtime';
import type { ToastSource } from './ui';

function categoryKey(category: NotificationSourceCategory): string {
  switch (category) {
    case 'built-in-integration':
      return ClientAppStrings.Feedback.Notification.Source.BuiltInIntegration;
    case 'plugin':
      return ClientAppStrings.Feedback.Notification.Source.Plugin;
    default:
      return ClientAppStrings.Feedback.Notification.Source.System;
  }
}

export function translateKey(localization: LocalizationTranslator, key: string): string {
  const separator = key.indexOf(':');
  return localization.translate(key.slice(0, separator), key.slice(separator + 1));
}

// The host derives the source, so a plugin's popup always says it is one and cannot pass for the system.
export function sourceOf(notification: ActiveNotification, localization: LocalizationTranslator): ToastSource {
  const kind = translateKey(localization, categoryKey(notification.sourceCategory));
  const name = notification.sourceName === kind ? '' : notification.sourceName || '';
  return { kind, name };
}
