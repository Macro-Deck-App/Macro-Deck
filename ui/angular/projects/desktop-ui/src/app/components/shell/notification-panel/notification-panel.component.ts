import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router } from '@angular/router';

import { AppStrings, UserNotification } from '@macro-deck/runtime';
import { LocalizationService, SidePanelComponent, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { DropdownMenuComponent } from '../../overlay/dropdown-menu/dropdown-menu.component';
import { TooltipDirective } from '../../overlay/tooltip/tooltip.directive';
import { BackupService } from '../../../services/backup.service';
import { IconPackService } from '../../../services/icon-pack.service';
import { ExternalLinkService } from '../../../services/external-link.service';
import { NotificationCenterService } from '../../../services/notification-center.service';
import { PluginAdbConsentService } from '../../../services/plugin-adb-consent.service';
import { PluginCompanionConsentService } from '../../../services/plugin-companion-consent.service';
import { RestartNoticeService, SettingsModalService, UpdateModalService, UpdateService } from '../../../services';
import { BACKUP_CANCEL_KEY_PREFIX } from '../../../util/backup-progress-display';
import { NotificationActionEvent, NotificationItemComponent, requiresAttention } from './notification-item.component';

const DOWNLOAD_PAGE_URL = 'https://macro-deck.app/download';
const TROUBLESHOOTING_GUIDE_URL = 'https://docs.macro-deck.app/guide/troubleshooting/';

@Component({
  selector: 'app-notification-panel',
  standalone: true,
  imports: [
    DropdownMenuComponent,
    EmptyStateComponent,
    NgTemplateOutlet,
    NotificationItemComponent,
    SidePanelComponent,
    TooltipDirective,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notification-panel.component.html',
  styleUrls: ['./notification-panel.component.scss'],
})
export class NotificationPanelComponent {
  protected readonly notificationCenter = inject(NotificationCenterService);
  protected readonly localization = inject(LocalizationService);
  protected readonly appStrings = AppStrings;
  private readonly router = inject(Router);
  private readonly settingsModal = inject(SettingsModalService);
  private readonly updateModal = inject(UpdateModalService);
  private readonly updateService = inject(UpdateService);
  private readonly iconPacks = inject(IconPackService);
  private readonly backups = inject(BackupService);
  private readonly restartNotice = inject(RestartNoticeService);
  private readonly adbConsent = inject(PluginAdbConsentService);
  private readonly companionConsent = inject(PluginCompanionConsentService);
  private readonly externalLinks = inject(ExternalLinkService);

  readonly isOpen = input(false);

  @Output() closed = new EventEmitter<void>();

  protected readonly notifications = this.notificationCenter.notifications;
  protected readonly attention = computed(() => this.notifications().filter(requiresAttention));
  protected readonly earlier = computed(() => this.notifications().filter(n => !requiresAttention(n)));
  protected readonly showSections = computed(() => this.attention().length > 0 && this.earlier().length > 0);
  protected readonly menuOpen = signal(false);

  protected isPending(notification: UserNotification): boolean {
    return this.adbConsent.allowing() === notification.id || this.companionConsent.allowing() === notification.id;
  }

  onClearAll(): void {
    this.notificationCenter.dismissAll();
    this.closeWhenEmpty();
  }

  onDismiss(id: string): void {
    this.notificationCenter.dismiss(id);
    this.closeWhenEmpty();
  }

  onMarkRead(id: string): void {
    this.notificationCenter.markRead(id);
  }

  onMarkAllRead(): void {
    this.notificationCenter.markAllRead();
  }

  onAction(event: NotificationActionEvent): void {
    const { notification, action } = event;

    switch (action.kind) {
      case 'OpenIntegration':
        if (action.target) {
          void this.router.navigate(['/integrations', action.target]);
        }
        break;
      case 'OpenLogs':
        void this.router.navigate(['/developer'], { queryParams: { tab: 'logs' } });
        break;
      case 'OpenIconPacks':
        void this.router.navigate(['/library/icon-packs']);
        break;
      case 'OpenSecretApprovals':
        void this.router.navigate(['/approvals']);
        break;
      case 'OpenUpdateSettings':
        this.settingsModal.open('about');
        break;
      case 'OpenExtensionStore':
        void this.router.navigate(action.target === 'installed' ? ['/store', 'installed'] : ['/store']);
        break;
      case 'RestartApplication':
        void this.restartApplication();
        break;
      case 'OpenUpdateDetails':
        this.updateModal.open();
        break;
      case 'InstallUpdate':
        void this.updateService.install();
        break;
      case 'DismissNotification':
        this.onDismiss(notification.id);
        return;
      case 'EnablePluginAdb':
        void this.allowPluginsToUseAdb(notification.id);
        return;
      case 'EnablePluginCompanionDevices':
        void this.allowPluginsToUseCompanionDevices(notification.id);
        return;
      case 'OpenDownloadPage':
        this.externalLinks.open(DOWNLOAD_PAGE_URL);
        break;
      case 'OpenTroubleshootingGuide':
        this.externalLinks.open(
          action.target ? `${TROUBLESHOOTING_GUIDE_URL}#${encodeURIComponent(action.target)}` : TROUBLESHOOTING_GUIDE_URL,
        );
        break;
      case 'None':
        break;
    }

    this.closed.emit();
  }

  private closeWhenEmpty(): void {
    if (!this.notificationCenter.hasNotifications()) {
      this.closed.emit();
    }
  }

  private async allowPluginsToUseAdb(notificationId: string): Promise<void> {
    if (!await this.adbConsent.allow(notificationId)) {
      this.closed.emit();
    }
  }

  private async allowPluginsToUseCompanionDevices(notificationId: string): Promise<void> {
    if (!await this.companionConsent.allow(notificationId)) {
      this.closed.emit();
    }
  }

  private async restartApplication(): Promise<void> {
    if (!this.restartNotice.required()) {
      await this.restartNotice.refresh();
    }
    await this.restartNotice.restartNow();
  }

  onShowBackupDetails(): void {
    this.backups.showProgressDialog();
    this.settingsModal.open('backups');
    this.closed.emit();
  }

  onCancel(notification: UserNotification): void {
    if (notification.kind === 'IconImport' && notification.cancelKey) {
      void this.iconPacks.cancelBatch(notification.cancelKey);
    } else if (notification.kind === 'Update' && notification.cancelKey) {
      void this.updateService.cancelDownload();
    } else if (notification.cancelKey?.startsWith(BACKUP_CANCEL_KEY_PREFIX)) {
      void this.backups.cancelOperation(notification.cancelKey.slice(BACKUP_CANCEL_KEY_PREFIX.length));
    }
  }
}
