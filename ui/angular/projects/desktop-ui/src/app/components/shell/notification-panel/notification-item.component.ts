import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, booleanAttribute, inject } from '@angular/core';

import {
  AppStrings,
  GetBackupStatusResponse,
  Strings,
  UserNotification,
  UserNotificationAction,
  UserNotificationActionKind,
  UserNotificationSeverity,
  UserNotificationSourceCategory,
} from '@macro-deck/runtime';
import {
  ButtonComponent,
  ButtonVariant,
  LocalizationService,
  TranslatePipe,
} from '@shared';
import { TooltipDirective } from '../../overlay/tooltip/tooltip.directive';
import { BackupService } from '../../../services/backup.service';
import { BackupProgressCardComponent } from '../../backup/backup-progress-card/backup-progress-card.component';
import { BACKUP_CANCEL_KEY_PREFIX, isActiveBackupStage } from '../../../util/backup-progress-display';

const SEVERITY_ICON: Record<UserNotificationSeverity, string> = {
  Info: 'icon-info',
  Success: 'icon-circle-check',
  Warning: 'icon-alert-triangle',
  Error: 'icon-circle-x',
};

const SEVERITY_CLASS: Record<UserNotificationSeverity, string> = {
  Info: 'ni-glyph-info',
  Success: 'ni-glyph-success',
  Warning: 'ni-glyph-warning',
  Error: 'ni-glyph-error',
};

const SOURCE_CATEGORY_LABEL: Record<UserNotificationSourceCategory, string> = {
  System: AppStrings.Feedback.Notification.Source.System,
  BuiltInIntegration: AppStrings.Feedback.Notification.Source.BuiltInIntegration,
  Plugin: AppStrings.Feedback.Notification.Source.Plugin,
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
  EnablePluginAdb: AppStrings.Shell.Notifications.Action.EnablePluginAdb,
  OpenDownloadPage: AppStrings.Settings.Update.OpenDownloadPageAction,
  OpenTroubleshootingGuide: AppStrings.Shell.Notifications.Action.OpenTroubleshootingGuide,
  EnablePluginCompanionDevices: AppStrings.Shell.Notifications.Action.EnablePluginAdb,
  OpenSecretApprovals: AppStrings.Shell.Notifications.Action.OpenSecretApprovals,
};

const ATTENTION_ACTIONS: ReadonlySet<UserNotificationActionKind> = new Set<UserNotificationActionKind>([
  'EnablePluginAdb',
  'EnablePluginCompanionDevices',
  'InstallUpdate',
  'RestartApplication',
]);

export interface NotificationActionEvent {
  notification: UserNotification;
  action: UserNotificationAction;
}

export function notificationActions(notification: UserNotification): UserNotificationAction[] {
  if (notification.actions && notification.actions.length > 0) {
    return notification.actions.filter(action => action.kind !== 'None');
  }
  return notification.action && notification.action.kind !== 'None' ? [notification.action] : [];
}

export function requiresAttention(notification: UserNotification): boolean {
  return notificationActions(notification).some(action => ATTENTION_ACTIONS.has(action.kind));
}

@Component({
  selector: 'app-notification-item',
  standalone: true,
  imports: [BackupProgressCardComponent, ButtonComponent, TooltipDirective, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notification-item.component.html',
  styleUrls: ['./notification-item.component.scss'],
})
export class NotificationItemComponent {
  private readonly localization = inject(LocalizationService);
  private readonly backups = inject(BackupService);
  protected readonly appStrings = AppStrings;
  protected readonly commonStrings = Strings;

  @Input({ required: true }) notification!: UserNotification;
  @Input({ transform: booleanAttribute }) attention = false;
  @Input({ transform: booleanAttribute }) pending = false;

  @Output() action = new EventEmitter<NotificationActionEvent>();
  @Output() dismissed = new EventEmitter<string>();
  @Output() markRead = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<UserNotification>();
  @Output() showDetails = new EventEmitter<UserNotification>();

  protected get severityIcon(): string {
    return SEVERITY_ICON[this.notification.severity];
  }

  protected get severityClass(): string {
    return SEVERITY_CLASS[this.notification.severity];
  }

  protected get isUnread(): boolean {
    return this.notification.read === false;
  }

  protected get sourceCategoryLabel(): string | null {
    const category = this.notification.sourceCategory;
    return category ? this.localization.translateKey(SOURCE_CATEGORY_LABEL[category] ?? SOURCE_CATEGORY_LABEL.Plugin) : null;
  }

  protected get sourceName(): string | null {
    const { sourceCategory, sourceName } = this.notification;
    return sourceCategory && sourceCategory !== 'System' && sourceName ? sourceName : null;
  }

  protected get actionList(): UserNotificationAction[] {
    return notificationActions(this.notification);
  }

  protected variantFor(action: UserNotificationAction): ButtonVariant {
    if (action.kind === 'DismissNotification') {
      return 'ghost';
    }
    return this.attention && ATTENTION_ACTIONS.has(action.kind) ? 'primary' : 'secondary';
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

  protected get runningBackup(): GetBackupStatusResponse | null {
    const operation = this.backups.operation();
    return this.notification.cancelKey?.startsWith(BACKUP_CANCEL_KEY_PREFIX) && operation && isActiveBackupStage(operation.stage)
      ? operation
      : null;
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
    if (this.pending) {
      return;
    }
    this.action.emit({ notification: this.notification, action });
  }

  onDismiss(event: MouseEvent): void {
    event.stopPropagation();
    this.dismissed.emit(this.notification.id);
  }

  onMarkRead(event: MouseEvent): void {
    event.stopPropagation();
    this.markRead.emit(this.notification.id);
  }

  onShowDetails(event: MouseEvent): void {
    event.stopPropagation();
    this.showDetails.emit(this.notification);
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
