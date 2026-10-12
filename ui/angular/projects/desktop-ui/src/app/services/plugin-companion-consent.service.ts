import { Injectable, computed, inject, signal } from '@angular/core';
import { UserNotification } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { NotificationCenterService } from './notification-center.service';
import { SettingsModalService } from './settings-modal.service';

@Injectable({ providedIn: 'root' })
export class PluginCompanionConsentService {
  private readonly api = inject(ApiService);
  private readonly notificationCenter = inject(NotificationCenterService);
  private readonly settingsModal = inject(SettingsModalService);

  private readonly deferred = signal<ReadonlySet<string>>(new Set());

  readonly pending = computed<UserNotification | null>(() => {
    const deferred = this.deferred();
    return this.notificationCenter.notifications().find(notification =>
      !deferred.has(notification.id) && isCompanionConsentRequest(notification)) ?? null;
  });

  async allow(notificationId: string): Promise<boolean> {
    const status = await this.api.updateCompanionAppPluginAccess(true).catch(() => null);
    if (!status?.allowPlugins) {
      this.defer(notificationId);
      this.settingsModal.open('license');
      return false;
    }
    this.notificationCenter.dismiss(notificationId);
    return true;
  }

  defer(notificationId: string): void {
    this.deferred.update(deferred => new Set(deferred).add(notificationId));
  }
}

function isCompanionConsentRequest(notification: UserNotification): boolean {
  return (notification.actions ?? []).some(action => action.kind === 'EnablePluginCompanionDevices')
    || notification.action?.kind === 'EnablePluginCompanionDevices';
}
