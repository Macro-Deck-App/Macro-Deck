import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject, input } from '@angular/core';
import { Router } from '@angular/router';

import { AppStrings, UserNotification } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { IconPackService } from '../../../services/icon-pack.service';
import { NotificationCenterService } from '../../../services/notification-center.service';
import { RestartNoticeService, SettingsModalService, UpdateModalService, UpdateService } from '../../../services';
import { NotificationActionEvent, NotificationItemComponent } from './notification-item.component';

@Component({
  selector: 'app-notification-panel',
  standalone: true,
  imports: [EmptyStateComponent, NotificationItemComponent, TranslatePipe],
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
  private readonly restartNotice = inject(RestartNoticeService);

  readonly isOpen = input(false);

  @Output() closed = new EventEmitter<void>();

  protected readonly notifications = this.notificationCenter.notifications;

  onClearAll(): void {
    this.notificationCenter.dismissAll();
  }

  onDismiss(id: string): void {
    this.notificationCenter.dismiss(id);
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
      case 'OpenUpdateSettings':
        this.settingsModal.open('about');
        break;
      case 'OpenExtensionStore':
        void this.router.navigate(['/store']);
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
        this.notificationCenter.dismiss(notification.id);
        return;
      case 'None':
        break;
    }

    this.closed.emit();
  }

  private async restartApplication(): Promise<void> {
    if (!this.restartNotice.required()) {
      await this.restartNotice.refresh();
    }
    await this.restartNotice.restartNow();
  }

  onCancel(notification: UserNotification): void {
    if (notification.kind === 'IconImport' && notification.cancelKey) {
      void this.iconPacks.cancelBatch(notification.cancelKey);
    } else if (notification.kind === 'Update' && notification.cancelKey) {
      void this.updateService.cancelDownload();
    }
  }
}
