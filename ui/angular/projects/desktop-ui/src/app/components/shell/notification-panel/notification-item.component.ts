import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject } from '@angular/core';

import {
  AppStrings,
  Strings,
  UserNotification,
  UserNotificationAction,
  UserNotificationActionKind,
  UserNotificationSeverity,
} from '@macro-deck/runtime';
import {
  LocalizationService,
  TranslatePipe,
} from '@shared';

const SEVERITY_ICON: Record<UserNotificationSeverity, string> = {
  Info: 'icon-info',
  Warning: 'icon-alert-triangle',
  Error: 'icon-alert-triangle',
};

const SEVERITY_CLASS: Record<UserNotificationSeverity, string> = {
  Info: 'issue-badge-info',
  Warning: 'issue-badge-warning',
  Error: 'issue-badge-error',
};

const ACTION_LABEL: Record<Exclude<UserNotificationActionKind, 'None'>, string> = {
  OpenIntegration: AppStrings.Shell.Notifications.Action.OpenIntegration,
  OpenLogs: AppStrings.Shell.Notifications.Action.OpenLogs,
  OpenIconPacks: AppStrings.Shell.Notifications.Action.OpenIconPacks,
  OpenUpdateSettings: AppStrings.Shell.Notifications.Action.OpenUpdateSettings,
  OpenExtensionStore: AppStrings.Shell.Notifications.Action.OpenExtensionStore,
  RestartApplication: AppStrings.Shell.Notifications.Action.RestartApplication,
  OpenUpdateDetails: AppStrings.Shell.Notifications.Action.OpenUpdateDetails,
  InstallUpdate: AppStrings.Shell.Notifications.Action.InstallUpdate,
  DismissNotification: AppStrings.Shell.Notifications.Action.DismissNotification,
};

export interface NotificationActionEvent {
  notification: UserNotification;
  action: UserNotificationAction;
}

@Component({
  selector: 'app-notification-item',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notification-item.component.html',
  styleUrls: ['./notification-item.component.scss'],
})
export class NotificationItemComponent {
  private readonly localization = inject(LocalizationService);
  protected readonly appStrings = AppStrings;
  protected readonly commonStrings = Strings;

  @Input({ required: true }) notification!: UserNotification;

  @Output() action = new EventEmitter<NotificationActionEvent>();
  @Output() dismissed = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<UserNotification>();

  protected get severityIcon(): string {
    return SEVERITY_ICON[this.notification.severity];
  }

  protected get severityClass(): string {
    return SEVERITY_CLASS[this.notification.severity];
  }

  protected get actionList(): UserNotificationAction[] {
    const notification = this.notification;
    if (notification.actions && notification.actions.length > 0) {
      return notification.actions.filter(action => action.kind !== 'None');
    }
    return notification.action && notification.action.kind !== 'None' ? [notification.action] : [];
  }

  protected labelFor(action: UserNotificationAction): string {
    if (action.kind === 'None') {
      return '';
    }
    return this.localization.translateKey(ACTION_LABEL[action.kind]);
  }

  protected get relativeTime(): string {
    return formatRelativeTime(this.notification.timestamp, (key, args) => this.localization.translateKey(key, args));
  }

  protected get progressPercent(): number | null {
    const progress = this.notification.progress;
    if (!progress || !progress.total || progress.total <= 0) {
      return null;
    }

    return Math.min(100, Math.round((progress.processed / progress.total) * 100));
  }

  protected get progressLabel(): string {
    const progress = this.notification.progress;
    if (!progress) {
      return '';
    }

    return progress.total != null
      ? this.localization.translateKey(AppStrings.Shell.Notifications.ProcessingOfTotal, {
        processed: progress.processed,
        total: progress.total,
      })
      : this.localization.translateKey(AppStrings.Shell.Notifications.ProcessingCount, { processed: progress.processed });
  }

  onAction(event: MouseEvent, action: UserNotificationAction): void {
    event.stopPropagation();
    this.action.emit({ notification: this.notification, action });
  }

  onDismiss(event: MouseEvent): void {
    event.stopPropagation();
    this.dismissed.emit(this.notification.id);
  }

  onCancel(event: MouseEvent): void {
    event.stopPropagation();
    this.cancel.emit(this.notification);
  }
}

function formatRelativeTime(timestamp: string, translate: (key: string, args?: Record<string, unknown>) => string): string {
  const then = new Date(timestamp).getTime();
  if (Number.isNaN(then)) {
    return '';
  }

  const diffSeconds = Math.round((Date.now() - then) / 1000);
  if (diffSeconds < 60) {
    return translate(AppStrings.Shell.Notifications.JustNow);
  }

  const diffMinutes = Math.round(diffSeconds / 60);
  if (diffMinutes < 60) {
    return translate(AppStrings.Shell.Notifications.MinutesAgo, { count: diffMinutes });
  }

  const diffHours = Math.round(diffMinutes / 60);
  if (diffHours < 24) {
    return translate(AppStrings.Shell.Notifications.HoursAgo, { count: diffHours });
  }

  const diffDays = Math.round(diffHours / 24);
  if (diffDays < 7) {
    return translate(AppStrings.Shell.Notifications.DaysAgo, { count: diffDays });
  }

  return new Date(timestamp).toLocaleDateString();
}
